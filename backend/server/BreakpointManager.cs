using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Server
{
	internal abstract class BreakpointManager : IDisposable
	{
		protected Dictionary<int,BreakpointEntry> bpt_by_index;

		protected BreakpointManager ()
		{
			bpt_by_index = new Dictionary<int,BreakpointEntry> ();
		}

		protected BreakpointManager (BreakpointManager old)
		{
			foreach (int index in old.bpt_by_index.Keys) {
				bpt_by_index.Add (index, old.bpt_by_index [index]);
			}
		}

		protected virtual void Lock ()
		{
			Monitor.Enter (this);
		}

		protected virtual void Unlock ()
		{
			Monitor.Exit (this);
		}

		public abstract BreakpointManager Clone ();

		public abstract BreakpointHandle LookupBreakpoint (TargetAddress address,
								  out int index, out bool is_enabled);

		public BreakpointHandle LookupBreakpoint (int index)
		{
			Lock ();
			try {
				if (!bpt_by_index.ContainsKey (index))
					return null;
				return bpt_by_index [index].Handle;
			} finally {
				Unlock ();
			}
		}

		public abstract bool IsBreakpointEnabled (int breakpoint);

		public int InsertBreakpoint (Inferior inferior, BreakpointHandle handle,
					     TargetAddress address, int domain)
		{
			Lock ();
			try {
				int index;
				bool is_enabled;
				BreakpointHandle old = LookupBreakpoint (
					address, out index, out is_enabled);
				if (old != null)
					throw new TargetException (
						TargetError.AlreadyHaveBreakpoint,
						"Already have breakpoint {0} at address {1}.",
						old.Breakpoint.Index, address);

				int dr_index = -1;
				switch (handle.Breakpoint.Type) {
				case EventType.Breakpoint:
					index = inferior.InsertBreakpoint (address);
					break;

				case EventType.WatchRead:
					index = inferior.InsertHardwareWatchPoint (
						address, DebuggerServer.HardwareBreakpointType.READ,
						out dr_index);
					break;

				case EventType.WatchWrite:
					index = inferior.InsertHardwareWatchPoint (
						address, DebuggerServer.HardwareBreakpointType.WRITE,
						out dr_index);
					break;

				default:
					throw new InternalError ();
				}

				bpt_by_index.Add (index, new BreakpointEntry (handle, domain));
				return index;
			} finally {
				Unlock ();
			}
		}

		public void RemoveBreakpoint (Inferior inferior, BreakpointHandle handle)
		{
			Lock ();
			try {
				int[] indices = new int [bpt_by_index.Count];
				bpt_by_index.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					BreakpointEntry entry = bpt_by_index [indices [i]];
					if (entry.Handle != handle)
						continue;
					inferior.RemoveBreakpoint (indices [i]);
					bpt_by_index.Remove (indices [i]);
				}
			} finally {
				Unlock ();
			}
		}

		public void InitializeAfterFork (Inferior inferior)
		{
			Lock ();
			try {
				int[] indices = new int [bpt_by_index.Count];
				bpt_by_index.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					int idx = indices [i];
					BreakpointEntry entry = bpt_by_index [idx];
					SourceBreakpoint bpt = entry.Handle.Breakpoint as SourceBreakpoint;

					if (!entry.Handle.Breakpoint.ThreadGroup.IsGlobal) {
						try {
							inferior.RemoveBreakpoint (idx);
						} catch (Exception ex) {
							Report.Error ("Removing breakpoint {0} failed: {1}",
								      idx, ex);
						}
					}
				}
			} finally {
				Unlock ();
			}
		}

		public void RemoveAllBreakpoints (Inferior inferior)
		{
			Lock ();
			try {
				int[] indices = new int [bpt_by_index.Count];
				bpt_by_index.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					try {
						inferior.RemoveBreakpoint (indices [i]);
					} catch (Exception ex) {
						Report.Error ("Removing breakpoint {0} failed: {1}",
							      indices [i], ex);
					}
				}
			} finally {
				Unlock ();
			}
		}

		public void DomainUnload (Inferior inferior, int domain)
		{
			Lock ();
			try {
				int[] indices = new int [bpt_by_index.Count];
				bpt_by_index.Keys.CopyTo (indices, 0);

				for (int i = 0; i < indices.Length; i++) {
					BreakpointEntry entry = bpt_by_index [indices [i]];
					if (entry.Domain != domain)
						continue;
					inferior.RemoveBreakpoint (indices [i]);
					bpt_by_index.Remove (indices [i]);
				}
			} finally {
				Unlock ();
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("BreakpointManager");
		}

		protected virtual void DoDispose ()
		{ }

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (disposed)
				return;

			lock (this) {
				if (disposed)
					return;

				disposed = true;
				DoDispose ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~BreakpointManager ()
		{
			Dispose (false);
		}

		protected struct BreakpointEntry
		{
			public readonly BreakpointHandle Handle;
			public readonly int Domain;

			public BreakpointEntry (BreakpointHandle handle, int domain)
			{
				this.Handle = handle;
				this.Domain = domain;
			}

			public override int GetHashCode ()
			{
				return Handle.GetHashCode ();
			}

			public override bool Equals (object obj)
			{
				BreakpointEntry entry = (BreakpointEntry) obj;

				return (entry.Handle == Handle) && (entry.Domain == Domain);
			}

			public override string ToString ()
			{
				return String.Format ("BreakpointEntry ({0}:{1})", Handle, Domain);
			}
		}
	}
}