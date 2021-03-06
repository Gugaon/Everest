/* 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using Teltec.Everest.Data.Versioning;
using Teltec.Storage;
using Teltec.Storage.Backend;

namespace Teltec.Everest.PlanExecutor.Backup
{
	public class CustomBackupAgent : BackupAgent<CustomVersionedFile>
	{
		public CustomBackupAgent(ITransferAgent agent)
			: base(agent)
		{
		}
	}
}
