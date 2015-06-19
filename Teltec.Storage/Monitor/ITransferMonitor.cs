﻿using System;

namespace Teltec.Storage.Monitor
{
	public interface ITransferMonitor
	{
		void ClearTransfers();
		void TransferAdded(object sender, TransferFileProgressArgs args);
		void TransferStarted(object sender, TransferFileProgressArgs args);
		void TransferProgress(object sender, TransferFileProgressArgs args);
		void TransferFailed(object sender, TransferFileProgressArgs args, Exception e);
		void TransferCanceled(object sender, TransferFileProgressArgs args, Exception e);
		void TransferCompleted(object sender, TransferFileProgressArgs args);
	}
}
