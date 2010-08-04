using System;

using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal abstract class NativeExecutableReader : DebuggerMarshalByRefObject, IDisposable
	{
		public abstract TargetMemoryInfo TargetMemoryInfo {
			get;
		}

		public abstract Module Module {
			get;
		}

		public abstract string FileName {
			get;
		}

		public abstract bool IsLoaded {
			get;
		}

		public abstract string TargetName {
			get;
		}

		public abstract bool IsContinuous {
			get;
		}

		public abstract TargetAddress StartAddress {
			get;
		}

		public abstract TargetAddress EndAddress {
			get;
		}

		public abstract TargetAddress BaseAddress {
			get;
		}

		public abstract TargetAddress LookupSymbol (string name);

		public abstract TargetAddress LookupLocalSymbol (string name);

		public abstract bool HasSection (string name);

		public abstract TargetAddress GetSectionAddress (string name);

		public abstract byte[] GetSectionContents (string name);

		public abstract TargetAddress EntryPoint {
			get;
		}

		public abstract void ReadDebuggingInfo ();

		internal abstract TargetAddress ReadDynamicInfo (Inferior inferior);

		//
		// IDisposable
		//

		private bool disposed = false;

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

		~NativeExecutableReader ()
		{
			Dispose (false);
		}
	}
}
