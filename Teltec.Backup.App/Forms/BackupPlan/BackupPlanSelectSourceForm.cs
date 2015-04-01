﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Teltec.Backup.App.Controls;
using Teltec.Backup.App.DAO;
using Teltec.Backup.App.Models;
using Teltec.Common.Extensions;

namespace Teltec.Backup.App.Forms.BackupPlan
{
	public partial class BackupPlanSelectSourceForm : Teltec.Forms.Wizard.WizardForm
	{
		private readonly BackupPlanSourceEntryRepository _dao = new BackupPlanSourceEntryRepository();

		public BackupPlanSelectSourceForm()
		{
			InitializeComponent();
			loadingPanel.Dock = DockStyle.Fill;
			tvFiles.FileSystemFetchStarted += (object sender, EventArgs e) =>
			{
				//loadingPanel.Visible = true;
			};
			tvFiles.FileSystemFetchEnded += (object sender, EventArgs e) =>
			{
				//loadingPanel.Visible = false;
			};

			this.ModelChangedEvent += (Teltec.Forms.Wizard.WizardForm sender, Teltec.Forms.Wizard.WizardForm.ModelChangedEventArgs e) =>
			{
				Models.BackupPlan plan = e.Model as Models.BackupPlan;
				// Lazily select nodes that match entries from `plan.SelectedSources`.
				tvFiles.CheckedDataSource = BackupPlanSelectedSourcesToCheckedDataSource(plan);
			};
		}

		private FileSystemTreeNode.TypeEnum ToTypeEnum(EntryType obj)
		{
			switch (obj)
			{
				default: throw new ArgumentException("Unhandled EntryType", "obj");
				case EntryType.DRIVE:
					return FileSystemTreeNode.TypeEnum.DRIVE;
				case EntryType.FOLDER:
					return FileSystemTreeNode.TypeEnum.FOLDER;
				case EntryType.FILE:
					return FileSystemTreeNode.TypeEnum.FILE;
			}
		}

		private Dictionary<string, FileSystemTreeNodeData> BackupPlanSelectedSourcesToCheckedDataSource(Models.BackupPlan plan)
		{
			return plan.SelectedSources.ToDictionary(
				e => e.Path,
				e => new FileSystemTreeNodeData
				{
					Id = e.Id,
					Type = ToTypeEnum(e.Type),
					Path = e.Path,
					State = Teltec.Common.Controls.CheckState.Checked
				}
			);
		}

		protected override bool IsValid()
		{
			Models.BackupPlan plan = Model as Models.BackupPlan;
			bool didSelectSource = plan.SelectedSources != null && plan.SelectedSources.Count > 0;
			return didSelectSource;
		}

		protected override void OnBeforeNextOrFinish(object sender, CancelEventArgs e)
		{
			Models.BackupPlan plan = Model as Models.BackupPlan;
			
			ICollection<BackupPlanSourceEntry> entries = tvFiles.GetCheckedTagData().ToBackupPlanSourceEntry(plan, _dao);
			plan.SelectedSources.Clear();
			plan.SelectedSources.AddRange(entries);

			if (DoValidate && !IsValid())
			{
				e.Cancel = true;
				this.ShowErrorMessage("Please, select a source.");
			}
			base.OnBeforeNextOrFinish(sender, e);
		}
	}
}
