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
	internal class ThreadManager : DebuggerMarshalByRefObject
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

			sse_by_inferior = new Dictionary<int, SingleSteppingEngine> ();

			event_queue = new DebuggerEventQueue<Event> ("event_queue");

			engine_thread = new ST.Thread (new ST.ThreadStart (engine_thread_main));
			engine_thread.Start ();
		}

		protected readonly Debugger debugger;
		protected Hashtable thread_hash;
		protected Hashtable engine_hash;
		protected ArrayList processes;

		protected readonly AddressDomain address_domain;
		protected readonly DebuggerServer debugger_server;

		#region Main Event Loop

		delegate object TargetDelegate ();

		class Event : IDisposable
		{
			public readonly ServerEvent ServerEvent;
			public readonly TargetDelegate Delegate;

			ST.ManualResetEvent ready_event;
			object result;

			public Event (ServerEvent e)
			{
				this.ServerEvent = e;
			}

			public Event (TargetDelegate dlg)
			{
				this.Delegate = dlg;
				ready_event = new ST.ManualResetEvent (false);
			}

			public object Wait ()
			{
				ready_event.WaitOne ();
				return result;
			}

			public void RunDelegate ()
			{
				result = Delegate ();
				ready_event.Set ();
			}

			public void Dispose ()
			{
				if (ready_event != null) {
					ready_event.Dispose ();
					ready_event = null;
				}
			}
		}

		ST.Thread engine_thread;
		DebuggerEventQueue<Event> event_queue;

		void engine_thread_main ()
		{
			while (true) {
				try {
					engine_thread_iteration ();
				} catch (Exception ex) {
					Console.WriteLine ("EVENT THREAD EX: {0}", ex);
				}
			}
		}

		void engine_thread_iteration ()
		{
			event_queue.Lock ();

			if (event_queue.Count == 0)
				event_queue.Wait ();

			var e = event_queue.Dequeue ();

			event_queue.Unlock ();

			lock (this) {
				if (e.ServerEvent != null)
					HandleEvent (e.ServerEvent);
				else
					e.RunDelegate ();
				e.Dispose ();
			}
		}

		#endregion

		internal void OnServerEvent (ServerEvent e)
		{
			event_queue.Lock ();

			event_queue.Enqueue (new Event (e));

			if (event_queue.Count == 1)
				event_queue.Signal ();

			event_queue.Unlock ();
		}

		Dictionary<int, SingleSteppingEngine> sse_by_inferior;
		Process main_process;

		internal void AddEngine (IInferior inferior, SingleSteppingEngine sse)
		{
			lock (this) {
				sse_by_inferior.Add (inferior.ID, sse);
			}
		}

		void OnDllLoaded (IExecutableReader reader)
		{
			Console.WriteLine ("DLL LOADED: {0}", reader.FileName);

			var exe = new ExecutableReader (main_process, null, reader);
			exe.ReadDebuggingInfo ();
		}

		void OnThreadCreated (IInferior inferior)
		{
			Console.WriteLine ("THREAD CREATED: {0}", inferior.ID);

			var sse = main_process.ThreadCreated (inferior);
			sse_by_inferior.Add (inferior.ID, sse);
		}

		protected void HandleEvent (ServerEvent e)
		{
			Console.WriteLine ("SERVER EVENT: {0} {1}", e, DebuggerWaitHandle.CurrentThread);

			if (e.Sender.Kind == ServerObjectKind.Inferior) {
				var inferior = (IInferior) e.Sender;
				Console.WriteLine ("INFERIOR EVENT: {0}", inferior.ID);

				if (!sse_by_inferior.ContainsKey (inferior.ID)) {
					Console.WriteLine ("UNKNOWN INFERIOR !");
					return;
				}

				var sse = sse_by_inferior[inferior.ID];
				sse.ProcessEvent (e);
				return;
			}

			switch (e.Type) {
			case ServerEventType.MainModuleLoaded:
			case ServerEventType.DllLoaded:
				OnDllLoaded ((IExecutableReader) e.ArgumentObject);
				break;

			case ServerEventType.ThreadCreated:
				OnThreadCreated ((IInferior) e.ArgumentObject);
				break;
			}
		}

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

		public bool HasTarget {
			get { return debugger_server != null; }
		}

		static int next_process_id = 0;
		internal int NextThreadID {
			get { return ++next_process_id; }
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref ServerEvent cevent, out bool resume_target)
		{
			if (cevent.Type == ServerEventType.None) {
				resume_target = true;
				return true;
			}

#if FIXME
			if (cevent.Type == ServerEventType.ThreadCreated) {
				int pid = (int) cevent.Argument;
				inferior.Process.ThreadCreated (inferior, pid, false, true);
				GetPendingSigstopForNewThread (pid);
				resume_target = true;
				return true;
			}

			if (cevent.Type == ServerEventType.Forked) {
				inferior.Process.ChildForked (inferior, (int) cevent.Argument);
				resume_target = true;
				return true;
			}

			if (cevent.Type == ServerEventType.Execd) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				inferior.Process.ChildExecd (engine, inferior);
				resume_target = false;
				return true;
			}
#endif

			if (cevent.Type == ServerEventType.Stopped) {
				if (inferior.HasSignals) {
					if (cevent.Argument == inferior.SIGCHLD) {
						cevent = new ServerEvent (ServerEventType.Stopped, inferior.InferiorHandle, 0, 0, 0);
						resume_target = true;
						return true;
					} else if (inferior.Has_SIGWINCH && (cevent.Argument == inferior.SIGWINCH)) {
						resume_target = true;
						return true;
					} else if (cevent.Argument == inferior.Kernel_SIGRTMIN+1) {
						// __SIGRTMIN and __SIGRTMIN+1 are used internally by the threading library
						resume_target = true;
						return true;
					}
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

			if ((cevent.Type == ServerEventType.Exited) ||
			    (cevent.Type == ServerEventType.Signaled)) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				engine.OnThreadExited (cevent);
				resume_target = false;
				return true;
			}

			return retval;
		}

		internal bool GetPendingSigstopForNewThread (int pid)
		{
			return false;
		}

		public Debugger Debugger {
			get { return debugger; }
		}

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		internal bool InBackgroundThread {
			get { return true; }
		}

		internal DebuggerServer DebuggerServer {
			get { return debugger_server; }
		}

		internal object SendCommand (SingleSteppingEngine sse, TargetAccessDelegate target, object user_data)
		{
			event_queue.Lock ();

			var dlg = new TargetDelegate (delegate {
				return target (sse.Thread, user_data);
			});

			var e = new Event (dlg);
			event_queue.Enqueue (e);

			if (event_queue.Count == 1)
				event_queue.Signal ();

			event_queue.Unlock ();

			return e.Wait ();
		}

		public Process StartApplication (ProcessStart start, out CommandResult result)
		{
			Process process = main_process = new Process (this, start);
			processes.Add (process);

			result = process.StartApplication ();

			return process;
		}

		internal void AddPendingEvent (SingleSteppingEngine engine, ServerEvent cevent)
		{
			throw new InvalidOperationException ();
		}

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
				return (debugger_server.Capabilities & ServerCapabilities.ThreadEvents) != 0;
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
				return (debugger_server.Capabilities & ServerCapabilities.CanDetachAny) != 0;
			}
		}

		public OperatingSystemBackend CreateOperatingSystemBackend (Process process)
		{
			switch (debugger_server.Type) {
			case ServerType.LinuxPTrace:
				return new LinuxOperatingSystem (process);
			case ServerType.Darwin:
				return new DarwinOperatingSystem (process);
			case ServerType.Windows:
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
