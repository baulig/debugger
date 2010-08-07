using System;
using System.IO;
using System.Linq;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Languages;
using Mono.Debugger.Server;

namespace Mono.Debugger.Backend
{
	internal abstract class ThreadManager : DebuggerMarshalByRefObject
	{
		public static TimeSpan WaitTimeout = TimeSpan.FromMilliseconds (5000);

		internal ThreadManager (Debugger debugger, DebuggerServer server)
		{
			this.debugger = debugger;
			this.debugger_server = server;

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			engine_hash = Hashtable.Synchronized (new Hashtable ());
			processes = ArrayList.Synchronized (new ArrayList ());

			address_domain = AddressDomain.Global;
		}

		protected readonly Debugger debugger;
		protected Hashtable thread_hash;
		protected Hashtable engine_hash;
		protected ArrayList processes;

		protected readonly AddressDomain address_domain;

		protected readonly DebuggerServer debugger_server;

#if DISABLED
		public Process OpenCoreFile (ProcessStart start, out Thread[] threads)
		{
			CoreFile core = CoreFile.OpenCoreFile (this, start);
			threads = core.GetThreads ();
			return core;
		}
#endif

		internal void AddEngine (SingleSteppingEngine engine)
		{
			thread_hash.Add (engine.PID, engine);
			engine_hash.Add (engine.ID, engine);
		}

		internal void RemoveProcess (Process process)
		{
			processes.Remove (process);
		}

		internal SingleSteppingEngine GetEngine (int id)
		{
			return (SingleSteppingEngine) engine_hash [id];
		}

		public abstract bool HasTarget {
			get;
		}

		static int next_process_id = 0;
		internal int NextThreadID {
			get { return ++next_process_id; }
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref DebuggerServer.ChildEvent cevent, out bool resume_target)
		{
			if (cevent.Type == DebuggerServer.ChildEventType.NONE) {
				resume_target = true;
				return true;
			}

			if (cevent.Type == DebuggerServer.ChildEventType.CHILD_CREATED_THREAD) {
				int pid = (int) cevent.Argument;
				inferior.Process.ThreadCreated (inferior, pid, false, true);
				GetPendingSigstopForNewThread (pid);
				resume_target = true;
				return true;
			}

			if (cevent.Type == DebuggerServer.ChildEventType.CHILD_FORKED) {
				inferior.Process.ChildForked (inferior, (int) cevent.Argument);
				resume_target = true;
				return true;
			}

			if (cevent.Type == DebuggerServer.ChildEventType.CHILD_EXECD) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				inferior.Process.ChildExecd (engine, inferior);
				resume_target = false;
				return true;
			}

			if (cevent.Type == DebuggerServer.ChildEventType.CHILD_STOPPED) {
				if (cevent.Argument == inferior.SIGCHLD) {
					cevent = new DebuggerServer.ChildEvent (
						DebuggerServer.ChildEventType.CHILD_STOPPED, 0, 0, 0);
					resume_target = true;
					return true;
				} else if (inferior.Has_SIGWINCH && (cevent.Argument == inferior.SIGWINCH)) {
					resume_target = true;
					return true;
				} else if (inferior.HasSignals && (cevent.Argument == inferior.Kernel_SIGRTMIN+1)) {
					// __SIGRTMIN and __SIGRTMIN+1 are used internally by the threading library
					resume_target = true;
					return true;
				}
			}

			if (inferior.Process.OperatingSystem.CheckForPendingMonoInit (inferior)) {
				resume_target = true;
				return true;
			}

			bool retval = false;
			resume_target = false;
			if (inferior.Process.MonoManager != null)
				retval = inferior.Process.MonoManager.HandleChildEvent (
					engine, inferior, ref cevent, out resume_target);

			if ((cevent.Type == DebuggerServer.ChildEventType.CHILD_EXITED) ||
			    (cevent.Type == DebuggerServer.ChildEventType.CHILD_SIGNALED)) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				engine.OnThreadExited (cevent);
				resume_target = false;
				return true;
			}

			return retval;
		}

		internal abstract bool GetPendingSigstopForNewThread (int pid);

		public Debugger Debugger {
			get { return debugger; }
		}

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		internal abstract bool InBackgroundThread {
			get;
		}

		internal DebuggerServer DebuggerServer {
			get { return debugger_server; }
		}

		internal abstract object SendCommand (SingleSteppingEngine sse, TargetAccessDelegate target,
						      object user_data);

		public abstract Process StartApplication (ProcessStart start, out CommandResult result);

		internal abstract void AddPendingEvent (SingleSteppingEngine engine, DebuggerServer.ChildEvent cevent);

		protected static void check_error (TargetError error)
		{
			if (error == TargetError.None)
				return;

			throw new TargetException (error);
		}

		public TargetInfo GetTargetInfo ()
		{
			return debugger_server.GetTargetInfo ();
		}

		public TargetMemoryInfo GetTargetMemoryInfo (AddressDomain domain)
		{
			return new TargetMemoryInfo (debugger_server.GetTargetInfo (), domain);
		}

		public bool HasThreadEvents {
			get {
				return (debugger_server.Capabilities & DebuggerServer.ServerCapabilities.THREAD_EVENTS) != 0;
			}
		}

		//
		// Whether we can detach from any target.
		//
		// Background:
		//
		// The Linux kernel allows detaching from any traced child, even if we did not
		// previously attach to it.
		//

		public bool CanDetachAny {
			get {
				return (debugger_server.Capabilities & DebuggerServer.ServerCapabilities.CAN_DETACH_ANY) != 0;
			}
		}

		public OperatingSystemBackend CreateOperatingSystemBackend (Process process)
		{
			switch (debugger_server.Type) {
			case DebuggerServer.ServerType.LINUX_PTRACE:
				return new LinuxOperatingSystem (process);
			case DebuggerServer.ServerType.DARWIN:
				return new DarwinOperatingSystem (process);
			case DebuggerServer.ServerType.WINDOWS:
				return new WindowsOperatingSystem (process);
			default:
				throw new NotSupportedException (String.Format ("Unknown server type {0}.", debugger_server.Type));
			}
		}


#region IDisposable implementation
		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadManager");
		}

		protected virtual void DoDispose ()
		{
			Process[] procs = new Process [processes.Count];
			processes.CopyTo (procs, 0);

			for (int i = 0; i < procs.Length; i++)
				procs [i].Dispose ();
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			if (disposing) {
				DoDispose ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion

		~ThreadManager ()
		{
			Dispose (false);
		}
	}
}
