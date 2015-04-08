﻿using NLog;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using Teltec.Common;
using Teltec.Storage;
using Teltec.Storage.Agent;

namespace Teltec.Backup.App
{
	public abstract class BaseOperation : ObservableObject, IDisposable
	{
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		public Teltec.Storage.Monitor.TransferListControl TransferListControl; // IDisposable, but an external reference.
		protected IAsyncTransferAgent TransferAgent; // IDisposable

		#region Properties

		public bool IsRunning { get; protected set; }

		#endregion

		public abstract void Start(out TransferResults results);

		#region Task

		protected CancellationTokenSource CancellationTokenSource; // IDisposable

		protected Task<T> ExecuteOnBackround<T>(Func<T> action, CancellationToken token)
		{
			return Task.Factory.StartNew<T>(action, token);
		}

		public virtual void Cancel()
		{
			Assert.IsTrue(IsRunning);
		}

		#endregion

		protected BaseOperation()
		{
			CancellationTokenSource = new CancellationTokenSource();
		}

		#region Logging

		public System.Diagnostics.EventLog EventLog;

		protected void Log(System.Diagnostics.EventLogEntryType type, string format, params object[] args)
		{
			string message = string.Format(format, args);
			Console.WriteLine(message);
			if (EventLog != null)
				EventLog.WriteEntry(message, type);
		}

		protected void Warn(string format, params object[] args)
		{
			Log(System.Diagnostics.EventLogEntryType.Warning, format, args);
		}

		protected void Error(string format, params object[] args)
		{
			Log(System.Diagnostics.EventLogEntryType.Error, format, args);
		}

		protected void Info(string format, params object[] args)
		{
			Log(System.Diagnostics.EventLogEntryType.Information, format, args);
		}

		#endregion

		#region Dispose Pattern Implementation

		bool _shouldDispose = true;
		bool _isDisposed;

		/// <summary>
		/// Implements the Dispose pattern
		/// </summary>
		/// <param name="disposing">Whether this object is being disposed via a call to Dispose
		/// or garbage collected.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (!this._isDisposed)
			{
				if (disposing && _shouldDispose)
				{
					if (TransferAgent != null)
					{
						TransferAgent.Dispose();
						TransferAgent = null;
					}

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
}
