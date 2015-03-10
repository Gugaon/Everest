﻿using NLog;
using System.Collections.Generic;
using System.IO;
using Teltec.Backup.App.Models;
using Teltec.Storage;
using Teltec.Storage.Versioning;

namespace Teltec.Backup.App.Versioning
{
	public sealed class DefaultPathScanner : PathScanner<CustomVersionedFile>
	{
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		BackupPlan Plan;
		LinkedList<CustomVersionedFile> Result;

		public DefaultPathScanner(BackupPlan plan)
		{
			Plan = plan;
		}

		#region PathScanner

		public override LinkedList<CustomVersionedFile> Scan()
		{
			Result = new LinkedList<CustomVersionedFile>();

			//
			// Add sources.
			//
			foreach (var entry in Plan.SelectedSources)
			{
				switch (entry.Type)
				{
					case BackupPlanSourceEntry.EntryType.DRIVE:
						{
							DirectoryInfo dir = new DriveInfo(entry.Path).RootDirectory;
							AddDirectory(dir);
							break;
						}
					case BackupPlanSourceEntry.EntryType.FOLDER:
						{
							DirectoryInfo dir = new DirectoryInfo(entry.Path);
							AddDirectory(dir);
							break;
						}
					case BackupPlanSourceEntry.EntryType.FILE:
						{
							FileInfo file = new FileInfo(entry.Path);
							AddFile(file);
							break;
						}
				}
			}

			return Result;
		}

		#endregion

		private void AddFile(FileInfo file)
		{
			if (!file.Exists)
			{
				logger.Warn("File {0} does not exist", file.FullName);
				return;
			}

			CustomVersionedFile item = new CustomVersionedFile(file);
			Result.AddLast(item);
			logger.Debug("File added: {0}, {1} bytes", file.FullName, file.Length);

			if (FileAdded != null)
				FileAdded(this, item);
		}

		private void AddDirectory(string path)
		{
			AddDirectory(new DirectoryInfo(path));
		}

		private void AddDirectory(DirectoryInfo directory)
		{
			if (!directory.Exists)
			{
				logger.Warn("Directory {0} does not exist", directory.FullName);
				return;
			}

			// Add all files from this directory.
			foreach (FileInfo file in directory.GetFiles())
				AddFile(file);

			// Add all sub-directories recursively.
			foreach (DirectoryInfo subdir in directory.GetDirectories())
				AddDirectory(subdir);
		}
	}
}
