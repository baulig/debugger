using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Remoting;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger;
using Mono.Debugger.Languages;
using EE=Mono.Debugger.ExpressionEvaluator;

namespace Mono.Debugger.Frontend
{
	public class Interpreter : DebuggerMarshalByRefObject, IInterruptionHandler, IDisposable
	{
		DebuggerConfiguration config;
		DebuggerOptions options;

		DebuggerSession session;
		DebuggerEngine engine;

		SourceFileFactory source_factory;

		Debugger debugger;
		Process main_process;
		Process current_process;

		Hashtable styles;
		StyleBase current_style;

		bool is_interactive;
		bool is_script;
		int exit_code = 0;
		int interrupt_level;

		ExpressionParser parser;
		ManualResetEvent interrupt_event;
		ManualResetEvent process_event;
		Thread current_thread;

		internal static readonly string DirectorySeparatorStr;
		
		static Interpreter ()
		{
			// FIXME: Why isn't this public in System.IO.Path ?
			DirectorySeparatorStr = Path.DirectorySeparatorChar.ToString ();
		}

		public Interpreter (bool is_interactive, DebuggerConfiguration config,
				    DebuggerOptions options)
		{
			this.config = config;
			this.options = options;
			this.is_interactive = is_interactive;
			this.is_script = options.IsScript;
			this.parser = new ExpressionParser (this);
			this.session = new DebuggerSession (config, options, "main", parser);
			this.engine = new DebuggerEngine (this);

			parser.Session = session;

			source_factory = new SourceFileFactory ();

			interrupt_event = new ManualResetEvent (false);
			process_event = new ManualResetEvent (false);

			styles = new Hashtable ();
			styles.Add ("cli", new StyleCLI (this));
			styles.Add ("emacs", new StyleEmacs (this));
			current_style = (StyleBase) styles ["cli"];
		}

		public void Exit ()
		{
			try {
				Dispose ();
			} catch (Exception ex) {
				Console.WriteLine (ex);
			} finally {
				Environment.Exit (exit_code);
			}
		}

		public StyleBase Style {
			get { return current_style; }
			set {
				current_style = value;
				current_style.Reset ();
			}
		}

		public DebuggerConfiguration DebuggerConfiguration {
			get { return config; }
		}

		public DebuggerEngine DebuggerEngine {
			get { return engine; }
		}

		public SourceFileFactory SourceFileFactory {
			get { return source_factory; }
		}

		public Process CurrentProcess {
			get {
				if (current_process == null)
					throw new TargetException (TargetError.NoTarget);

				return current_process;
			}

			set { current_process = value; }
		}

		public bool HasCurrentProcess {
			get { return current_process != null; }
		}

		public DebuggerSession Session {
			get {
				if (session == null)
					throw new TargetException (TargetError.NoTarget);

				return session;
			}
		}

		public StyleBase GetStyle (string name)
		{
			StyleBase style = (StyleBase) styles [name];
			if (style == null)
				throw new ScriptingException (
					"No such user interface: `{0}'", name);

			return style;
		}

		public string[] GetStyleNames ()
		{
			string[] names = new string[styles.Keys.Count];
			styles.Keys.CopyTo (names, 0);

			return names;
		}

		public ExpressionParser ExpressionParser {
			get { return parser; }
		}

		public bool IsInteractive {
			get { return is_interactive; }
			set { is_interactive = value; }
		}

		public bool IsScript {
			get { return is_script; }
			set { is_script = value; }
		}

		public DebuggerOptions Options {
			get { return session.Options; }
		}

		public int ExitCode {
			get { return exit_code; }
			set { exit_code = value; }
		}

		public void Abort ()
		{
			Print ("Caught fatal error while running non-interactively; exiting!");
			Environment.Exit (-1);
		}

		public void Error (string message)
		{
			Report.Error ("ERROR: {0}", message);
		}

		public void Error (string format, params object[] args)
		{
			Error (String.Format (format, args));
		}

		public void Error (ScriptingException ex)
		{
			Error (ex.Message);
		}

		public void Error (TargetException ex)
		{
			Error (ex.Message);
		}

		public virtual void Print (string message)
		{
			Report.Print ("{0}\n", message);
		}

		public void Print (string format, params object[] args)
		{
			Print (String.Format (format, args));
		}

		public void Print (object obj)
		{
			Print (obj.ToString ());
		}

		public bool Query (string prompt) {

			Report.Print (prompt);
			Report.Print (" (y or n) ");
	    
			string result = Report.ReadLine ();
			return (result == "y") || (result == "yes");
		}

		public void PrintInstruction (AssemblerLine line)
		{
			if (line.Label != null)
				Print ("{0}:", line.Label);
			Print ("{0:11x}\t{1}", line.Address, line.Text);
		}

		public TargetFunctionType QueryMethod (TargetFunctionType[] methods)
		{
			Report.Print ("More than one method matches your query:\n");

			ArrayList list = new ArrayList ();

			foreach (TargetFunctionType method in methods) {
				list.Add (method);
				Report.Print ("{0,4}  {1}\n", list.Count, method.Name);
			}

			Report.Print ("Select a method or 0 to abort: ");
			string result = Report.ReadLine ();

			uint index;
			try {
				index = UInt32.Parse (result);
			} catch {
				Report.Print ("Invalid number.");
				return null;
			}

			if (index == 0)
				return null;

			if (index > list.Count) {
				Report.Print ("No such method.");
				return null;
			}

			return (TargetFunctionType) list [(int) index];
		}

		public bool HasTarget {
			get {
				return (debugger != null) && debugger.HasTarget;
			}
		}

		public Process Start ()
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			if (!IsScript)
				Print ("Starting program: {0} {1}", Options.File,
				       String.Join (" ", Options.InferiorArgs));

			try {
				debugger = new Debugger (config, options);

				new InterpreterEventSink (this, debugger);

				CommandResult result;
				current_process = main_process = debugger.Run (session, out result);
				current_thread = current_process.MainThread;

				Wait (result);

				return current_process;
			} catch (TargetException) {
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

		public Process Attach (int pid)
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			if (!IsScript)
				Print ("Attaching to {0}", pid);

			try {
				debugger = new Debugger (config, options);

				new InterpreterEventSink (this, debugger);

				CommandResult result;
				current_process = main_process = debugger.Attach (session, pid, out result);
				current_thread = current_process.MainThread;

				Wait (result);

				return current_process;
			} catch (TargetException) {
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

#if DISABLED
		public Process OpenCoreFile (string core_file)
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			Console.WriteLine ("Loading core file {0}", core_file);

			try {
				debugger = new Debugger (config);

				new InterpreterEventSink (this, debugger);

				Thread[] threads;
				current_process = main_process = debugger.OpenCoreFile (
					session, core_file, out threads);

				current_thread = current_process.MainThread;

				return current_process;
			} catch (TargetException) {
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}
#endif

		public void SaveSession (Stream stream)
		{
			session.SaveSession (stream);
		}

		public Process LoadSession (Stream stream)
		{
			if ((debugger != null) || (main_process != null))
				throw new TargetException (TargetError.AlreadyHaveTarget);

			try {
				debugger = new Debugger (config, options);
				parser = new ExpressionParser (this);
				session = new DebuggerSession (config, stream, parser);
				parser.Session = session;

				new InterpreterEventSink (this, debugger);

				CommandResult result;
				current_process = main_process = debugger.Run (session, out result);
				current_thread = current_process.MainThread;

				Wait (result);

				return current_process;
			} catch (TargetException ex) {
				Console.WriteLine ("Got a TargetException during LoadSession: {0}", ex);
				debugger.Dispose ();
				debugger = null;
				throw;
			} catch (Exception ex) {
				Console.WriteLine ("Got an Exception during LoadSession: {0}", ex);
				debugger.Dispose ();
				debugger = null;
				throw;
			}
		}

		public bool Wait (CommandResult result)
		{
			if (result == null)
				return true;

			ClearInterrupt ();
			WaitHandle[] handles = new WaitHandle [2];
			handles [0] = interrupt_event;
			handles [1] = result.CompletedEvent;

			int ret = WaitHandle.WaitAny (handles);

			if (ret == 0) {
				result.Abort ();
				result.CompletedEvent.WaitOne ();
				return false;
			}

			if (result.Result is Exception)
				throw (Exception) result.Result;

			return true;
		}

		public int Interrupt ()
		{
			interrupt_event.Set ();
			return ++interrupt_level;
		}

		public void ClearInterrupt ()
		{
			interrupt_level = 0;
			interrupt_event.Reset ();
		}

		WaitHandle IInterruptionHandler.InterruptionEvent {
			get { return interrupt_event; }
		}

		bool IInterruptionHandler.CheckInterruption ()
		{
			return interrupt_level > 0;
		}

		public void ShowBreakpoints ()
		{
			Event[] events = Session.Events;
			if (events.Length == 0) {
				Print ("No breakpoints or catchpoints.");
				return;
			}
				       
			Print ("Breakpoints:");
			Print ("{0,3} {1,6} {2,3} {3,4} {4,12}  {5}", "Id", "Type", "En", "Act", "ThreadGroup", "What");
			foreach (Event handle in events) {
				string type;

				if (handle is ExceptionCatchPoint)
					type = "catch";
				else
					type = "break";

				Print ("{0,3} {1,6} {2,3} {3,4} {4,12}  {5}",
				       handle.Index, type,
				       handle.IsEnabled ? "y" : "n",
				       handle.IsActivated ? "y" : "n",
				       handle.ThreadGroup != null ? handle.ThreadGroup.Name : "global",
				       handle.Name);
			}
		}

		public Event GetEvent (int index)
		{
			Event handle = Session.GetEvent (index);
			if (handle == null)
				throw new ScriptingException ("No such breakpoint/catchpoint.");

			return handle;
		}

		protected virtual void OnThreadCreated (Thread thread)
		{
			process_event.Set ();
			if ((DebuggerConfiguration.UserNotifications & DebuggerConfiguration.UserNotificationType.Threads) != 0)
				Print ("Process #{0} created new thread @{1}.",
				       thread.Process.ID, thread.ID);
		}

		protected virtual void OnThreadExited (Thread thread)
		{
			if ((DebuggerConfiguration.UserNotifications & DebuggerConfiguration.UserNotificationType.Threads) != 0)
				Print ("Thread @{0} exited.", thread.ID);
			if (thread == current_thread)
				current_thread = null;
		}

		protected virtual void OnMainProcessCreated (Process process)
		{
			new ProcessEventSink (this, process);
		}

		protected virtual void OnProcessReachedMain (Process process)
		{ }

		protected virtual void OnProcessCreated (Process process)
		{
			process_event.Set ();
			new ProcessEventSink (this, process);
			Print ("Created new process #{0}.", process.ID);
			if (current_process == null) {
				current_process = process;
				current_thread = process.MainThread;
			}
		}

		protected virtual void OnProcessExited (Process process)
		{
			Print ("Process #{0} exited.", process.ID);
			if (process == main_process) {
				current_process = main_process = null;
				current_thread = null;
			} else if (process == current_process) {
				current_process = main_process;
				current_thread = main_process.MainThread;
			}
		}

		protected virtual void OnProcessExecd (Process process)
		{
			Print ("Process #{0} exec()'d: {1}", process.ID,
			       PrintCommandLineArgs (process));
		}

		protected virtual void OnTargetEvent (Thread thread, TargetEventArgs args)
		{
			if ((args.Type != TargetEventType.TargetSignaled) &&
			    (args.Type != TargetEventType.TargetExited) &&
			    (args.Type != TargetEventType.TargetInterrupted))
				CurrentThread = thread;
			Style.TargetEvent (thread, args);
		}

		protected virtual void OnTargetOutput (bool is_stderr, string line)
		{
			if (!IsScript) {
				if (is_stderr)
					Report.Print ("{0}", line);
				else
					Report.Print ("{0}", line);
			}
		}

		protected virtual void OnTargetExited ()
		{
			debugger = null;
			main_process = current_process = null;
			current_thread = null;

			Print ("Target exited.");
		}

		public Thread CurrentThread {
			get {
				if (current_thread == null)
					throw new TargetException (TargetError.NoTarget);

				return current_thread;
			}

			set {
				current_thread = value;
				current_process = value.Process;
			}
		}

		public bool HasCurrentThread {
			get { return current_thread != null; }
		}

		public Process GetProcess (int number)
		{
			if (number == -1)
				return CurrentProcess;

			foreach (Process process in Processes) {
				if (process.ID == number)
					return process;
			}

			throw new ScriptingException ("No such process: {0}", number);
		}

		public Thread GetThread (int number)
		{
			if (number == -1)
				return CurrentThread;

			foreach (Process process in Processes) {
				foreach (Thread thread in process.GetThreads ())
					if (thread.ID == number)
						return thread;
			}

			throw new ScriptingException ("No such thread: {0}", number);
		}

		public Thread[] GetThreads (int[] indices)
		{
			Thread[] retval = new Thread [indices.Length];

			for (int i = 0; i < indices.Length; i++)
				retval [i] = GetThread (indices [i]);

			return retval;
		}

		public void ShowThreadGroups ()
		{
			foreach (ThreadGroup group in Session.ThreadGroups) {
				if (group.Name.StartsWith ("@"))
					continue;
				StringBuilder ids = new StringBuilder ();
				foreach (int thread in group.Threads) {
					ids.Append (" @");
					ids.Append (thread);
				}
				Print ("{0}:{1}", group.Name, ids.ToString ());
			}
		}

		public void CreateThreadGroup (string name)
		{
			if (Session.ThreadGroupExists (name))
				throw new ScriptingException ("A thread group with that name already exists.");

			Session.CreateThreadGroup (name);
		}

		public void DeleteThreadGroup (string name)
		{
			if (!Session.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			Session.DeleteThreadGroup (name);
		}

		public ThreadGroup GetThreadGroup (string name, bool writable)
		{
			if (name == null)
				name = "main";
			else if (name == "global")
				return ThreadGroup.Global;
			if (name.StartsWith ("@"))
				throw new ScriptingException ("No such thread group.");
			if (!Session.ThreadGroupExists (name))
				throw new ScriptingException ("No such thread group.");

			ThreadGroup group = Session.CreateThreadGroup (name);

			if (writable && group.IsSystem)
				throw new ScriptingException ("Cannot modify system-created thread group.");

			return group;
		}

		public void AddToThreadGroup (string name, Thread[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);

			foreach (Thread thread in threads)
				group.AddThread (thread.ID);
		}

		public void RemoveFromThreadGroup (string name, Thread[] threads)
		{
			ThreadGroup group = GetThreadGroup (name, true);
	
			foreach (Thread thread in threads)
				group.RemoveThread (thread.ID);
		}

		public int InsertExceptionCatchPoint (Thread target, ThreadGroup group,
						      TargetType exception, bool unhandled)
		{
			Event handle = target.Process.Session.InsertExceptionCatchPoint (
				target, group, exception, unhandled);
			return handle.Index;
		}

		public int InsertHardwareWatchPoint (Thread target, TargetAddress address)
		{
			Event handle = target.Process.Session.InsertHardwareWatchPoint (
				target, address, HardwareWatchType.WatchWrite);
			return handle.Index;
		}

		public void Kill ()
		{
			if (debugger != null) {
				debugger.Kill ();
				debugger = null;
			}
		}

		public void Kill (Process process)
		{
			if (process == main_process)
				Kill ();
			else
				process.Kill ();
		}

		public void Detach ()
		{
			debugger.Detach ();
		}

		public void Detach (Process process)
		{
			if (process == main_process)
				Detach ();
			else
				process.Detach ();
		}

		public Module[] GetModules (int[] indices)
		{
			List<Module> retval = new List<Module> ();
			Module[] modules = CurrentProcess.Modules;

			foreach (int index in indices) {
				bool found = false;
				for (int i = 0; i < modules.Length; i++) {
					if (modules [i].ID == index) {
						retval.Add (modules [i]);
						found = true;
						break;
					}
				}

				if (!found)
					throw new ScriptingException ("No such module {0}.", index);
			}

			return retval.ToArray ();
		}

		public SourceFile[] GetSources (int[] indices)
		{
			Hashtable source_hash = new Hashtable ();
			Module[] modules = CurrentProcess.Modules;

			foreach (Module module in modules) {
				if (!module.SymbolsLoaded)
					continue;

				foreach (SourceFile source in module.Sources)
					source_hash.Add (source.ID, source);
			}

			int pos = 0;
			SourceFile[] retval = new SourceFile [indices.Length];

			foreach (int index in indices) {
				SourceFile source = (SourceFile) source_hash [index];
				if (source == null)
					throw new ScriptingException (
						"No such source file: {0}", index);

				retval [pos++] = source;
			}

			return retval;
		}

		public SourceBuffer ReadFile (string filename)
		{
			return source_factory.FindFile (filename);
		}

		public void ModuleOperations (Module[] modules, ModuleOperation[] operations)
		{
			foreach (Module module in modules) {
				foreach (ModuleOperation operation in operations) {
					switch (operation) {
					case ModuleOperation.Ignore:
						module.LoadSymbols = false;
						break;
					case ModuleOperation.UnIgnore:
						module.LoadSymbols = true;
						break;
					case ModuleOperation.Step:
						module.StepInto = true;
						break;
					case ModuleOperation.DontStep:
						module.StepInto = false;
						break;
					default:
						throw new InternalError ();
					}
				}
			}
		}

		public Process[] Processes {
			get {
				if (debugger == null)
					throw new TargetException (TargetError.NoTarget);

				return debugger.Processes;
			}
		}

		public string PrintCommandLineArgs (Process process)
		{
			StringBuilder sb = new StringBuilder ();
			string[] args = process.CommandLineArguments;
			int start = 0;
			if ((args.Length > 1) && (args [0] == BuildInfo.mono)) {
				if (args [1] == "--inside-mdb")
					start = 2;
				else
					start = 1;
			}
			for (int i = start; i < args.Length; i++) {
				if (i > start)
					sb.Append (" ");
				sb.Append (args [i]);
			}
			return sb.ToString ();
		}

		public string PrintProcess (Process process)
		{
			string command_line = PrintCommandLineArgs (process);
			if (command_line.Length > 70) {
				command_line = command_line.Substring (0, 70);
				command_line += " ...";
			}
			return String.Format ("#{0} ({1}:{2})", process.ID,
					      process.MainThread.PID, command_line);
		}

		public void ShowDisplays (StackFrame frame)
		{
			ScriptingContext context = new ScriptingContext (this);
			context.CurrentFrame = frame;

			foreach (Display d in Session.Displays)
				context.ShowDisplay (d);
		}

		internal CommandLineInterpreter CLI {
			get; set;
		}

		protected virtual void OnEnterNestedBreakState (Thread thread)
		{
			if (CLI != null)
				CLI.EnterNestedBreakState ();
		}

		protected void OnLeaveNestedBreakState (Thread thread)
		{
			if (CLI != null)
				CLI.LeaveNestedBreakState ();
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (debugger != null) {
					debugger.Kill ();
					debugger = null;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Interpreter ()
		{
			Dispose (false);
		}

		protected class InterpreterEventSink : DebuggerMarshalByRefObject
		{
			Interpreter interpreter;

			public InterpreterEventSink (Interpreter interpreter, Debugger debugger)
			{
				this.interpreter = interpreter;

				debugger.TargetExitedEvent += target_exited;
				debugger.ThreadCreatedEvent += thread_created;
				debugger.ManagedThreadCreatedEvent += managed_thread_created;
				debugger.ThreadExitedEvent += thread_exited;
				debugger.MainProcessCreatedEvent += main_process_created;
				debugger.ProcessReachedMainEvent += process_reached_main;
				debugger.ProcessCreatedEvent += process_created;
				debugger.ProcessExitedEvent += process_exited;
				debugger.ProcessExecdEvent += process_execd;
				debugger.TargetEvent += target_event;
				debugger.EnterNestedBreakStateEvent +=
					delegate (Debugger unused, Thread thread) {
						interpreter.OnEnterNestedBreakState (thread);
					};
				debugger.LeaveNestedBreakStateEvent +=
					delegate (Debugger unused, Thread thread) {
						interpreter.OnLeaveNestedBreakState (thread);
					};
			}

			public void target_event (Thread thread, TargetEventArgs args)
			{
				if (((thread.ThreadFlags & Thread.Flags.Daemon) != 0) &&
				    ((args.Type == TargetEventType.TargetExited) ||
				     (args.Type == TargetEventType.TargetSignaled)))
					return;
				interpreter.OnTargetEvent (thread, args);
			}

			public void thread_created (Debugger debugger, Thread thread)
			{
				if (!thread.Process.IsManaged ||
				    ((thread.ThreadFlags & Thread.Flags.Daemon) == 0))
					interpreter.OnThreadCreated (thread);
			}

			public void managed_thread_created (Debugger debugger, Thread thread)
			{
				interpreter.OnThreadCreated (thread);
			}

			public void thread_exited (Debugger debugger, Thread thread)
			{
				if ((thread.ThreadFlags & Thread.Flags.Daemon) == 0)
					interpreter.OnThreadExited (thread);
			}

			public void main_process_created (Debugger debugger, Process process)
			{
				interpreter.OnMainProcessCreated (process);
			}

			public void process_reached_main (Debugger debugger, Process process)
			{
				interpreter.OnProcessReachedMain (process);
			}

			public void process_created (Debugger debugger, Process process)
			{
				interpreter.OnProcessCreated (process);
			}

			public void process_exited (Debugger debugger, Process process)
			{
				interpreter.OnProcessExited (process);
			}

			public void process_execd (Debugger debugger, Process process)
			{
				interpreter.OnProcessExecd (process);
			}

			public void target_exited (Debugger debugger)
			{
				interpreter.OnTargetExited ();
			}
		}

		protected class ProcessEventSink : DebuggerMarshalByRefObject
		{
			Interpreter interpreter;

			public ProcessEventSink (Interpreter interpreter, Process process)
			{
				this.interpreter = interpreter;				
				process.TargetOutputEvent += target_output;
			}

			public void target_output (bool is_stderr, string line)
			{
				interpreter.OnTargetOutput (is_stderr, line);
			}
		}
	}
}
