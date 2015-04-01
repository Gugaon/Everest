﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Teltec.Backup.App.Controls;
using Teltec.Backup.App.DAO;
using Teltec.Backup.App.Models;
using Teltec.Common.Extensions;

namespace Teltec.Backup.App.Forms.RestorePlan
{
	public partial class RestorePlanSelectSourceForm : Teltec.Forms.Wizard.WizardForm
	{
		private readonly RestorePlanSourceEntryRepository _dao = new RestorePlanSourceEntryRepository();

		public RestorePlanSelectSourceForm()
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
				Models.RestorePlan plan = e.Model as Models.RestorePlan;
				// Lazily select nodes that match entries from `plan.SelectedSources`.
				tvFiles.CheckedDataSource = RestorePlanSelectedSourcesToCheckedDataSource(plan);
			};
		}

		private BackupPlanTreeNode.TypeEnum ToTypeEnum(EntryType obj)
		{
			switch (obj)
			{
				default: throw new ArgumentException("Unhandled EntryType", "obj");
				case EntryType.DRIVE:
					return BackupPlanTreeNode.TypeEnum.DRIVE;
				case EntryType.FOLDER:
					return BackupPlanTreeNode.TypeEnum.FOLDER;
				case EntryType.FILE:
					return BackupPlanTreeNode.TypeEnum.FILE;
				//case EntryType.FILE_VERSION:
				//	return BackupPlanTreeNode.TypeEnum.FILE_VERSION;
			}
		}

		private Dictionary<string, BackupPlanTreeNodeData> RestorePlanSelectedSourcesToCheckedDataSource(Models.RestorePlan plan)
		{
			return plan.SelectedSources.ToDictionary(
				e => e.Path,
				e => new BackupPlanTreeNodeData
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
			Models.RestorePlan plan = Model as Models.RestorePlan;
			bool didSelectSource = plan.SelectedSources != null && plan.SelectedSources.Count > 0;
			return didSelectSource;
		}

		protected override void OnBeforeNextOrFinish(object sender, CancelEventArgs e)
		{
			Models.RestorePlan plan = Model as Models.RestorePlan;
			
			ICollection<RestorePlanSourceEntry> entries = tvFiles.GetCheckedTagData().ToRestorePlanSourceEntry(plan, _dao);
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
