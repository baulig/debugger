using System;
using System.IO;
using Mono.Debugger;
using Mono.Debugger.Server;
using System.Collections.Generic;

namespace Mono.Debugger.Backend
{
	internal class OperatingSystemBackend : DebuggerMarshalByRefObject, IDisposable
	{
		public readonly Process Process;

		internal OperatingSystemBackend (Process process)
		{
			this.Process = process;
		}

		protected virtual void CheckLoadedLibrary (Inferior inferior, ExecutableReader reader)
		{ }

		public virtual bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						   out TargetAddress trampoline, out bool is_start)
		{
			trampoline = TargetAddress.Null;
			is_start = false;
			return false;
		}

		internal void UpdateSharedLibraries (Inferior inferior)
		{ }

		internal virtual bool CheckForPendingMonoInit (Inferior inferior)
		{
			return false;
		}

#region IDisposable

		//
		// IDisposable
		//

		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException (GetType ().Name);
		}

		protected virtual void DoDispose ()
		{ }

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing)
				DoDispose ();
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~OperatingSystemBackend ()
		{
			Dispose (false);
		}

#endregion
	}
}
