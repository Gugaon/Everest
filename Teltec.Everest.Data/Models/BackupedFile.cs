/* 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using Teltec.Storage;

namespace Teltec.Everest.Data.Models
{
	public class BackupedFile : BaseEntity<Int64?>
	{
		public BackupedFile()
		{
		}

		public BackupedFile(Backup backup, BackupPlanFile file)
			: this()
		{
			_Backup = backup;
			if (_Backup != null)
			{
				if (_Backup.BackupPlan != null && _Backup.BackupPlan.StorageAccount != null)
				{
					_StorageAccountType = _Backup.BackupPlan.StorageAccountType;
					_StorageAccount = _Backup.BackupPlan.StorageAccount;
				}
			}
			_File = file;
		}

		public BackupedFile(Backup backup, BackupPlanFile file, Synchronization sync)
			: this()
		{
			_Backup = backup;
			_File = file;
			_Synchronization = sync;
		}

		private Int64? _Id;
		public virtual Int64? Id
		{
			get { return _Id; }
			set { SetField(ref _Id, value); }
		}

		private Backup _Backup;
		public virtual Backup Backup
		{
			get { return _Backup; }
			protected set
			{
				_Backup = value;
				if (_Backup != null)
				{
					if (_Backup.BackupPlan != null && _Backup.BackupPlan.StorageAccount != null)
					{
						StorageAccountType = _Backup.BackupPlan.StorageAccountType;
						StorageAccount = _Backup.BackupPlan.StorageAccount;
					}
				}
			}
		}

		#region Account

		private EStorageAccountType _StorageAccountType;
		public virtual EStorageAccountType StorageAccountType
		{
			get { return _StorageAccountType; }
			set { SetField(ref _StorageAccountType, value); }
		}

		//private int _StorageAccountId;
		//public virtual int StorageAccountId
		//{
		//	get { return _StorageAccountId; }
		//	set { SetField(ref _StorageAccountId, value); }
		//}

		//public static ICloudStorageAccount GetStorageAccount(BackupPlan plan, ICloudStorageAccount dao)
		//{
		//	switch (plan.StorageAccountType)
		//	{
		//		default:
		//			throw new ArgumentException("Unhandled StorageAccountType", "plan");
		//		case EStorageAccountType.AmazonS3:
		//			return dao.Get(plan.StorageAccountId);
		//	}
		//}

		private StorageAccount _StorageAccount;
		public virtual StorageAccount StorageAccount
		{
			get { return _StorageAccount; }
			set { SetField(ref _StorageAccount, value); }
		}

		#endregion

		private Synchronization _Synchronization;
		public virtual Synchronization Synchronization
		{
			get { return _Synchronization; }
			protected set { _Synchronization = value; }
		}

		private BackupPlanFile _File;
		public virtual BackupPlanFile File
		{
			get { return _File; }
			protected set { _File = value; }
		}

		private long _FileSize;
		public virtual long FileSize
		{
			get { return _FileSize; }
			set { SetField(ref _FileSize, value); }
		}

		private BackupFileStatus _FileStatus;
		public virtual BackupFileStatus FileStatus
		{
			get { return _FileStatus; }
			set { SetField(ref _FileStatus, value); }
		}

		private DateTime _FileLastWrittenAt; // Last date the file was modified.
		public virtual DateTime FileLastWrittenAt
		{
			get { return _FileLastWrittenAt; }
			set { SetField(ref _FileLastWrittenAt, value); }
		}

		private byte[] _FileLastChecksum; // SHA-1
		public virtual byte[] FileLastChecksum
		{
			get { return _FileLastChecksum; }
			set { SetField(ref _FileLastChecksum, value); }
		}

		private TransferStatus _TransferStatus;
		public virtual TransferStatus TransferStatus
		{
			get { return _TransferStatus; }
			set { _TransferStatus = value; }
		}

		private DateTime _UpdatedAt;
		public virtual DateTime UpdatedAt
		{
			get { return _UpdatedAt; }
			set { _UpdatedAt = value; }
		}

		#region Read-only properties

		public static readonly string VersionFormat = "yyyyMMddHHmmss";

		public virtual string Version
		{
			get { return FileLastWrittenAt.ToString(VersionFormat); }
		}

		public virtual string VersionName
		{
			get { return FileLastWrittenAt.ToString("yyyy/MM/dd - HH:mm:ss"); }
		}

		#endregion
	}
}
