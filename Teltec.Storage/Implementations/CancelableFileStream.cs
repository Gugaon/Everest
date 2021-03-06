/* 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System.IO;
using System.Threading;

namespace Teltec.Storage.Implementations
{
	// This is a hack just to make the synchronous upload/download operations cancelable.
	// We could just switch to asynchronous APIs but this would cause more tasks to be queued - we don't want it!
	public class CancelableFileStream : FileStream
	{
		private CancellationToken _CancellationToken;

		public CancelableFileStream(string path, FileMode mode, FileAccess access, CancellationToken cancellationToken)
			: base(ZetaLongPaths.ZlpIOHelper.CreateFileHandle(
				path,
				(ZetaLongPaths.Native.CreationDisposition)mode,
				(ZetaLongPaths.Native.FileAccess)access,
				ZetaLongPaths.Native.FileShare.Read), access)
		{
			_CancellationToken = cancellationToken;
		}

		public CancelableFileStream(string path, FileMode mode, FileAccess access, int bufferSize, CancellationToken cancellationToken)
			: base(ZetaLongPaths.ZlpIOHelper.CreateFileHandle(
				path,
				(ZetaLongPaths.Native.CreationDisposition)mode,
				(ZetaLongPaths.Native.FileAccess)access,
				ZetaLongPaths.Native.FileShare.Read), access, bufferSize)
		{
			_CancellationToken = cancellationToken;
		}

		public override int Read(byte[] array, int offset, int count)
		{
			if (_CancellationToken != null)
				_CancellationToken.ThrowIfCancellationRequested();
			return base.Read(array, offset, count);
		}

		public override void Write(byte[] array, int offset, int count)
		{
			if (_CancellationToken != null)
				_CancellationToken.ThrowIfCancellationRequested();
			base.Write(array, offset, count);
		}
	}
}
