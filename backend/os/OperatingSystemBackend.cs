using System;
using System.IO;
using Mono.Debugger;
using Mono.Debugger.Server;
using System.Collections.Generic;

namespace Mono.Debugger.Backend
{
	internal abstract class OperatingSystemBackend : DebuggerMarshalByRefObject, IDisposable
	{
		public readonly Process Process;
		public readonly DebuggerServer Server;

		protected readonly Dictionary<string,ExecutableReader> reader_hash;
		ExecutableReader main_reader;

		protected OperatingSystemBackend (Process process)
		{
			this.Process = process;
			this.Server = process.ThreadManager.DebuggerServer;
			reader_hash = new Dictionary<string,ExecutableReader> ();
		}

		public ExecutableReader LoadExecutable (TargetMemoryInfo memory, string filename,
							bool load_native_symtabs)
		{
			lock (this) {
				check_disposed ();
				if (reader_hash.ContainsKey (filename))
					return reader_hash [filename];

				var reader = Server.GetExecutableReader (this, memory, filename, TargetAddress.Null, load_native_symtabs);
				reader_hash.Add (filename, reader);
				main_reader = reader;
				return reader;
			}
		}

		public ExecutableReader AddExecutableFile (Inferior inferior, string filename,TargetAddress base_address,
							   bool step_into, bool is_loaded)
		{
			lock (this) {
				check_disposed ();
				if (reader_hash.ContainsKey (filename))
					return reader_hash [filename];

				var reader = Server.GetExecutableReader (this, inferior.TargetMemoryInfo, filename, base_address, is_loaded);
				reader_hash.Add (filename, reader);
				CheckLoadedLibrary (inferior, reader);
				return reader;
			}
		}

		public ExecutableReader LookupLibrary (string name)
		{
			lock (this) {
				foreach (ExecutableReader reader in reader_hash.Values) {
					if (Path.GetFileName (reader.FileName) == name)
						return reader;
				}

				return null;
			}
		}

		public ExecutableReader LookupLibrary (TargetAddress address)
		{
			lock (this) {
				foreach (ExecutableReader reader in reader_hash.Values) {
					if (!reader.IsContinuous)
						continue;

					if ((address >= reader.StartAddress) && (address < reader.EndAddress))
						return reader;
				}

				return null;
			}
		}

		public TargetAddress LookupSymbol (string name)
		{
			lock (this) {
				foreach (ExecutableReader reader in reader_hash.Values) {
					TargetAddress symbol = reader.LookupSymbol (name);
					if (!symbol.IsNull)
						return symbol;
				}

				return TargetAddress.Null;
			}
		}

		protected virtual void CheckLoadedLibrary (Inferior inferior, ExecutableReader reader)
		{ }

		public abstract bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start);

		internal void UpdateSharedLibraries (Inferior inferior)
		{
			lock (this) {
				// This fails if it's a statically linked executable.
				try {
					DoUpdateSharedLibraries (inferior, main_reader);
				} catch (TargetException ex) {
					Report.Error ("Failed to read shared libraries: {0}", ex.Message);
				} catch (Exception ex) {
					Report.Error ("Failed to read shared libraries: {0}", ex);
				}
			}
		}

		protected abstract void DoUpdateSharedLibraries (Inferior inferior, ExecutableReader main_reader);

		internal abstract bool CheckForPendingMonoInit (Inferior inferior);

		internal void ReadNativeTypes ()
		{
			lock (this) {
				foreach (ExecutableReader reader in reader_hash.Values) {
					reader.ReadDebuggingInfo ();
				}
			}
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
