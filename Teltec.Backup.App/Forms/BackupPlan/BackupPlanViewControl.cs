﻿using NUnit.Framework;
using System;
using System.ComponentModel;
using System.Windows.Forms;
using Teltec.Backup.App.DAO;
using Teltec.Backup.App.Models;
using Teltec.Common;
using Teltec.Common.Extensions;
using Teltec.Storage;
using Teltec.Storage.Utils;

namespace Teltec.Backup.App.Forms.BackupPlan
{
	public partial class BackupPlanViewControl : ObservableUserControl, IDisposable
	{
		private readonly BackupPlanRepository _dao = new BackupPlanRepository();

		BackupOperation RunningBackup = null;
		BackupResults BackupResults = null;

		public BackupPlanViewControl()
		{
			InitializeComponent();

			this.ModelChangedEvent += (sender, args) =>
			{
				lblTitle.DataBindings.Clear();
				lblSchedule.DataBindings.Clear();
				lblLastRun.DataBindings.Clear();
				lblLastSuccessfulRun.DataBindings.Clear();

				if (Model == null)
					return;

				Binding lblTitleTextBinding = new Binding("Text", Model,
					this.GetPropertyName((Models.BackupPlan x) => x.Name));
				lblTitleTextBinding.Format += TitleFormatter;

				Binding lblScheduleTextBinding = new Binding("Text", Model,
					this.GetPropertyName((Models.BackupPlan x) => x.ScheduleType));

				Binding lblLastRunTextBinding = new Binding("Text", Model,
					this.GetPropertyName((Models.BackupPlan x) => x.LastRunAt));
				lblLastRunTextBinding.Format += DateTimeOptionalFormatter;

				Binding lblLastSuccessfulRunTextBinding = new Binding("Text", Model,
					this.GetPropertyName((Models.BackupPlan x) => x.LastSuccessfulRunAt));
				lblLastSuccessfulRunTextBinding.Format += DateTimeOptionalFormatter;

				lblTitle.DataBindings.Add(lblTitleTextBinding);
				lblSchedule.DataBindings.Add(lblScheduleTextBinding);
				lblLastRun.DataBindings.Add(lblLastRunTextBinding);
				lblLastSuccessfulRun.DataBindings.Add(lblLastSuccessfulRunTextBinding);

				NewBackupOperation(this.Model as Models.BackupPlan);
			};
		}

		private void NewBackupOperation(Models.BackupPlan plan)
		{
			// IMPORTANT: Dispose before assigning.
			if (RunningBackup != null)
				RunningBackup.Dispose();
			
			// Create new backup.
			BackupOperation obj = new BackupOperation(plan);
			obj.Updated += (sender2, e2) => UpdateStatsInfo(e2.Status);
			//obj.EventLog = ...
			//obj.TransferListControl = ...

			RunningBackup = obj;
			UpdateStatsInfo(BackupOperationStatus.Unknown);
		}

		#region Binding formatters

		void TitleFormatter(object sender, ConvertEventArgs e)
		{
			if (e.DesiredType != typeof(string))
				return;

			string value = e.Value as string;

			e.Value = string.IsNullOrEmpty(value)
				? "(UNNAMED)"
				: e.Value = value.ToUpper();
		}

		void DateTimeOptionalFormatter(object sender, ConvertEventArgs e)
		{
			if (e.DesiredType != typeof(string))
				return;

			DateTime? dt = e.Value as DateTime?;

			e.Value = dt.HasValue
				? string.Format("{0:yyyy-MM-dd HH:mm:ss zzzz}", dt.Value.ToLocalTime())
				: "Never";
		}

		#endregion

		private void llblEditPlan_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			using (var presenter = new NewBackupPlanPresenter(this.Model as Models.BackupPlan))
			{
				presenter.ShowDialog(this.ParentForm);
			}
		}

		private void llblDeletePlan_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			Models.BackupPlan plan = Model as Models.BackupPlan;
			_dao.Delete(plan);
			Model = null;
			OnDelete(this, e);
		}

		private void llblRunNow_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			this.llblRunNow.Enabled = false;

			if (RunningBackup.IsRunning)
			{
				RunningBackup.Cancel();
			}
			else
			{
				// Create new backup operation for every 'Run' click.
				NewBackupOperation(this.Model as Models.BackupPlan);
				RunningBackup.Start(out BackupResults);
			}

			this.llblRunNow.Enabled = true;
		}

		public delegate void DeleteEventHandler(object sender, EventArgs e);
		public event DeleteEventHandler Deleted;

		protected virtual void OnDelete(object sender, EventArgs e)
		{
			if (Deleted != null)
				Deleted(this, e);
		}

		private void timer1_Tick(object sender, EventArgs e)
		{
			UpdateDuration(RunningBackup.IsRunning ? BackupOperationStatus.Updated : BackupOperationStatus.Finished);
		}

		static string LBL_RUNNOW_RUNNING = "Cancel";
		static string LBL_RUNNOW_STOPPED = "Run now";
		static string LBL_STATUS_STARTED = "Running";
		static string LBL_STATUS_STOPPED = "Stopped";
		static string LBL_STATUS_CANCELED = "Canceled";
		static string LBL_STATUS_FAILED = "Failed";
		static string LBL_STATUS_COMPLETED = "Completed";
		static string LBL_DURATION_STARTED = "Starting...";
		static string LBL_DURATION_INITIAL = "Not started";
		static string LBL_FILES_UPLOADED_STOPPED = "Not started"; 

		private void UpdateStatsInfo(BackupOperationStatus status)
		{
			Assert.IsNotNull(RunningBackup);

			switch (status)
			{
				default: throw new ArgumentException("Unhandled status", "status");
				case BackupOperationStatus.Unknown:
					{
						Models.BackupPlan plan = Model as Models.BackupPlan;
						this.lblSources.Text = RunningBackup.Sources;
						this.lblStatus.Text = LBL_STATUS_STOPPED;
						this.llblRunNow.Text = LBL_RUNNOW_STOPPED;
						this.lblFilesUploaded.Text = LBL_FILES_UPLOADED_STOPPED;
						this.lblDuration.Text = LBL_DURATION_INITIAL;
						break;
					}
				case BackupOperationStatus.Started:
					{
						Assert.IsNotNull(BackupResults);
						Models.BackupPlan plan = Model as Models.BackupPlan;
						this.lblSources.Text = RunningBackup.Sources;
						this.llblRunNow.Text = LBL_RUNNOW_RUNNING;
						this.lblStatus.Text = LBL_STATUS_STARTED;
						this.lblDuration.Text = LBL_DURATION_STARTED;
						this.lblFilesUploaded.Text = string.Format("{0} of {1}",
							BackupResults.Stats.Completed, BackupResults.Stats.Total);

						this.llblEditPlan.Enabled = false;
						this.llblDeletePlan.Enabled = false;

						timer1.Enabled = true;
						timer1.Start();
						break;
					}
				case BackupOperationStatus.ProcessingFilesStarted:
					{
						Models.BackupPlan plan = Model as Models.BackupPlan;
						this.lblSources.Text = "Processing files...";
						break;
					}
					case BackupOperationStatus.ProcessingFilesFinished:
					{
						Models.BackupPlan plan = Model as Models.BackupPlan;
						this.lblSources.Text = RunningBackup.Sources;
						break;
					}
				case BackupOperationStatus.Finished:
					{
						UpdateDuration(status);
						this.llblRunNow.Text = LBL_RUNNOW_STOPPED;
						this.lblStatus.Text = LBL_STATUS_COMPLETED;

						this.llblEditPlan.Enabled = true;
						this.llblDeletePlan.Enabled = true;

						timer1.Stop();
						timer1.Enabled = false;

						// Update timestamps.
						Models.BackupPlan plan = Model as Models.BackupPlan;
						plan.LastRunAt = plan.LastSuccessfulRunAt = DateTime.UtcNow;
						_dao.Update(plan);
						break;
					}
				case BackupOperationStatus.Updated:
					{
						this.lblFilesUploaded.Text = string.Format("{0} of {1}",
							BackupResults.Stats.Completed, BackupResults.Stats.Total);
						break;
					}
				case BackupOperationStatus.Failed:
				case BackupOperationStatus.Canceled:
					{
						UpdateDuration(status);

						this.llblRunNow.Text = LBL_RUNNOW_STOPPED;
						this.lblStatus.Text = status == BackupOperationStatus.Canceled ? LBL_STATUS_CANCELED : LBL_STATUS_FAILED;

						this.llblEditPlan.Enabled = true;
						this.llblDeletePlan.Enabled = true;

						timer1.Stop();
						timer1.Enabled = false;

						// Update timestamps.
						Models.BackupPlan plan = Model as Models.BackupPlan;
						plan.LastRunAt = DateTime.UtcNow;
						_dao.Update(plan);
						break;
					}
			}
		}

		private void UpdateDuration(BackupOperationStatus status)
		{
			Assert.IsNotNull(RunningBackup);
			var duration = !status.IsEnded()
				? DateTime.UtcNow - RunningBackup.StartedAt.Value
				: RunningBackup.FinishedAt.Value - RunningBackup.StartedAt.Value;
			lblDuration.Text = TimeSpanUtils.GetReadableTimespan(duration);
		}

		#region Model

		[
		Bindable(true),
		System.ComponentModel.Category("Data"),
		DefaultValue(null),
		DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)
		]
		protected object _Model;
		public virtual object Model
		{
			get { return _Model; }
			set
			{
				SetField(ref _Model, value);
				OnModelChanged(this, new ModelChangedEventArgs(_Model));
			}
		}

		public class ModelChangedEventArgs : EventArgs
		{
			private object _model;
			public object Model
			{
				get { return _model; }
			}

			public ModelChangedEventArgs(object model)
			{
				_model = model;
			}
		}

		public delegate void ModelChangedEventHandler(object sender, ModelChangedEventArgs e);

		public event ModelChangedEventHandler ModelChangedEvent;

		protected virtual void OnModelChanged(object sender, ModelChangedEventArgs e)
		{
			if (ModelChangedEvent != null)
				ModelChangedEvent(this, e);
		}

		#endregion

		#region Panel collapsing

		private bool Collapsed = false;

		private void panelTitle_MouseClick(object sender, MouseEventArgs e)
		{
			Collapsed = !Collapsed;
			if (Collapsed)
			{
				this.Controls.Remove(this.panelContents);
				this.Size = new System.Drawing.Size(this.Size.Width, this.Size.Height - this.panelContents.Size.Height);
			}
			else
			{
				this.Controls.Add(this.panelContents);
				this.Size = new System.Drawing.Size(this.Size.Width, this.Size.Height + this.panelContents.Size.Height);
			}
		}

		#endregion

		//private void eventLog1_EntryWritten(object sender, System.Diagnostics.EntryWrittenEventArgs e)
		//{
		//	string message = string.Format("{0:o} {1} {2}",
		//		e.Entry.TimeWritten, e.Entry.EntryType.ToString().ToUpper(),
		//		e.Entry.Message);
		//	listBox1.Items.Add(message);
		//	// Auto-scroll
		//	listBox1.TopIndex = listBox1.Items.Count - 1;
		//}

		#region Dispose Pattern Implementation

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
				if (RunningBackup != null)
				{
					RunningBackup.Dispose();
					RunningBackup = null;
				}
			}
			base.Dispose(disposing);
		}

		#endregion
	}
}