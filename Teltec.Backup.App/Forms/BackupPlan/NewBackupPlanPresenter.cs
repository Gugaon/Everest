﻿using NLog;
using System;
using Teltec.Backup.App.DAO;
using Teltec.Backup.App.Forms.Schedule;
using Teltec.Backup.App.Models;
using Teltec.Common.Extensions;
using Teltec.Forms.Wizard;

namespace Teltec.Backup.App.Forms.BackupPlan
{
	sealed class NewBackupPlanPresenter : WizardPresenter
	{
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();
		private readonly BackupPlanRepository _dao = new BackupPlanRepository();

		public NewBackupPlanPresenter()
			: this(new Models.BackupPlan())
		{
			IsEditingModel = false;
			
			//Models.BackupPlan plan = Model as Models.BackupPlan;
			//plan.Name = "Testing name";
			//plan.ScheduleType = Models.BackupPlan.ScheduleTypeE.RunManually;
		}

		public NewBackupPlanPresenter(Models.BackupPlan plan)
		{
			IsEditingModel = true;
			Model = plan;

			WizardFormOptions options = new WizardFormOptions { DoValidate = true };
			RegisterFormClass(typeof(BackupPlanSelectAccountForm), options);
			RegisterFormClass(typeof(BackupPlanGiveNameForm), options);
			RegisterFormClass(typeof(BackupPlanSelectSourceForm), options);
			RegisterFormClass(typeof(SchedulablePlanForm<Models.BackupPlan>), options);
		}

		public override void OnCancel()
		{
			base.OnCancel();

			Models.BackupPlan plan = Model as Models.BackupPlan;
			_dao.Refresh(plan);
		}

		public override void OnFinish()
		{
			base.OnFinish();

			Models.BackupPlan plan = Model as Models.BackupPlan;

			Console.WriteLine("Name = {0}", plan.Name);
			Console.WriteLine("StorageAccount = {0}", plan.StorageAccount.DisplayName);
			Console.WriteLine("StorageAccountType = {0}", plan.StorageAccountType.ToString());
			foreach (BackupPlanSourceEntry entry in plan.SelectedSources)
				Console.WriteLine("SelectedSource => #{0}, {1}, {2}", entry.Id, entry.Type.ToString(), entry.Path);
			Console.WriteLine("ScheduleType = {0}", plan.ScheduleType.ToString());
			Console.WriteLine("Schedule.ScheduleType = {0}", plan.Schedule.ScheduleType.ToString());
			
			PlanSchedule schedule = plan.Schedule;
			switch (plan.ScheduleType)
			{
				case Models.ScheduleTypeEnum.RUN_MANUALLY:
					break;
				case Models.ScheduleTypeEnum.SPECIFIC:
					Console.WriteLine("OccursSpecificallyAt = {0}", schedule.OccursSpecificallyAt.HasValue ? schedule.OccursSpecificallyAt.Value.ToString() : "null");
					break;
				case Models.ScheduleTypeEnum.RECURRING:
					Console.WriteLine("RecurrencyFrequencyType      = {0}",
						schedule.RecurrencyFrequencyType.HasValue ? schedule.RecurrencyFrequencyType.Value.ToString() : "null");
					Console.WriteLine("RecurrencyDailyFrequencyType = {0}",
						schedule.RecurrencyDailyFrequencyType.HasValue ? schedule.RecurrencyDailyFrequencyType.Value.ToString() : "null");

					if (schedule.RecurrencyFrequencyType.HasValue)
					{
						switch (schedule.RecurrencyFrequencyType.Value)
						{
							case FrequencyTypeEnum.DAILY:
								break;
							case FrequencyTypeEnum.WEEKLY:
								Console.WriteLine("OccursAtDaysOfWeek           = {0}",
									schedule.OccursAtDaysOfWeek != null ? schedule.OccursAtDaysOfWeek.ToReadableString() : "null");
								break;
							case FrequencyTypeEnum.MONTHLY:
								Console.WriteLine("MonthlyOccurrenceType        = {0}",
									schedule.MonthlyOccurrenceType.HasValue ? schedule.MonthlyOccurrenceType.Value.ToString() : "null");
								Console.WriteLine("OccursMonthlyAtDayOfWeek     = {0}",
									schedule.OccursMonthlyAtDayOfWeek.HasValue ? schedule.OccursMonthlyAtDayOfWeek.Value.ToString() : "null");
								break;
							case FrequencyTypeEnum.DAY_OF_MONTH:
								Console.WriteLine("OccursAtDayOfMonth           = {0}",
									schedule.OccursAtDayOfMonth.HasValue ? schedule.OccursAtDayOfMonth.Value.ToString() : "null");
								break;
						}
					}
					Console.WriteLine("RecurrencySpecificallyAtTime = {0}",
						schedule.RecurrencySpecificallyAtTime.HasValue ? schedule.RecurrencySpecificallyAtTime.Value.ToString() : "null");
					Console.WriteLine("RecurrencyTimeInterval       = {0}",
						schedule.RecurrencyTimeInterval.HasValue ? schedule.RecurrencyTimeInterval.Value.ToString() : "null");
					Console.WriteLine("RecurrencyTimeUnit           = {0}",
						schedule.RecurrencyTimeUnit.HasValue ? schedule.RecurrencyTimeUnit.Value.ToString() : "null");
					Console.WriteLine("RecurrencyWindowStartsAtTime = {0}",
						schedule.RecurrencyWindowStartsAtTime.HasValue ? schedule.RecurrencyWindowStartsAtTime.Value.ToString() : "null");
					Console.WriteLine("RecurrencyWindowEndsAtTime   = {0}",
						schedule.RecurrencyWindowEndsAtTime.HasValue ? schedule.RecurrencyWindowEndsAtTime.Value.ToString() : "null");
					break;
			}

			//try
			//{
				if (IsEditingModel)
				{
					_dao.Update(plan);
				}
				else
				{
					_dao.Insert(plan);
				}
			//}
			//catch (Exception ex)
			//{
			//	MessageBox.Show(ex.Message, "Error");
			//} 
		}
	}
}
