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

			address_domain = AddressDomain.Global;

			sse_by_inferior = new Dictionary<int, SingleSteppingEngine> ();
			process_by_id = new Dictionary<int, Process> ();
			exe_reader_by_id = new Dictionary<int, ExecutableReader> ();

			event_queue = new DebuggerEventQueue<Event> ("event_queue");

			engine_thread = new ST.Thread (new ST.ThreadStart (engine_thread_main));
			engine_thread.Start ();
		}

		protected readonly Debugger debugger;

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
					((IDisposable) ready_event).Dispose ();
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
			Report.Debug (DebugFlags.Wait, "Engine thread waiting");

			event_queue.Lock ();

			Report.Debug (DebugFlags.Wait, "Engine thread done waiting: {0}",
				      event_queue.Count);

			if (event_queue.Count == 0)
				event_queue.Wait ();

			var e = event_queue.Dequeue ();

			event_queue.Unlock ();

			Report.Debug (DebugFlags.Wait, "Engine thread got event: {0}",
				      e.ServerEvent != null ? e.ServerEvent.ToString () : "<delegate>");

			try {
				if (e.ServerEvent != null)
					HandleEvent (e.ServerEvent);
				else
					e.RunDelegate ();
			} catch (Exception ex) {
				Console.WriteLine ("SERVER EVENT EX: {0}", ex);
			}
		}

		#endregion

		internal void OnServerEvent (ServerEvent e)
		{
			Report.Debug (DebugFlags.Wait, "Server event: {0}", e);

			event_queue.Lock ();

			event_queue.Enqueue (new Event (e));

			Report.Debug (DebugFlags.Wait, "Server event #1: {0} / {1}", e,
				      event_queue.Count);

			if (event_queue.Count == 1)
				event_queue.Signal ();

			event_queue.Unlock ();

			Report.Debug (DebugFlags.Wait, "Server event #2");
		}

		Dictionary<int, SingleSteppingEngine> sse_by_inferior;
		Dictionary<int, Process> process_by_id;
		Dictionary<int, ExecutableReader> exe_reader_by_id;

		Process main_process;

		internal void AddEngine (IInferior inferior, SingleSteppingEngine sse)
		{
			lock (this) {
				sse_by_inferior.Add (inferior.ID, sse);
			}
		}

		internal void OnDllLoaded (Process process, IExecutableReader reader)
		{
			if (exe_reader_by_id.ContainsKey (reader.ID))
				return;

			Console.WriteLine ("DLL LOADED: {0}", reader.FileName);

			var exe = new ExecutableReader (process, TargetInfo, reader);
			exe_reader_by_id.Add (reader.ID, exe);
			exe.ReadDebuggingInfo ();
			process.OnDllLoaded (exe);
		}

		internal ExecutableReader CreateExeReader (Process process, IExecutableReader reader)
		{
			var exe = new ExecutableReader (process, TargetInfo, reader);
			exe_reader_by_id.Add (reader.ID, exe);
			return exe;
		}

		internal void OnThreadCreated (IInferior inferior)
		{
			if (sse_by_inferior.ContainsKey (inferior.ID))
				return;

			Console.WriteLine ("THREAD CREATED: {0}", inferior.ID);

			var sse = main_process.ThreadCreated (inferior);
			sse_by_inferior.Add (inferior.ID, sse);
		}

		void OnMonoRuntimeLoaded (Process process, IMonoRuntime runtime)
		{
			process.InitializeMono (runtime);
		}

		protected void HandleEvent (ServerEvent e)
		{
			switch (e.Type) {
			case ServerEventType.MainModuleLoaded:
			case ServerEventType.DllLoaded:
				OnDllLoaded (process_by_id [e.Sender.ID], (IExecutableReader) e.ArgumentObject);
				return;

			case ServerEventType.ThreadCreated:
				OnThreadCreated ((IInferior) e.ArgumentObject);
				return;

			case ServerEventType.MonoRuntimeLoaded:
				OnMonoRuntimeLoaded (process_by_id [e.Sender.ID], (IMonoRuntime) e.ArgumentObject);
				return;
			}

			switch (e.Type) {
			case ServerEventType.MainModuleLoaded:
			case ServerEventType.DllLoaded:
				OnDllLoaded (process_by_id [e.Sender.ID], (IExecutableReader) e.ArgumentObject);
				return;

			case ServerEventType.ThreadCreated:
				OnThreadCreated ((IInferior) e.ArgumentObject);
				return;

			case ServerEventType.MonoRuntimeLoaded:
				OnMonoRuntimeLoaded (process_by_id [e.Sender.ID], (IMonoRuntime) e.ArgumentObject);
				return;
			}

			if (e.Sender.Kind == ServerObjectKind.Inferior) {
				var inferior = (IInferior) e.Sender;

				if (!sse_by_inferior.ContainsKey (inferior.ID)) {
					Console.WriteLine ("UNKNOWN INFERIOR !");
					return;
				}

				var sse = sse_by_inferior[inferior.ID];
				sse.ProcessEvent (e);
				return;
			}

			Console.WriteLine ("UNKNOWN EVENT: {0}", e);
		}

#if DISABLED
		public Process OpenCoreFile (ProcessStart start, out Thread[] threads)
		{
			CoreFile core = CoreFile.OpenCoreFile (this, start);
			threads = core.GetThreads ();
			return core;
		}
#endif

#if FIXME
		internal void AddEngine (SingleSteppingEngine engine)
		{
			thread_hash.Add (engine.PID, engine);
			engine_hash.Add (engine.ID, engine);
		}

		internal SingleSteppingEngine GetEngine (int id)
		{
			return (SingleSteppingEngine) engine_hash [id];
		}
#endif

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
			if (cevent.Type == ServerEventType.Notification)
				return engine.Process.MonoManager.HandleEvent (engine, inferior, ref cevent, out resume_target);

			//
			// FIXME: This should be done in the server.
			//

			if (cevent.Type == ServerEventType.Stopped) {
				if (inferior.HasSignals) {
					if (cevent.Argument == inferior.SIGCHLD) {
						cevent = new ServerEvent (ServerEventType.Stopped, inferior.InferiorHandle, 0, 0, 0);
						resume_target = true;
						return true;
					} else if (inferior.Has_SIGWINCH && (cevent.Argument == inferior.SIGWINCH)) {
						resume_target = true;
						return true;
					} else if (inferior.Has_Kernel_SIGRTMIN && (cevent.Argument == inferior.Kernel_SIGRTMIN+1)) {
						// __SIGRTMIN and __SIGRTMIN+1 are used internally by the threading library
						resume_target = true;
						return true;
					}
				}
			}

			resume_target = false;
			return false;
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
			get { return ST.Thread.CurrentThread == engine_thread; }
		}

		internal DebuggerServer DebuggerServer {
			get { return debugger_server; }
		}

		object DoSendCommand (TargetDelegate dlg)
		{
			event_queue.Lock ();

			using (var e = new Event (dlg)) {
				event_queue.Enqueue (e);

				if (event_queue.Count == 1)
					event_queue.Signal ();

				event_queue.Unlock ();

				return e.Wait ();
			}
		}

		internal object SendCommand (SingleSteppingEngine sse, TargetAccessDelegate target, object user_data)
		{
			return DoSendCommand (delegate {
				return target (sse.Thread, user_data);
			});
		}

		public Process StartApplication (ProcessStart start, out CommandResult result)
		{
			var server_process = debugger_server.CreateProcess ();

			var process = main_process = new Process (this, server_process, start);
			process_by_id.Add (server_process.ID, process);

			result = (CommandResult) DoSendCommand (delegate {
				return process.StartApplication ();
			});

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

		public TargetInfo TargetInfo {
			get { return debugger_server.TargetInfo; }
		}

		public TargetMemoryInfo GetTargetMemoryInfo (AddressDomain domain)
		{
			return new TargetMemoryInfo (TargetInfo, domain);
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
#if FIXME
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
#else
			return new OperatingSystemBackend (process);
#endif
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
			var procs = process_by_id.Values.ToArray ();

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
