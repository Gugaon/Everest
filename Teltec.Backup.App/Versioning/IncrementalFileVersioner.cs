﻿using NLog;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teltec.Backup.App.DAO;
using Teltec.Backup.App.Models;
using Teltec.Common.Extensions;
using Teltec.Storage;
using Teltec.Storage.Versioning;

namespace Teltec.Backup.App.Versioning
{
	public sealed class IncrementalFileVersioner : IDisposable
	{
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		CancellationTokenSource CancellationTokenSource; // IDisposable

		public IncrementalFileVersioner()
		{
			CancellationTokenSource = new CancellationTokenSource();
		}

		public async Task NewVersion(Models.Backup backup, LinkedList<string> files)
		{
			await DoVersion(backup, files, true);
		}

		public async Task ResumeVersion(Models.Backup backup, LinkedList<string> files)
		{
			await DoVersion(backup, files, false);
		}

		public async Task DoVersion(Models.Backup backup, LinkedList<string> filePaths, bool newVersion)
		{
			Assert.IsNotNull(backup);
			Assert.AreEqual(TransferStatus.RUNNING, backup.Status);
			Assert.IsNotNull(filePaths);

			Backup = backup;

			BackupPlanFileRepository daoBackupPlanFile = new BackupPlanFileRepository();
			AllFilesFromPlan = daoBackupPlanFile.GetAllByPlan(backup.BackupPlan).ToDictionary<BackupPlanFile, string>(p => p.Path);

			await ExecuteOnBackround(() =>
			{
				Execute(backup, filePaths, newVersion);
			}, CancellationTokenSource.Token);
		}

		public void Cancel()
		{
			CancellationTokenSource.Cancel();
		}

		private Task ExecuteOnBackround(Action action, CancellationToken token)
		{
			return Task.Run(action, token);
			//return AsyncHelper.ExecuteOnBackround(action, token);
		}

		#region File change detection

		private bool IsFileModified(BackupPlanFile file)
		{
			Assert.IsNotNull(file);

			DateTime dt1 = file.LastWrittenAt;
			DateTime dt2 = FileManager.GetLastWriteTimeUtc(file.Path).Value;

			// Strip milliseconds off from both dates!
			dt1 = dt1.AddTicks(-(dt1.Ticks % TimeSpan.TicksPerSecond));
			dt2 = dt2.AddTicks(-(dt2.Ticks % TimeSpan.TicksPerSecond));

			return DateTime.Compare(dt1, dt2) != 0;
		}

		#endregion

		bool IsSaved = false;
		Models.Backup Backup;

		// Contains ALL `BackupPlanFile`s that were registered at least once for the plan associated to this backup.
		// Fact 1: ALL of its items are also contained (distributed) in:
		//		`ChangeSet.RemovedFiles`
		//		`ChangeSet.DeletedFiles`
		//		`SuppliedFiles`
		Dictionary<string, BackupPlanFile> AllFilesFromPlan;

		// Contains ALL `BackupPlanFile`s that were informed to be included in this backup.
		// Fact 1: ALL of its items are also contained in `AllFilesFromPlan`.
		// Fact 2: SOME of its items may also be contained in `ChangeSet.AddedFiles`.
		// Fact 3: SOME of its items may also be contained in `ChangeSet.ModifiedFiles`.
		// Fact 4: SOME of its items may also be contained in `ChangeSet.RemovedFiles`.
		// Fact 5: SOME of its items may also be contained in `ChangeSet.DeletedFiles`.
		LinkedList<BackupPlanFile> SuppliedFiles;

		// Contains the relation of all `BackupPlanFile`s that will be listed on this backup,
		// be it an addition, deletion, modification, or removal.
		ChangeSet<BackupPlanFile> ChangeSet = new ChangeSet<BackupPlanFile>();

		// After `Save()`, contains ALL `CustomVersionedFile`s that are eligible for transfer - those whose status is ADDED or MODIFIED.
		TransferSet<CustomVersionedFile> TransferSet = new TransferSet<CustomVersionedFile>();

		public IEnumerable<CustomVersionedFile> FilesToTransfer
		{
			get
			{
				Assert.IsTrue(IsSaved);
				return TransferSet.Files;
			}
		}

		private void Execute(Models.Backup backup, LinkedList<string> filePaths, bool isNewVersion)
		{
			// The `filePaths` argument contains the filesystem paths informed by the user for this backup.

			//
			// NOTE: The methods call ORDER is important!
			//

			SuppliedFiles = DoLoadOrCreateBackupPlanFiles(backup.BackupPlan, filePaths);
			DoUpdateBackupPlanFilesStatus(SuppliedFiles, isNewVersion);

			ChangeSet.AddedFiles = GetAddedFiles();
			foreach (var item in ChangeSet.AddedFiles)
				Console.WriteLine("BackupPlanAddedFiles: {0}", item.Path);

			ChangeSet.ModifiedFiles = GetModifiedFiles();
			foreach (var item in ChangeSet.ModifiedFiles)
				Console.WriteLine("BackupPlanModifiedFiles: {0}", item.Path);

			// DO NOT update files removal and deletion status for `ResumeBackupOperation`,
			// only for `NewBackupOperation`.
			if (isNewVersion)
			{
				ChangeSet.RemovedFiles = GetRemovedFiles();
				foreach (var item in ChangeSet.RemovedFiles)
					Console.WriteLine("BackupPlanRemovedFiles: {0}", item.Path);
				ChangeSet.DeletedFiles = GetDeletedFilesAndUpdateTheirStatus(SuppliedFiles);
			}

			DoUpdateFilesProperties(SuppliedFiles);
			//throw new Exception("Simulating failure.");
		}

		//
		// Loads or creates `BackupPlanFile`s for each file in `files`.
		// Returns the complete list of `BackupPlanFile`s that are related to `files`.
		// It modifies the `UserData` property for each file in `files`.
		// NOTE: Does not save to the database because this method is run by a secondary thread.
		//
		private LinkedList<BackupPlanFile> DoLoadOrCreateBackupPlanFiles(Models.BackupPlan plan, LinkedList<string> filePaths)
		{
			Assert.IsNotNull(plan);
			Assert.IsNotNull(filePaths);
			Assert.IsNotNull(AllFilesFromPlan);

			LinkedList<BackupPlanFile> result = new LinkedList<BackupPlanFile>();

			// Check all files.
			foreach (string path in filePaths)
			{
				// Throw if the operation was canceled.
				CancellationTokenSource.Token.ThrowIfCancellationRequested();

				//
				// Create or update `BackupPlanFile`.
				//
				BackupPlanFile backupPlanFile = null;
				bool backupPlanFileAlreadyExists = AllFilesFromPlan.TryGetValue(path, out backupPlanFile);

				if (!backupPlanFileAlreadyExists)
				{
					backupPlanFile = new BackupPlanFile(plan, path);
					backupPlanFile.CreatedAt = DateTime.UtcNow;
				}

				result.AddLast(backupPlanFile);
			}

			return result;
		}

		//
		// Summary:
		// Update the `LastStatus` property of each file in `files` according to the actual
		// state of the file in the filesystem.
		// NOTE: This function has a side effect - It updates properties of items from `files`.
		//
		private void DoUpdateBackupPlanFilesStatus(LinkedList<BackupPlanFile> files, bool isNewVersion)
		{
			Assert.IsNotNull(files);

			// Check all files.
			foreach (BackupPlanFile entry in files)
			{
				// Throw if the operation was canceled.
				CancellationTokenSource.Token.ThrowIfCancellationRequested();

				//
				// Check what happened to the file.
				//

				bool fileExistsOnFilesystem = File.Exists(entry.Path);
				BackupFileStatus? changeStatusTo = null;

				if (entry.Id.HasValue) // File was backed up at least once in the past?
				{
					switch (entry.LastStatus)
					{
						case BackupFileStatus.DELETED: // File was marked as DELETED by a previous backup?
							if (fileExistsOnFilesystem) // Exists?
								changeStatusTo = BackupFileStatus.ADDED;
							break;
						case BackupFileStatus.REMOVED: // File was marked as REMOVED by a previous backup?
							if (fileExistsOnFilesystem) // Exists?
								changeStatusTo = BackupFileStatus.ADDED;
							else
								// QUESTION: Do we really care to transition REMOVED to DELETED?
								changeStatusTo = BackupFileStatus.DELETED;
							break;
						default: // ADDED, MODIFIED, UNMODIFIED
							if (fileExistsOnFilesystem) // Exists?
							{
								// DO NOT verify whether the file changed for a `ResumeBackupOperation`,
								// only for `NewBackupOperation`.
								if (isNewVersion)
								{
									if (IsFileModified(entry)) // Modified?
									{
										changeStatusTo = BackupFileStatus.MODIFIED;
									}
									else // Not modified?
									{
										changeStatusTo = BackupFileStatus.UNCHANGED;
									}
								}
							}
							else // Deleted from filesystem?
							{
								changeStatusTo = BackupFileStatus.DELETED;
							}
							break;
					}
				}
				else // Adding to this backup?
				{
					if (fileExistsOnFilesystem) // Exists?
					{
						changeStatusTo = BackupFileStatus.ADDED;
					}
					else
					{
						// Error? Can't add a non-existent file to the plan.
					}
				}

				if (changeStatusTo.HasValue)
				{
					entry.LastStatus = changeStatusTo.Value;
					entry.UpdatedAt = DateTime.UtcNow;
				}
			}
		}

		//
		// Summary:
		// Return all files from `SuppliedFiles` which are marked as `ADDED`;
		// NOTE: This function has no side effects.
		//
		private IEnumerable<BackupPlanFile> GetAddedFiles()
		{
			Assert.IsNotNull(SuppliedFiles);

			// Find all `BackupPlanFile`s from this `BackupPlan` that are marked as ADDED.
			return SuppliedFiles.Where(p => p.LastStatus == BackupFileStatus.ADDED);
		}

		//
		// Summary:
		// Return all files from `SuppliedFiles` which are marked as `MODIFIED`;
		// NOTE: This function has no side effects.
		//
		private IEnumerable<BackupPlanFile> GetModifiedFiles()
		{
			Assert.IsNotNull(SuppliedFiles);

			// Find all `BackupPlanFile`s from this `BackupPlan` that are marked as MODIFIED.
			return SuppliedFiles.Where(p => p.LastStatus == BackupFileStatus.MODIFIED);
		}

		//
		// Summary:
		// Return all files from `AllFilesFromPlan` which are marked as `REMOVED`;
		// NOTE: This function has no side effects.
		//
		private IEnumerable<BackupPlanFile> GetRemovedFiles()
		{
			Assert.IsNotNull(AllFilesFromPlan);

			// Find all `BackupPlanFile`s from this `BackupPlan` that are marked as REMOVED.
			return AllFilesFromPlan.Values.Where(p => p.LastStatus == BackupFileStatus.REMOVED);
		}

		//
		// Summary:
		// 1. Check which files from `files` are not marked as DELETED;
		// 2. Check which files from `files` are not marked as REMOVED;
		// 3. Check which files from `AllFilesFromPlan` are not in the results of 1 AND 2, meaning
		//    they have been deleted from the filesystem.
		// 4. Mark them as DELETED;
		// 5. Return the union of all files from 1 and 3;
		// NOTE: This function has a side effect - It updates properties of items from `files`.
		//
		private IEnumerable<BackupPlanFile> GetDeletedFilesAndUpdateTheirStatus(LinkedList<BackupPlanFile> files)
		{
			Assert.IsNotNull(files);
			Assert.IsNotNull(AllFilesFromPlan);

			// 1. Find all files from `files` that were previously marked as DELETED.
			IEnumerable<BackupPlanFile> deletedFiles = files.Where(p => p.LastStatus == BackupFileStatus.DELETED);
			foreach (var item in deletedFiles)
				Console.WriteLine("GetDeletedFilesAndUpdateTheirStatus: deletedFiles: {0}", item.Path);

			// 2. Find all files from `files` that were previously marked as REMOVED.
			IEnumerable<BackupPlanFile> nonRemovedFiles = files.Where(p => p.LastStatus != BackupFileStatus.REMOVED);
			foreach (var item in nonRemovedFiles)
				Console.WriteLine("GetDeletedFilesAndUpdateTheirStatus: nonRemovedFiles: {0}", item.Path);

			// 3. Check which files from `AllFilesFromPlan` are not in the results of 1 AND 2, meaning
			//    they have been deleted from the filesystem.
			IEnumerable<BackupPlanFile> deletedFilesToBeUpdated = AllFilesFromPlan.Values.Except(nonRemovedFiles).Except(deletedFiles);
			foreach (var item in deletedFilesToBeUpdated)
				Console.WriteLine("GetDeletedFilesAndUpdateTheirStatus: deletedFilesToBeUpdated: {0}", item.Path);

			// 4. Mark them as DELETED;
			foreach (BackupPlanFile entry in deletedFilesToBeUpdated)
			{
				// Throw if the operation was canceled.
				CancellationTokenSource.Token.ThrowIfCancellationRequested();

				entry.LastStatus = BackupFileStatus.DELETED;
				entry.UpdatedAt = DateTime.UtcNow;
			}

			// 5. Return all files from 1 and 3;
			List<BackupPlanFile> result = new List<BackupPlanFile>(deletedFiles.Count() + deletedFilesToBeUpdated.Count());
			result.AddRange(deletedFiles);
			result.AddRange(deletedFilesToBeUpdated);
			foreach (var item in result)
				Console.WriteLine("GetDeletedFilesAndUpdateTheirStatus: result: {0}", item.Path);

			return result;
		}

		//
		// Summary:
		// Update all files' properties like size, last written date, etc, skipping files
		// marked as REMOVED, DELETED or UNCHANGED.
		// NOTE: This function has a side effect - It updates properties of items from `files`.
		//
		private void DoUpdateFilesProperties(LinkedList<BackupPlanFile> files)
		{
			foreach (BackupPlanFile entry in files)
			{
				// Throw if the operation was canceled.
				CancellationTokenSource.Token.ThrowIfCancellationRequested();

				switch (entry.LastStatus)
				{
					// Skip REMOVED, DELETED, and UNCHANGED files.
					default:
						break;
					case BackupFileStatus.ADDED:
					case BackupFileStatus.MODIFIED:
						// Update file related properties
						string path = entry.Path;
						entry.LastSize = FileManager.GetFileSize(path).Value;
						entry.LastWrittenAt = FileManager.GetLastWriteTimeUtc(path).Value;

						if (entry.Id.HasValue)
							entry.UpdatedAt = DateTime.UtcNow;
						break;
				}
			}
		}

		//
		// Summary:
		// ...
		//
		private IEnumerable<CustomVersionedFile> GetFilesToTransfer(Models.Backup backup, LinkedList<BackupPlanFile> files)
		{
			IFileVersion version = new FileVersion { Version = backup.Id.Value.ToString() };

			// Update files version.
			foreach (BackupPlanFile entry in files)
			{
				// Throw if the operation was canceled.
				CancellationTokenSource.Token.ThrowIfCancellationRequested();

				switch (entry.LastStatus)
				{
					// Skip REMOVED, DELETED, and UNCHANGED files.
					default:
						break;
					case BackupFileStatus.ADDED:
					case BackupFileStatus.MODIFIED:
						yield return new CustomVersionedFile
						{
							Path = entry.Path,
							Size = entry.LastSize,
							Checksum = null,
							Version = version,
							LastWriteTimeUtc = entry.LastWrittenAt,
						};
						break; // YES, it's required!
				}
			}
		}

		public void Undo()
		{
			Assert.IsFalse(IsSaved);
			BackupRepository daoBackup = new BackupRepository();
			daoBackup.Refresh(Backup);
		}

		//
		// Summary:
		// 1. Insert or update all `BackupPlanFile`s from the backup plan associated with this backup operation.
		// 2. Create `BackupedFile`s as necessary and add them to the `Backup`.
		// 3. Insert/Update `Backup` and its `BackupedFile`s into the database.
		// 4. Create versioned files and remove files that won't belong to this backup.
		//
		public void Save()
		{
			Assert.IsFalse(IsSaved);
			BackupRepository daoBackup = new BackupRepository();
			BackupPlanRepository daoBackupPlan = new BackupPlanRepository();
			BackupPlanFileRepository daoBackupPlanFile = new BackupPlanFileRepository();
			BackupedFileRepository daoBackupedFile = new BackupedFileRepository();

			// 2. Create `BackupedFile`s as necessary and add them to the `Backup`.
			var FilesToTrack = SuppliedFiles.Union(ChangeSet.DeletedFiles);
			foreach (BackupPlanFile entry in FilesToTrack)
			{
				TransferStatus? changeTransferStatusTo = null;
				switch (entry.LastStatus)
				{
					// A file that has been marked as REMOVED or DELETED has 2 possibilities here:
					// 1. This is the 1st backup after the removal/deletion, thus we need to insert a `BackupedFile` as REMOVED/DELETED.
					// 2. This is not the 1st backup after the removal/deletion, thus we should have already signaled it.
					//    No need to insert a `BackupedFile` as REMOVED/DELETED again.
					case BackupFileStatus.REMOVED:
					case BackupFileStatus.DELETED:
						{
							// Retrieve the actual `BackupPlanFile` from the database using a stateless session to check whether its status differ.
							BackupPlanFile currentBackupPlanFileFromDB = daoBackupPlanFile.GetStateless(entry.Id);
							// TODO - nao rolar fazer esse lance pq dei UPDATE no passo 1 .. logo acima. FAIL!
							bool statusIsTheSameFromDB = entry.LastStatus == currentBackupPlanFileFromDB.LastStatus;
							if (statusIsTheSameFromDB)
								continue; // No need to insert a `BackupedFile` as REMOVED/DELETED again.
							changeTransferStatusTo = TransferStatus.COMPLETED;
							break; // Will add or update a `BackupedFile`.
						}

					case BackupFileStatus.ADDED:
					case BackupFileStatus.MODIFIED:
						break; // Will add or update a `BackupedFile`.

					default: // Skip all UNCHANGED files.
						continue;
				}

				BackupedFile backupedFile = daoBackupedFile.GetByBackupAndPath(Backup, entry.Path);
				if (backupedFile == null) // If we're resuming, this should already exist.
				{
					// Create `BackupedFile`.
					backupedFile = new BackupedFile(Backup, entry);
				}
				backupedFile.FileStatus = entry.LastStatus;
				backupedFile.TransferStatus = changeTransferStatusTo ?? default(TransferStatus);
				backupedFile.UpdatedAt = DateTime.UtcNow;
				Backup.Files.Add(backupedFile);
				// IMPORTANT: It's important that we guarantee the referenced `BackupPlanFile` has a valid `Id`,
				// otherwise NHibernate won't have a valid value to put on the `backup_plan_file_id` column.
				daoBackupPlanFile.InsertOrUpdate(entry);
				daoBackupedFile.InsertOrUpdate(backupedFile);
			}

			// 1. Insert or update all `BackupPlanFile`s that already exist for the backup plan associated with this backup operation.
			foreach (BackupPlanFile file in AllFilesFromPlan.Values)
				daoBackupPlanFile.Update(file);
			//// This we'll iterate once again over files that were RE-ADDED after being REMOVED or DELETED,
			//// because they are also in `AllFilesFromPlan`. Sorry! We could use `Union()` + `Except()`, but
			//// I believe the performance for this would be horrible.
			//foreach (BackupPlanFile file in BackupPlanAddedFiles)
			//	daoBackupPlanFile.InsertOrUpdate(file);

			// 3. Insert/Update `Backup` and its `BackupedFile`s into the database.
			daoBackup.Update(Backup);
			IsSaved = true;

			// 4. Create versioned files and remove files that won't belong to this backup.
			TransferSet.Files = GetFilesToTransfer(Backup, SuppliedFiles);

			// Test to see if things are okay!
			{
				var transferCount = TransferSet.Files.Count();
				var filesCount = ChangeSet.AddedFiles.Count() + ChangeSet.ModifiedFiles.Count();

				Assert.IsTrue(transferCount == filesCount, "FilesToTransfer must be euqla (BackupPlanAddedFiles + BackupPlanModifiedFiles)");
			}
		}

		#region Dispose Pattern Implementation

		bool _shouldDispose = true;
		bool _isDisposed;

		/// <summary>
		/// Implements the Dispose pattern
		/// </summary>
		/// <param name="disposing">Whether this object is being disposed via a call to Dispose
		/// or garbage collected.</param>
		private void Dispose(bool disposing)
		{
			if (!this._isDisposed)
			{
				if (disposing && _shouldDispose)
				{
					if (CancellationTokenSource != null)
					{
						CancellationTokenSource.Dispose();
						CancellationTokenSource = null;
					}
				}
				this._isDisposed = true;
			}
		}

		/// <summary>
		/// Disposes of all managed and unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion
	}

	public sealed class ChangeSet<T>
	{
		// Contains ALL `BackupPlanFile`s that were marked as ADDED to the plan associated to this backup.
		// Fact 1: SOME of its items may also be contained in `AllFilesFromPlan`.
		// Fact 2: ALL of its items are also contained in `SuppliedFiles`.
		internal IEnumerable<T> AddedFiles;

		// Contains ALL `BackupPlanFile`s that were marked as MODIFIED from the plan associated to this backup.
		// Fact 1: ALL of its items are also contained in `AllFilesFromPlan`.
		// Fact 2: ALL of its items are also contained in `SuppliedFiles`.
		internal IEnumerable<T> ModifiedFiles;

		// Contains ALL `BackupPlanFile`s that were marked as DELETED from the plan associated to this backup.
		// Fact 1: ALL of its items are also contained in `AllFilesFromPlan`.
		// Fact 2: SOME of its items may also be contained in `SuppliedFiles`.
		internal IEnumerable<T> DeletedFiles;

		// Contains ALL `BackupPlanFile`s that were marked as REMOVED from the plan associated to this backup.
		// Fact 1: ALL of its items are also contained in `AllFilesFromPlan`.
		internal IEnumerable<T> RemovedFiles;
	}

	public sealed class TransferSet<T>
	{
		internal IEnumerable<T> Files;
	}
}