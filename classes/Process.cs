using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using ST = System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;
using Mono.Debugger.Server;
using Mono.Debugger.Architectures;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using Mono.Debugger.Languages.Native;

namespace Mono.Debugger
{
	public delegate bool ExceptionCatchPointHandler (string exception, out ExceptionAction action);

	[Serializable]
	public enum ExceptionAction
	{
		None = 0,
		Stop = 1,
		StopUnhandled = 2
	}

	public sealed class Process : DebuggerMarshalByRefObject
	{
		int id = ++next_id;
		static int next_id = 0;

		public int ID {
			get { return id; }
		}

		public Thread MainThread {
			get { return MainThreadServant.Client; }
		}

		public override string ToString ()
		{
			return String.Format ("Process #{0}", ID);
		}

		public event TargetOutputHandler TargetOutputEvent;

		internal void OnTargetOutput (bool is_stderr, string output)
		{
			if (TargetOutputEvent != null)
				TargetOutputEvent (is_stderr, output);
		}

		public CommandResult ActivatePendingBreakpoints ()
		{
			if (!Session.HasPendingBreakpoints ())
				return null;

			BreakpointCommandResult result = new BreakpointCommandResult (this);
			bool completed = ActivatePendingBreakpoints_internal (result);
			if (completed)
				return null;
			return result;
		}

		class BreakpointCommandResult : CommandResult
		{
			Process process;
			ST.ManualResetEvent completed_event = new ST.ManualResetEvent (false);

			internal BreakpointCommandResult (Process process)
			{
				this.process = process;
			}

			public Process Process {
				get { return process; }
			}

			public override ST.WaitHandle CompletedEvent {
				get { return completed_event; }
			}

			internal override void Completed ()
			{
				completed_event.Set ();
			}

			public override void Abort ()
			{
				throw new NotImplementedException ();
			}
		}

		//
		// Stopping / resuming all threads for the GUI
		//

		[Obsolete]
		public GUIManager StartGUIManager ()
		{
			Session.Config.StopOnManagedSignals = false;
			return Debugger.GUIManager;
		}

		internal void OnTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			Debugger.OnTargetEvent (sse.Client, args);
		}

		internal void OnEnterNestedBreakState (SingleSteppingEngine sse)
		{
			Debugger.OnEnterNestedBreakState (sse.Client);
		}

		internal void OnLeaveNestedBreakState (SingleSteppingEngine sse)
		{
			Debugger.OnLeaveNestedBreakState (sse.Client);
		}

		ExceptionCatchPointHandler generic_exc_handler;

		public void InstallGenericExceptionCatchPoint (ExceptionCatchPointHandler handler)
		{
			this.generic_exc_handler = handler;
		}

		public bool GenericExceptionCatchPoint (string exception, out ExceptionAction action)
		{
			if (generic_exc_handler != null)
				return generic_exc_handler (exception, out action);

			action = ExceptionAction.None;
			return false;
		}

		[Obsolete("Use DebuggerSession.InsertExceptionCatchPoint().")]
		public int AddExceptionCatchPoint (ExceptionCatchPoint catchpoint)
		{
			check_disposed ();
			// Do nothing; the session now maintains exception catchpoints.
			return catchpoint.UniqueID;
		}

		[Obsolete("Use DebuggerSession.RemoveEvent().")]
		public void RemoveExceptionCatchPoint (int index)
		{
			check_disposed ();
		}

		TargetInfo target_info;
		ThreadManager manager;
		Architecture architecture;
		OperatingSystemBackend os;
		NativeLanguage native_language;
		SymbolTableManager symtab_manager;
		BreakpointManager breakpoint_manager;
		Dictionary<int,ExceptionCatchPoint> exception_handlers;
		ProcessStart start;
		DebuggerSession session;
		MonoLanguageBackend mono_language;
		ThreadServant main_thread;
		Hashtable thread_hash;

		IProcess server_process;
		MonoRuntimeManager mono_manager;

		Dictionary<string, ExecutableReader> exe_reader_by_filename;

		MyOperationHost operation_host;

		Process parent;

#if FIXME && !WINDOWS
		ThreadDB thread_db;
#endif

		bool is_attached;
		bool is_execed;
		bool is_forked;
		bool initialized;
		bool has_thread_lock;

		private Process (ThreadManager manager, DebuggerSession session)
		{
			this.manager = manager;
			this.session = session;

			operation_host = new MyOperationHost (this);

			stopped_event = new ST.ManualResetEvent (false);

			thread_hash = Hashtable.Synchronized (new Hashtable ());

			exe_reader_by_filename = new Dictionary<string, ExecutableReader> ();

			target_info = manager.TargetInfo;

			switch (manager.DebuggerServer.ArchType) {
			case ArchType.I386:
				architecture = new Architecture_I386 (this, target_info);
				break;
			case ArchType.X86_64:
				architecture = new Architecture_X86_64 (this, target_info);
				break;
			case ArchType.ARM:
				architecture = new Architecture_Arm (this, target_info);
				break;
			default:
				throw new InternalError ();
			}
		}

		internal Process (ThreadManager manager, IProcess server_process, ProcessStart start)
			: this (manager, start.Session)
		{
			this.server_process = server_process;
			this.start = start;

			is_attached = start.PID != 0;

			breakpoint_manager = new BreakpointManager (manager.DebuggerServer.BreakpointManager);

			exception_handlers = new Dictionary<int,ExceptionCatchPoint> ();

			symtab_manager = new SymbolTableManager (session);

			os = manager.CreateOperatingSystemBackend (this);
			native_language = new NativeLanguage (this, os, target_info);

			session.OnProcessCreated (this);
		}

		private Process (Process parent, int pid)
			: this (parent.manager, parent.session)
		{
			this.start = new ProcessStart (parent.ProcessStart, pid);

			this.is_forked = true;
			this.initialized = true;

			this.parent = parent;

			breakpoint_manager = parent.breakpoint_manager.Clone ();

			exception_handlers = new Dictionary<int,ExceptionCatchPoint> ();
			foreach (KeyValuePair<int,ExceptionCatchPoint> catchpoint in parent.exception_handlers)
				exception_handlers.Add (catchpoint.Key, catchpoint.Value);

			symtab_manager = parent.symtab_manager;

			native_language = parent.native_language;
			os = parent.os;
		}

		public Debugger Debugger {
			get { return manager.Debugger; }
		}

		public bool IsAttached {
			get { return is_attached; }
		}

		public bool CanDetach {
			get { return is_attached || manager.CanDetachAny; }
		}

		public bool IsExeced {
			get { return is_execed; }
		}

		public DebuggerSession Session {
			get { return session; }
		}

		internal ThreadManager ThreadManager {
			get { return manager; }
		}

		internal Architecture Architecture {
			get { return architecture; }
		}

		internal BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
		}

		internal SymbolTableManager SymbolTableManager {
			get {
				return symtab_manager;
			}
		}

		public Language NativeLanguage {
			get { return native_language; }
		}

		internal OperatingSystemBackend OperatingSystem {
			get { return os; }
		}

		internal ThreadServant MainThreadServant {
			get { return main_thread; }
		}

		internal ProcessStart ProcessStart {
			get { return start; }
		}

		internal MonoRuntimeManager MonoManager {
			get { return mono_manager; }
		}

		internal bool MonoRuntimeFound {
			get; private set;
		}

		public bool IsManaged {
			get { return mono_manager != null; }
		}

		internal bool CanExecuteCode {
			get { return (mono_manager != null) && mono_manager.CanExecuteCode; }
		}

		public string TargetApplication {
			get { return start.TargetApplication; }
		}

		public string[] CommandLineArguments {
			get { return start.CommandLineArguments; }
		}

		internal ST.WaitHandle WaitHandle {
			get { return stopped_event; }
		}

		internal IOperationHost OperationHost {
			get { return operation_host; }
		}

		internal SingleSteppingEngine ThreadCreated (IInferior server_inferior)
		{
			var sse = new SingleSteppingEngine (manager, this, server_process, server_inferior);
			OnThreadCreatedEvent (sse);

			if (IsAttached)
				sse.StartSuspended ();
			else {
				CommandResult result = current_operation != null ?
					current_operation : new ThreadCommandResult (sse.Thread);

				sse.StartThread (result);
			}

			return sse;
		}

#if FIXME
		internal void ThreadCreated (Inferior inferior, int pid, bool do_attach, bool resume_thread)
		{
			SingleSteppingEngine new_thread = new SingleSteppingEngine (manager, this, pid, do_attach);
			Inferior new_inferior = new_thread.Inferior;

			Report.Debug (DebugFlags.Threads, "Thread created: {0} {1} {2}", pid, new_thread, do_attach);

			if (mono_manager != null)
				mono_manager.ThreadCreated (new_thread);

			if (!do_attach && !is_execed)
				get_thread_info (inferior, new_thread);
			OnThreadCreatedEvent (new_thread);

			if (resume_thread) {
				CommandResult result = current_operation != null ?
					current_operation : new ThreadCommandResult (new_thread.Thread);
				new_thread.StartThread (result);
			} else {
				new_thread.StartSuspended ();
			}
		}

		internal void ChildForked (Inferior inferior, int pid)
		{
			Process new_process = new Process (this, pid);
			new_process.ProcessStart.StopInMain = false;

			SingleSteppingEngine new_thread = new SingleSteppingEngine (manager, new_process, pid);

			Inferior new_inferior = new_thread.Inferior;
			new_inferior.InitializeThread (pid);

			if (!manager.Debugger.Configuration.FollowFork) {
				new_inferior.DetachAfterFork ();
				return;
			}

			Report.Debug (DebugFlags.Threads, "Child forked: {0} {1}", pid, new_thread);

			new_process.main_thread = new_thread;

			manager.Debugger.OnProcessCreatedEvent (new_process);
			new_process.OnThreadCreatedEvent (new_thread);

			CommandResult result = new_process.CloneParentOperation (new_thread);
			new_thread.StartForkedChild (result);
		}

		internal void ChildExecd (SingleSteppingEngine engine, Inferior inferior)
		{
			is_execed = true;

			if (!is_forked) {
				if (mono_language != null)
					mono_language.Dispose();

				if (native_language != null)
					native_language.Dispose ();

				if (os != null)
					os.Dispose ();

				if (symtab_manager != null)
					symtab_manager.Dispose ();
			}

			if (breakpoint_manager != null)
				breakpoint_manager.Dispose ();

			session.OnProcessExecd (this);

			breakpoint_manager = new BreakpointManager (manager.DebuggerServer.CreateBreakpointManager ());

			exception_handlers = new Dictionary<int,ExceptionCatchPoint> ();

			symtab_manager = new SymbolTableManager (session);

			os = manager.CreateOperatingSystemBackend (this);
			native_language = new NativeLanguage (this, os, target_info);

			SingleSteppingEngine new_thread = new SingleSteppingEngine (manager, this, inferior.PID);
			Inferior new_inferior = new_thread.Inferior;

			try {
				new_inferior.InitializeAfterExec (inferior.PID);
			} catch (Exception ex) {
				if ((ex is TargetException) && (((TargetException) ex).Type == TargetError.PermissionDenied)) {
					Report.Error ("Permission denied when trying to debug exec()ed child {0}, detaching!",
						      inferior.PID);
				} else {
					Report.Error ("InitializeAfterExec() failed on pid {0}: {1}", inferior.PID, ex);
				}
				new_inferior.DetachAfterFork ();
				return;
			}

			ThreadServant[] threads;
			lock (thread_hash.SyncRoot) {
				threads = new ThreadServant [thread_hash.Count];
				thread_hash.Values.CopyTo (threads, 0);
			}

			for (int i = 0; i < threads.Length; i++) {
				if (threads [i].PID != inferior.PID)
					threads [i].Kill ();
			}

			thread_hash [inferior.PID] = new_thread;

			inferior.Dispose ();
			inferior = null;

			manager.Debugger.OnProcessExecdEvent (this);
			manager.Debugger.OnThreadCreatedEvent (new_thread.Thread);
			initialized = is_forked = false;
			main_thread = new_thread;

			if ((engine.Thread.ThreadFlags & Thread.Flags.StopOnExit) != 0)
				new_thread.Thread.ThreadFlags |= Thread.Flags.StopOnExit;

			CommandResult result = engine.OnExecd (new_thread);
			new_thread.StartExecedChild (result);
		}
#endif

		internal CommandResult StartApplication ()
		{
			IInferior server_inferior;

			if (start.PID != 0) {
				server_inferior = server_process.Attach (start.PID);
			} else {
				string[] args = new string[start.CommandLineArguments.Length + 1];
				Array.Copy (start.CommandLineArguments, args, start.CommandLineArguments.Length);
				string[] env = new string[start.Environment.Length + 1];
				Array.Copy (start.Environment, env, start.Environment.Length);

				server_inferior = server_process.Spawn (start.WorkingDirectory, args, env);
			}

			var engine = new SingleSteppingEngine (manager, this, server_process, server_inferior);
			manager.AddEngine (server_inferior, engine);

			initialized = true;

			this.main_thread = engine;
			engine.Thread.ThreadFlags |= Thread.Flags.StopOnExit;

			if (thread_hash.Contains (engine.PID))
				thread_hash [engine.PID] = engine;
			else
				thread_hash.Add (engine.PID, engine);
			session.MainThreadGroup.AddThread (engine.Thread.ID);

			session.OnMainProcessCreated (this);
			manager.Debugger.OnMainProcessCreatedEvent (this);

			if (server_process.MonoRuntime != null)
				mono_manager = new MonoRuntimeManager (this, server_process.MonoRuntime);

			if (start.PID != 0) {
				foreach (var inferior in server_process.GetAllThreads ()) {
					manager.OnThreadCreated (inferior);
				}
				foreach (var dll in server_process.GetAllModules ()) {
					manager.OnDllLoaded (this, dll);
				}
				if (mono_manager != null)
					mono_manager.InitializeAfterAttach (engine.Inferior);
				engine.StartSuspended ();
				return null;
			} else {
				CommandResult result = Debugger.StartOperation (start.Session.Config.ThreadingModel, engine);
				return engine.StartApplication (result);
			}
		}

		internal void InitializeProcess (Inferior inferior)
		{
			server_process.InitializeProcess (inferior.InferiorHandle);
		}

		internal void OnDllLoaded (ExecutableReader reader)
		{
			lock (this) {
				exe_reader_by_filename.Add (reader.FileName, reader);
			}
		}

		internal ExecutableReader LookupLibrary (TargetAddress address)
		{
			lock (this) {
				foreach (ExecutableReader reader in exe_reader_by_filename.Values) {
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
				foreach (ExecutableReader reader in exe_reader_by_filename.Values) {
					TargetAddress symbol = reader.LookupSymbol (name);
					if (!symbol.IsNull)
						return symbol;
				}

				return TargetAddress.Null;
			}
		}

		internal void OnProcessExitedEvent ()
		{
			DropGlobalThreadLock ();

			if (current_state == ProcessState.Running) {
				current_state = ProcessState.Exited;
				current_operation.Completed ();
				current_operation = null;
				stopped_event.Set ();
			}

			if (!is_forked)
				session.OnProcessExited (this);
			session.MainThreadGroup.RemoveThread (main_thread.ID);
			manager.Debugger.OnProcessExitedEvent (this);
		}

		void OnThreadCreatedEvent (ThreadServant thread)
		{
			thread_hash.Add (thread.PID, thread);
			manager.Debugger.OnThreadCreatedEvent (thread.Client);
		}

		internal void OnManagedThreadExitedEvent (ThreadServant thread)
		{
			thread_hash.Remove (thread.PID);
		}

		internal void OnProcessReachedMainEvent ()
		{
			manager.Debugger.OnProcessReachedMainEvent (this);
		}

		internal void OnThreadExitedEvent (ThreadServant thread)
		{
			thread_hash.Remove (thread.PID);
			thread.ThreadGroup.RemoveThread (thread.ID);
			session.DeleteThreadGroup (thread.ThreadGroup.Name);
			manager.Debugger.OnThreadExitedEvent (thread.Client);

			if (thread_hash.Count == 0)
				OnProcessExitedEvent ();
		}

		internal void InitializeMono (IMonoRuntime handle)
		{
			mono_manager = new MonoRuntimeManager (this, handle);
		}

		internal void InitializeThreads (Inferior inferior, bool resume_threads)
		{
#if FIXME && !WINDOWS
			if (thread_db != null)
				return;

			thread_db = ThreadDB.Create (this, inferior);
			if (thread_db == null) {
				if (!IsManaged)
					return;

				Report.Error ("Failed to initialize thread_db on {0}", start.CommandLine);
				throw new TargetException (TargetError.CannotStartTarget,
							   "Failed to initialize thread_db on {0}", start.CommandLine);
			}

			int[] threads = inferior.GetThreads ();
			foreach (int thread in threads) {
				if (thread_hash.Contains (thread))
					continue;
				ThreadCreated (inferior, thread, manager.HasThreadEvents, resume_threads);
			}

			thread_db.GetThreadInfo (inferior, delegate (int lwp, long tid) {
				SingleSteppingEngine engine = (SingleSteppingEngine) thread_hash [lwp];
				if (engine == null) {
					Report.Error ("Unknown thread {0} in {1}", lwp, start.CommandLine);
					return;
				}
				engine.SetTID (tid);
			});
#endif
		}

		internal bool CheckForThreads (ArrayList check_threads)
		{
#if FIXME && !WINDOWS
			if(thread_db == null)
				return false;
			thread_db.GetThreadInfo (null, delegate (int lwp, long tid) {
				check_threads.Add(lwp);
			} );
			return true;
#else
			return false;
#endif
		}

		void get_thread_info (Inferior inferior, SingleSteppingEngine engine)
		{
#if FIMXE
			if (thread_db == null) {
				if (mono_manager == null)
					return;
				Report.Error ("Failed to initialize thread_db on {0}: {1} {2}",
					      start.CommandLine, start, Environment.StackTrace);
				throw new InternalError ();
			}

			bool found = false;
			thread_db.GetThreadInfo (inferior, delegate (int lwp, long tid) {
				if (lwp != engine.PID)
					return;

				engine.SetTID (tid);
				found = true;
			});

			if (!found)
				Report.Error ("Cannot find thread {0:x} in {1}",
					      engine.PID, start.CommandLine);
#endif
		}

		internal SingleSteppingEngine GetEngineByTID (Inferior inferior, long tid)
		{
			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				if (engine.TID == tid)
					return engine;
			}

#if FIXME && !WINDOWS
			if (thread_db == null) {
				Report.Error ("Failed to initialize thread_db on {0}: {1}",
					      start.CommandLine, start);
				return null;
			}

			SingleSteppingEngine result = null;
			thread_db.GetThreadInfo (inferior, delegate (int t_lwp, long t_tid) {
				if (tid != t_tid)
					return;
				result = (SingleSteppingEngine) thread_hash [t_lwp];

			});

			if (result == null)
				Report.Error ("Cannot find thread {0:x} in {1}",
					      tid, start.CommandLine);

			return result;
#else
			Report.Error ("Cannot find thread {0:x} in {1}",
				      tid, start.CommandLine);
			return null;
#endif
		}

		public void Kill ()
		{
			if (!manager.HasThreadEvents) {
				SingleSteppingEngine[] sses = new SingleSteppingEngine [thread_hash.Count];
				thread_hash.Values.CopyTo (sses, 0);
				foreach (SingleSteppingEngine sse in sses)
					sse.SetKilledFlag ();
				foreach (SingleSteppingEngine sse in sses)
					sse.Kill ();
			} else {
				main_thread.Kill ();
			}
		}

		public void Detach ()
		{
			if (!CanDetach)
				throw new TargetException (TargetError.CannotDetach);

			main_thread.Detach ();
		}

		internal void OnTargetDetached ()
		{
			OnProcessExitedEvent ();
			Dispose ();
		}

		internal MonoLanguageBackend CreateMonoLanguage (MonoDebuggerInfo info)
		{
			mono_language = new MonoLanguageBackend (this, info);
			return mono_language;
		}

		internal void UpdateSymbolTable (TargetMemoryAccess target)
		{
			if (mono_language != null)
				mono_language.Update (target);
		}

		public void LoadLibrary (Thread thread, string filename)
		{
			if (mono_language == null)
				throw new SymbolTableException (
						"Cannot load .NET assembly `{0}' while " +
						"debugging an unmanaged application.",
						filename);

			if (!mono_language.TryFindImage (thread, filename))
				throw new SymbolTableException ("Could not find any .NET assembly `{0}'.", filename);
		}

		internal MonoLanguageBackend MonoLanguage {
			get {
				if (mono_language == null)
					throw new InvalidOperationException ();

				return mono_language;
			}
		}

		internal bool IsManagedApplication {
			get { return mono_language != null; }
		}

		public Module[] Modules {
			get { return session.Modules; }
		}

		public SourceLocation FindLocation (string file, int line)
		{
			foreach (Module module in Modules) {
				SourceLocation location = module.FindLocation (file, line);

				if (location != null)
					return location;
			}

			return null;
		}

		public SourceLocation FindMethod (string name)
		{
			foreach (Module module in Modules) {
				MethodSource method = module.FindMethod (name);

				if (method != null)
					return new SourceLocation (method);
			}

			return null;
		}

		internal ThreadServant[] ThreadServants {
			get {
				lock (thread_hash.SyncRoot) {
					int count = thread_hash.Count;
					ThreadServant[] servants = new ThreadServant [count];
					thread_hash.Values.CopyTo (servants, 0);
					return servants;
				}
			}
		}

		internal SingleSteppingEngine[] Engines {
			get {
				lock (thread_hash.SyncRoot) {
					int count = thread_hash.Count;
					SingleSteppingEngine[] engines = new SingleSteppingEngine [count];
					thread_hash.Values.CopyTo (engines, 0);
					return engines;
				}
			}
		}

		public Thread[] GetThreads ()
		{
			lock (thread_hash.SyncRoot) {
				int count = thread_hash.Count;
				Thread[] threads = new Thread [count];
				ThreadServant[] servants = new ThreadServant [count];
				thread_hash.Values.CopyTo (servants, 0);
				for (int i = 0; i < count; i++)
					threads [i] = servants [i].Client;
				return threads;
			}
		}

		internal bool HasThreadLock {
			get { return has_thread_lock; }
		}

		// <summary>
		//   Stop all currently running threads without sending any notifications.
		//   The threads are automatically resumed to their previos state when
		//   ReleaseGlobalThreadLock() is called.
		// </summary>
		internal void AcquireGlobalThreadLock (SingleSteppingEngine caller)
		{
			lock (this) {
				if (has_thread_lock)
					throw new InternalError ("Recursive thread lock");

				Report.Debug (DebugFlags.Threads,
					      "Acquiring global thread lock: {0}", caller);

				server_process.Suspend (caller.Inferior.InferiorHandle);

				has_thread_lock = true;

				Report.Debug (DebugFlags.Threads,
					      "Done acquiring global thread lock: {0}",
					      caller);
			}
		}

		internal void ReleaseGlobalThreadLock (SingleSteppingEngine caller)
		{
			lock (this) {
				if (!has_thread_lock)
					throw new InternalError ("Not thread-locked");

				Report.Debug (DebugFlags.Threads,
					      "Releasing global thread lock: {0}", caller);

				server_process.Resume (caller.Inferior.InferiorHandle);

				has_thread_lock = false;

				Report.Debug (DebugFlags.Threads,
					      "Released global thread lock: {0}", caller);
			}
		}

		internal void DropGlobalThreadLock ()
		{
			lock (this) {
				if (thread_hash.Count != 0)
					throw new InternalError ();

				has_thread_lock = false;
			}
		}

#region User Threads

		internal enum ProcessState
		{
			SingleThreaded,
			Running,
			Stopping,
			Stopped,
			Exited
		}

		ST.ManualResetEvent stopped_event;

		ProcessState current_state = ProcessState.Stopped;
		OperationCommandResult current_operation = null;

		internal void OperationCompleted (SingleSteppingEngine caller, TargetEventArgs result, ThreadingModel model)
		{
			if (!ThreadManager.InBackgroundThread && manager.HasThreadEvents)
				throw new InternalError ();

			if (current_state == ProcessState.Stopping)
				return;
			else if (current_state != ProcessState.Running)
				throw new InternalError ();

			if ((result != null) && (caller != main_thread) &&
			    ((result.Type == TargetEventType.TargetExited) || (result.Type == TargetEventType.TargetSignaled)))
				return;

			current_state = ProcessState.Stopping;
			SuspendUserThreads (model, caller);

			lock (this) {
				current_state = ProcessState.Stopped;
				current_operation.Completed ();
				current_operation = null;
				stopped_event.Set ();
			}
		}

		void SuspendUserThreads (ThreadingModel model, SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Suspending user threads: {0} {1}", model, caller);

			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				Report.Debug (DebugFlags.Threads, "  check user thread: {0} {1}",
					      engine, engine.Thread.ThreadFlags);
				if (engine == caller)
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Immutable) != 0) &&
				    ((model & ThreadingModel.StopImmutableThreads) == 0))
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Daemon) != 0) &&
				    ((model & ThreadingModel.StopDaemonThreads) == 0))
					continue;
				engine.SuspendUserThread ();
			}

			Report.Debug (DebugFlags.Threads,
				      "Done suspending user threads: {0} {1}", model, caller);
		}

		void ResumeUserThreads (ThreadingModel model, SingleSteppingEngine caller)
		{
			Report.Debug (DebugFlags.Threads,
				      "Resuming user threads: {0}", caller);

			foreach (SingleSteppingEngine engine in thread_hash.Values) {
				if (engine == caller)
					continue;
				if ((engine.Thread.ThreadFlags & Thread.Flags.AutoRun) == 0)
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Immutable) != 0) &&
				    ((model & ThreadingModel.StopImmutableThreads) == 0))
					continue;
				if (((engine.Thread.ThreadFlags & Thread.Flags.Daemon) != 0) &&
				    ((model & ThreadingModel.StopDaemonThreads) == 0))
					continue;

				CommandResult result;
				if (current_operation != null)
					result = current_operation;
				else
					result = new ThreadCommandResult (engine.Thread);

				engine.ResumeUserThread (result);
			}

			Report.Debug (DebugFlags.Threads,
				      "Resumed user threads: {0}", caller);
		}

		internal CommandResult StartOperation (ThreadingModel model, SingleSteppingEngine caller)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			if ((current_state != ProcessState.Stopped) && (current_state != ProcessState.SingleThreaded))
				throw new TargetException (TargetError.NotStopped);

			if ((model & ThreadingModel.ThreadingMode) == ThreadingModel.Single) {
				current_state = ProcessState.SingleThreaded;
				if ((model & ThreadingModel.ResumeThreads) != 0)
					ResumeUserThreads (model, caller);
				return new ThreadCommandResult (caller.Thread);
			} else if ((model & ThreadingModel.ThreadingMode) != ThreadingModel.Process) {
				throw new ArgumentException ();
			}

			lock (this) {
				current_state = ProcessState.Running;
				stopped_event.Reset ();
				current_operation = new ProcessCommandResult (this, model);
			}

			ResumeUserThreads (model, caller);
			return current_operation;
		}

		internal void StartGlobalOperation (ThreadingModel model, SingleSteppingEngine caller, OperationCommandResult operation)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			if ((current_state != ProcessState.Stopped) && (current_state != ProcessState.SingleThreaded))
				throw new TargetException (TargetError.NotStopped);

			lock (this) {
				current_state = ProcessState.Running;
				stopped_event.Reset ();
				current_operation = operation;
			}

			ResumeUserThreads (model, caller);
		}

		CommandResult CloneParentOperation (SingleSteppingEngine new_thread)
		{
			if (parent.current_state == ProcessState.SingleThreaded) {
				current_state = ProcessState.SingleThreaded;
				return new ThreadCommandResult (new_thread.Thread);
			}

			if (parent.current_state != ProcessState.Running)
				throw new InternalError ();

			current_state = ProcessState.Running;
			if ((parent.current_operation.ThreadingModel & ThreadingModel.ThreadingMode) == ThreadingModel.Global)
				current_operation = parent.current_operation;
			else if ((parent.current_operation.ThreadingModel & ThreadingModel.ThreadingMode) == ThreadingModel.Process)
				current_operation = new ProcessCommandResult (this, parent.current_operation.ThreadingModel);
			else
				throw new InternalError ();

			return current_operation;
		}

		internal void Stop ()
		{
			main_thread.Invoke (delegate {
				current_state = ProcessState.Stopping;

				SuspendUserThreads (ThreadingModel.Process, null);
				current_state = ProcessState.Stopped;
				if (current_operation != null) {
					current_operation.Completed ();
					current_operation = null;
				}
				stopped_event.Set ();
				return null;
			}, null);
		}

		class MyOperationHost : IOperationHost
		{
			public Process Process;

			public MyOperationHost (Process process)
			{
				this.Process = process;
			}

			void IOperationHost.OperationCompleted (SingleSteppingEngine caller, TargetEventArgs result, ThreadingModel model)
			{
				Process.OperationCompleted (caller, result, model);
			}

			ST.WaitHandle IOperationHost.WaitHandle {
				get { return Process.stopped_event; }
			}

			void IOperationHost.SendResult (SingleSteppingEngine sse, TargetEventArgs args)
			{
				Process.Debugger.OnTargetEvent (sse.Client, args);
			}

			void IOperationHost.Abort ()
			{
				Process.Stop ();
			}
		}

		class ProcessCommandResult : OperationCommandResult
		{
			public Process Process {
				get; private set;
			}

			internal override IOperationHost Host {
				get { return Process.OperationHost; }
			}

			internal ProcessCommandResult (Process process, ThreadingModel model)
				: base (model)
			{
				this.Process = process;
			}

			internal override void OnExecd (SingleSteppingEngine new_thread)
			{
				Process = new_thread.Process;
			}
		}

#endregion

		internal bool ActivatePendingBreakpoints_internal (CommandResult result)
		{
			return ((SingleSteppingEngine) main_thread).ManagedCallback (
				delegate (SingleSteppingEngine sse) {
					return sse.ActivatePendingBreakpoints (null);
				}, result);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		void DoDispose ()
		{
			if (!is_forked) {
				if (architecture != null) {
					architecture.Dispose ();
					architecture = null;
				}

				if (mono_language != null) {
					mono_language.Dispose();
					mono_language = null;
				}

				if (native_language != null) {
					native_language.Dispose ();
					native_language = null;
				}

				if (os != null) {
					os.Dispose ();
					os = null;
				}

				if (symtab_manager != null) {
					symtab_manager.Dispose ();
					symtab_manager = null;
				}
			}

			if (breakpoint_manager != null) {
				breakpoint_manager.Dispose ();
				breakpoint_manager = null;
			}

#if FIXME && !WINDOWS
			if (thread_db != null) {
				thread_db.Dispose ();
				thread_db = null;
			}
#endif

			exception_handlers = null;
		}

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

		~Process ()
		{
			Dispose (false);
		}
	}
}
