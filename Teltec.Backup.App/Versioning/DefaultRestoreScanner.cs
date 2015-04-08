﻿using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Teltec.Backup.App.Controls;
using Teltec.Backup.App.DAO;
using Teltec.Backup.App.Models;
using Teltec.FileSystem;
using Teltec.Storage;
using Teltec.Storage.Versioning;

namespace Teltec.Backup.App.Versioning
{
	public sealed class DefaultRestoreScanner : PathScanner<CustomVersionedFile>
	{
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		CancellationToken CancellationToken;
		RestorePlan Plan;
		LinkedList<CustomVersionedFile> Result;

		public DefaultRestoreScanner(RestorePlan plan, CancellationToken cancellationToken)
		{
			CancellationToken = cancellationToken;
			Plan = plan;
		}

		#region PathScanner

		// TODO(jweyrich): Should return a HashSet/ISet instead?
		public override LinkedList<CustomVersionedFile> Scan()
		{
			Result = new LinkedList<CustomVersionedFile>();

			//
			// Add sources.
			//
			foreach (var entry in Plan.SelectedSources)
			{
				try
				{
					switch (entry.Type)
					{
						default:
							throw new InvalidOperationException("Unhandled EntryType");
						case EntryType.DRIVE:
							{
								AddDirectory(entry);
								break;
							}
						case EntryType.FOLDER:
							{
								AddDirectory(entry);
								break;
							}
						case EntryType.FILE:
							{
								AddFile(entry);
								break;
							}
						case EntryType.FILE_VERSION:
							{
								AddFileVersion(entry);
								break;
							}
					}
				}
				catch (OperationCanceledException ex)
				{
					throw ex; // Rethrow!
				}
				catch (Exception ex)
				{
					string message = string.Format("Failed to scan entry {0}", entry.Path);
					logger.Error(message, ex);
				}
			}

			return Result;
		}

		#endregion

		private void AddDirectory(BackupPlanPathNode node, IFileVersion version)
		{
			CancellationToken.ThrowIfCancellationRequested();

			// Add all files from this directory.
			foreach (BackupPlanPathNode subNode in node.SubNodes)
			{
				if (subNode.Type != EntryType.FILE)
					continue;

				AddFile(subNode, version);
			}

			// Add all sub-directories recursively.
			foreach (BackupPlanPathNode subNode in node.SubNodes)
			{
				if (subNode.Type != EntryType.FOLDER)
					continue;

				AddDirectory(subNode, version);
			}
		}

		private void AddFileVersion(BackupPlanPathNode node, IFileVersion version)
		{
			CancellationToken.ThrowIfCancellationRequested();

			var item = new CustomVersionedFile { Path = node.Path, Version = version };

			Result.AddLast(item);

			if (FileAdded != null)
				FileAdded(this, item);
		}

		private void AddFile(BackupPlanPathNode node, IFileVersion version)
		{
			AddFileVersion(node, version);
		}

		private void AddFileVersion(RestorePlanSourceEntry entry)
		{
			IFileVersion version = entry.Version == null ? null : new FileVersion { Version = entry.Version };
			AddFile(entry.PathNode, version);
		}

		private void AddFile(RestorePlanSourceEntry entry)
		{
			IFileVersion version = entry.Version == null ? null : new FileVersion { Version = entry.Version };
			AddFile(entry.PathNode, version);
		}

		private void AddDirectory(RestorePlanSourceEntry entry)
		{
			IFileVersion version = entry.Version == null ? null : new FileVersion { Version = entry.Version };
			AddDirectory(entry.PathNode, version);
		}
	}
}
