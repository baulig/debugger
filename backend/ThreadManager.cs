using System;
using System.IO;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal class ThreadManager : DebuggerMarshalByRefObject
	{
		public static TimeSpan WaitTimeout = TimeSpan.FromMilliseconds (5000);

		internal ThreadManager (DebuggerServant backend)
		{
			this.backend = backend;

			thread_hash = Hashtable.Synchronized (new Hashtable ());
			engine_hash = Hashtable.Synchronized (new Hashtable ());
			processes = ArrayList.Synchronized (new ArrayList ());

			pending_events = Hashtable.Synchronized (new Hashtable ());
			
			address_domain = AddressDomain.Global;

			command_mutex = new DebuggerMutex ("command_mutex");
			command_mutex.DebugFlags = DebugFlags.Wait;

			wait_event = new ST.AutoResetEvent (false);
			idle_event = new ST.ManualResetEvent (false);
			engine_event = new ST.ManualResetEvent (true);
			ready_event = new ST.ManualResetEvent (false);

			event_queue = new DebuggerEventQueue ("event_queue");
			event_queue.DebugFlags = DebugFlags.Wait;

			mono_debugger_server_global_init ();

			wait_thread = new ST.Thread (new ST.ThreadStart (start_wait_thread));
			wait_thread.IsBackground = true;
			wait_thread.Start ();

			inferior_thread = new ST.Thread (new ST.ThreadStart (start_inferior));
			inferior_thread.IsBackground = true;
			inferior_thread.Start ();

			ready_event.WaitOne ();
		}

		ProcessStart start;
		DebuggerServant backend;
		DebuggerEventQueue event_queue;
		ST.Thread inferior_thread;
		ST.Thread wait_thread;
		ST.ManualResetEvent ready_event;
		ST.ManualResetEvent idle_event;
		ST.ManualResetEvent engine_event;
		ST.AutoResetEvent wait_event;
		Hashtable thread_hash;
		Hashtable engine_hash;
		Hashtable pending_events;
		ArrayList processes;

		AddressDomain address_domain;

		DebuggerMutex command_mutex;
		bool abort_requested;
		bool waiting;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_global_init ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_global_wait (out int status);

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_get_pending_sigint ();

		void start_inferior ()
		{
			event_queue.Lock ();
			ready_event.Set ();

			while (!abort_requested) {
				engine_thread_main ();
			}
		}

		// <remarks>
		//   These three variables are shared between the two threads, so you need to
		//   lock (this) before accessing/modifying them.
		// </remarks>
		Command current_command = null;
		SingleSteppingEngine current_event = null;
		int current_event_status = 0;

		public ProcessServant StartApplication (ProcessStart start)
		{
			ProcessServant process = CreateProcess (start);
			process.WaitForApplication ();
			return process;
		}

		public ProcessServant OpenCoreFile (ProcessStart start, out Thread[] threads)
		{
			this.start = start;

			CoreFile core = CoreFile.OpenCoreFile (this, start);
			threads = core.GetThreads ();
			return core;
		}

		internal void AddEngine (SingleSteppingEngine engine)
		{
			thread_hash.Add (engine.PID, engine);
			engine_hash.Add (engine.ID, engine);
		}

		internal void ProcessExecd (SingleSteppingEngine engine)
		{
			SingleSteppingEngine old_engine = (SingleSteppingEngine) thread_hash [engine.PID];
			if (old_engine != null) {
				thread_hash [engine.PID] = engine;
				engine_hash.Remove (old_engine.ID);
			} else
				thread_hash.Add (engine.PID, engine);
			engine_hash.Add (engine.ID, engine);
		}

		internal void RemoveProcess (ProcessServant process)
		{
			processes.Remove (process);
		}

		internal SingleSteppingEngine GetEngine (int id)
		{
			return (SingleSteppingEngine) engine_hash [id];
		}

		public bool HasTarget {
			get { return inferior_thread != null; }
		}

		static int next_process_id = 0;
		internal int NextThreadID {
			get { return ++next_process_id; }
		}

		internal bool HandleChildEvent (SingleSteppingEngine engine, Inferior inferior,
						ref Inferior.ChildEvent cevent, out bool resume_target)
		{
			if (cevent.Type == Inferior.ChildEventType.NONE) {
				resume_target = true;
				return true;
			}

			inferior.Process.Initialize (engine, inferior, false);

			if (cevent.Type == Inferior.ChildEventType.CHILD_CREATED_THREAD) {
				inferior.Process.ThreadCreated (inferior, (int) cevent.Argument, false);
				resume_target = true;
				return true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_FORKED) {
				inferior.Process.ChildForked (inferior, (int) cevent.Argument);
				resume_target = true;
				return true;
			}

			if (cevent.Type == Inferior.ChildEventType.CHILD_EXECD) {
				inferior.Process.ChildExecd (inferior);
				resume_target = false;
				return true;
			}

			if ((cevent.Type == Inferior.ChildEventType.CHILD_STOPPED) &&
			    (cevent.Argument == inferior.SIGCHLD)) {
				cevent = new Inferior.ChildEvent (
					Inferior.ChildEventType.CHILD_STOPPED, 0, 0, 0);
				resume_target = false;
				return false;
			}

			bool retval = false;
			resume_target = false;
			if (inferior.Process.MonoManager != null)
				retval = inferior.Process.MonoManager.HandleChildEvent (
					engine, inferior, ref cevent, out resume_target);

			if ((cevent.Type == Inferior.ChildEventType.CHILD_EXITED) ||
			     (cevent.Type == Inferior.ChildEventType.CHILD_SIGNALED)) {
				thread_hash.Remove (engine.PID);
				engine_hash.Remove (engine.ID);
				engine.OnThreadExited (cevent);
				resume_target = false;
				return true;
			}

			return retval;
		}

		public DebuggerServant Debugger {
			get { return backend; }
		}

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		internal bool InBackgroundThread {
			get { return ST.Thread.CurrentThread == inferior_thread; }
		}

		internal object SendCommand (SingleSteppingEngine sse, TargetAccessDelegate target,
					     object user_data)
		{
			Command command = new Command (sse, target, user_data);

			if (!engine_event.WaitOne (WaitTimeout, false))
				throw new TargetException (TargetError.NotStopped);

			event_queue.Lock ();
			engine_event.Reset ();

			current_command = command;

			event_queue.Signal ();
			event_queue.Unlock ();

			engine_event.WaitOne ();

			if (command.Result is Exception)
				throw (Exception) command.Result;
			else
				return command.Result;
		}

		internal ProcessServant CreateProcess (ProcessStart start)
		{
			Command command = new Command (CommandType.CreateProcess, start);

			if (!engine_event.WaitOne (WaitTimeout, false))
				throw new TargetException (TargetError.NotStopped);

			event_queue.Lock ();
			engine_event.Reset ();

			current_command = command;

			event_queue.Signal ();
			event_queue.Unlock ();

			engine_event.WaitOne ();

			if (command.Result is Exception)
				throw (Exception) command.Result;
			else
				return (ProcessServant) command.Result;
		}

		internal void AddPendingEvent (SingleSteppingEngine engine, Inferior.ChildEvent cevent)
		{
			pending_events.Add (engine, cevent);
		}

		// <summary>
		//   The heart of the SingleSteppingEngine.  This runs in a background
		//   thread and processes stepping commands and events.
		//
		//   For each application we're debugging, there is just one SingleSteppingEngine,
		//   no matter how many threads the application has.  The engine is using one single
		//   event loop which is processing commands from the user and events from all of
		//   the application's threads.
		// </summary>
		void engine_thread_main ()
		{
			Report.Debug (DebugFlags.Wait, "ThreadManager waiting");

			event_queue.Wait ();

			Report.Debug (DebugFlags.Wait, "ThreadManager done waiting");

			if (abort_requested) {
				Report.Debug (DebugFlags.Wait, "Engine thread abort requested");
				return;
			}

			int status;
			SingleSteppingEngine event_engine;
			Command command;

			Report.Debug (DebugFlags.Wait, "ThreadManager woke up: {0} {1:x} {2}",
				      current_event, current_event_status, current_command);

			event_engine = current_event;
			status = current_event_status;

			current_event = null;
			current_event_status = 0;

			command = current_command;
			current_command = null;

			if (event_engine != null) {
				try {
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process event: {1}",
						      DebuggerWaitHandle.CurrentThread, event_engine);
					event_engine.ProcessEvent (status);
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process event done: {1}",
						      DebuggerWaitHandle.CurrentThread, event_engine);
				} catch (ST.ThreadAbortException) {
					;
				} catch (Exception e) {
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager caught exception: {0}", e);
					Console.WriteLine ("EXCEPTION: {0}", e);
				}

				check_pending_events ();

				engine_event.Set ();
				RequestWait ();
			}

			if (command == null)
				return;

			// These are synchronous commands; ie. the caller blocks on us
			// until we finished the command and sent the result.
			if (command.Type == CommandType.TargetAccess) {
				try {
					command.Result = command.Engine.Invoke (
						(TargetAccessDelegate) command.Data1, command.Data2);
				} catch (ST.ThreadAbortException) {
					return;
				} catch (Exception ex) {
					command.Result = ex;
				}

				engine_event.Set ();
			} else if (command.Type == CommandType.CreateProcess) {
				try {
					ProcessStart start = (ProcessStart) command.Data1;
					ProcessServant process = new ProcessServant (this, start);

					CommandResult result;
					SingleSteppingEngine sse = new SingleSteppingEngine (
						this, process, start, out result);

					thread_hash.Add (sse.PID, sse);
					engine_hash.Add (sse.ID, sse);
					processes.Add (process);

					RequestWait ();

					command.Result = process;
				} catch (ST.ThreadAbortException) {
					return;
				} catch (Exception ex) {
					command.Result = ex;
				}

				engine_event.Set ();
			} else {
				throw new InvalidOperationException ();
			}
		}

		void check_pending_events ()
		{
			SingleSteppingEngine[] list = new SingleSteppingEngine [pending_events.Count];
			pending_events.Keys.CopyTo (list, 0);

			for (int i = 0; i < list.Length; i++) {
				SingleSteppingEngine engine = list [i];
				if (engine.Process.HasThreadLock)
					continue;

				Inferior.ChildEvent cevent = (Inferior.ChildEvent) pending_events [engine];
				pending_events.Remove (engine);

				try {
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process pending event: {1} {2}",
						      DebuggerWaitHandle.CurrentThread, engine, cevent);
					engine.ReleaseThreadLock (cevent);
					Report.Debug (DebugFlags.Wait,
						      "ThreadManager {0} process pending event done: {1}",
						      DebuggerWaitHandle.CurrentThread, engine);
				} catch (ST.ThreadAbortException) {
					;
				} catch (Exception e) {
					Report.Debug (DebugFlags.Wait,
					      "ThreadManager caught exception: {0}", e);
				}
			}
		}

		void start_wait_thread ()
		{
			Report.Debug (DebugFlags.Threads, "Wait thread started: {0}",
				      DebuggerWaitHandle.CurrentThread);

			//
			// NOTE: Dispose() intentionally uses
			//          wait_thread.Abort ();
			//          wait_thread.Join ();
			//
			// The Thread.Abort() is neccessary since we may be blocked in a
			// waitpid().  In this case, the thread abort signal which is sent
			// to the current thread will make the waitpid() abort with an EINTR,
			// so we're not deadlocking here.
			//

			try {
				while (wait_thread_main ())
					;
			} catch (ST.ThreadAbortException) {
				Report.Debug (DebugFlags.Threads, "Wait thread abort: {0}",
					      DebuggerWaitHandle.CurrentThread);
				ST.Thread.ResetAbort ();
			}

			Report.Debug (DebugFlags.Threads, "Wait thread exiting: {0}",
				      DebuggerWaitHandle.CurrentThread);
		}

		bool wait_thread_main ()
		{
			Report.Debug (DebugFlags.Wait, "Wait thread sleeping");
			wait_event.WaitOne ();
			waiting = true;

			int pid, status;
			if (abort_requested) {
				Report.Debug (DebugFlags.Wait,
					      "Wait thread abort requested");

				//
				// Reap all our children.
				//

				do {
					pid = mono_debugger_server_global_wait (out status);
					Report.Debug (DebugFlags.Wait,
						      "Wait thread received event: {0} {1:x}",
						      pid, status);
				} while (pid > 0);

				return false;
			}

			Report.Debug (DebugFlags.Wait, "Wait thread waiting");

			//
			// Wait until we got an event from the target or a command from the user.
			//

			pid = mono_debugger_server_global_wait (out status);

			Report.Debug (DebugFlags.Wait,
				      "Wait thread received event: {0} {1:x}",
				      pid, status);

			//
			// Note: `pid' is basically just an unique number which identifies the
			//       SingleSteppingEngine of this event.
			//

			if (abort_requested || (pid <= 0))
				return true;

			SingleSteppingEngine event_engine = (SingleSteppingEngine) thread_hash [pid];
			if (event_engine == null) {
				Console.WriteLine ("WARNING: Got event {0:x} for unknown pid {1}",
						   status, pid);
				waiting = false;
				RequestWait ();
				return true;
			}

			engine_event.WaitOne ();

			event_queue.Lock ();
			engine_event.Reset ();

			if (current_event != null) {
				Console.WriteLine ("FUCK: {0}", Environment.StackTrace);
				throw new InternalError ();
			}

			current_event = event_engine;
			current_event_status = status;

			waiting = false;

			event_queue.Signal ();
			event_queue.Unlock ();
			return true;
		}

		private void RequestWait ()
		{
			if (waiting)
				throw new InternalError ();
			wait_event.Set ();
		}

#region IDisposable implementation
		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadManager");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				abort_requested = true;
#if FIXME
				RequestWait ();
#endif
				event_queue.Signal();
				disposed = true;
			}

			//
			// There are two situations where Dispose() can be called:
			//
			// a) It's a user-requested `kill' or `quit'.
			//
			//    In this case, the wait thread is normally blocking in waitpid()
			//    (via mono_debugger_server_global_wait ()).
			//
			//    To wake it up, the engine thread must issue a
			//    ptrace (PTRACE_KILL, inferior->pid) - note that the same restriction
			//    apply like for any other ptrace() call, so this can only be done
			//    from the engine thread.
			//
			//    To do that, we just set the `abort_requested' flag here and then
			//    join the engine thread - after it exited, we also join the wait
			//    thread so it can reap the dead child.
			//
			//    Once both threads exited, we can go ahead and dispose everything.
			//
			// b) The child exited.
			//
			//    In this case, we're invoked from the engine thread via the
			//    `ThreadExitEvent' (that's why we must not join the engine thread).
			//
			//    The child is already dead, so we just set the flag and join the
			//    wait thread [note that the wait thread is already dying at this point;
			//    it was blocking on the `wait_event', woke up and found the
			//    `abort_requested' - so we only join it to avoid a race condition].
			//
			//    After that, we can go ahead and dispose everything.
			//

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (inferior_thread == null)
					return;

				Report.Debug (DebugFlags.Wait,
					      "Thread manager dispose");

				if (ST.Thread.CurrentThread != inferior_thread)
					inferior_thread.Join ();
				wait_thread.Abort ();
				wait_thread.Join ();

				ProcessServant[] procs = new ProcessServant [processes.Count];
				processes.CopyTo (procs, 0);

				for (int i = 0; i < procs.Length; i++)
					procs [i].Dispose ();
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