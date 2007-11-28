using System;
using System.IO;
using System.Configuration;
using ST = System.Threading;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Frontend
{
	public class CommandLineInterpreter
	{
		Interpreter interpreter;
		DebuggerEngine engine;
		LineParser parser;
		const string prompt = "(mdb) ";
		int line = 0;

		bool is_inferior_main;
		ST.Thread command_thread;
		ST.Thread main_thread;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_static_init ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_get_pending_sigint ();

		static CommandLineInterpreter ()
		{
			mono_debugger_server_static_init ();
		}

		internal CommandLineInterpreter (bool is_interactive, DebuggerConfiguration config,
						 DebuggerOptions options)
		{
			if (options.HasDebugFlags)
				Report.Initialize (options.DebugOutput, options.DebugFlags);
			else
				Report.Initialize ();

			interpreter = new Interpreter (is_interactive, config, options);
			engine = interpreter.DebuggerEngine;
			parser = new LineParser (engine);

			main_thread = new ST.Thread (new ST.ThreadStart (main_thread_main));
			main_thread.IsBackground = true;

			command_thread = new ST.Thread (new ST.ThreadStart (command_thread_main));
			command_thread.IsBackground = true;
		}

		public CommandLineInterpreter (Interpreter interpreter)
		{
			this.interpreter = interpreter;
			this.engine = interpreter.DebuggerEngine;
			parser = new LineParser (engine);

			main_thread = new ST.Thread (new ST.ThreadStart (main_thread_main));
			main_thread.IsBackground = true;

			command_thread = new ST.Thread (new ST.ThreadStart (command_thread_main));
			command_thread.IsBackground = true;
		}

		public void MainLoop ()
		{
			string s;
			bool is_complete = true;

			parser.Reset ();
			while ((s = ReadInput (is_complete)) != null) {
				parser.Append (s);
				if (parser.IsComplete ()){
					interpreter.ClearInterrupt ();
					parser.Execute ();
					parser.Reset ();
					is_complete = true;
				} else
					is_complete = false;
			}
		}

		void main_thread_main ()
		{
			while (true) {
				try {
					interpreter.ClearInterrupt ();
					MainLoop ();
					if (is_inferior_main)
						break;
				} catch (ST.ThreadAbortException) {
					ST.Thread.ResetAbort ();
				}
			}
		}

		internal void RunMainLoop ()
		{
			is_inferior_main = false;

			command_thread.Start ();

			try {
				if (interpreter.Options.StartTarget)
					interpreter.Start ();

				main_thread.Start ();
				main_thread.Join ();
			} catch (ScriptingException ex) {
				interpreter.Error (ex);
			} catch (TargetException ex) {
				interpreter.Error (ex);
			} catch (Exception ex) {
				interpreter.Error ("ERROR: {0}", ex);
			} finally {
				interpreter.Exit ();
			}
		}

		public void RunInferiorMainLoop ()
		{
			is_inferior_main = true;

			command_thread.Start ();

			TextReader old_stdin = Console.In;
			TextWriter old_stdout = Console.Out;
			TextWriter old_stderr = Console.Error;

			StreamReader stdin_reader = new StreamReader (Console.OpenStandardInput ());

			StreamWriter stdout_writer = new StreamWriter (Console.OpenStandardOutput ());
			stdout_writer.AutoFlush = true;

			StreamWriter stderr_writer = new StreamWriter (Console.OpenStandardError ());
			stderr_writer.AutoFlush = true;

			Console.SetIn (stdin_reader);
			Console.SetOut (stdout_writer);
			Console.SetError (stderr_writer);

			bool old_is_script = interpreter.IsScript;
			bool old_is_interactive = interpreter.IsInteractive;

			interpreter.IsScript = false;
			interpreter.IsInteractive = true;

			Console.WriteLine ();

			try {
				main_thread.Start ();
				main_thread.Join ();
			} catch (ScriptingException ex) {
				interpreter.Error (ex);
			} catch (TargetException ex) {
				interpreter.Error (ex);
			} catch (Exception ex) {
				interpreter.Error ("ERROR: {0}", ex);
			} finally {
				Console.WriteLine ();

				Console.SetIn (old_stdin);
				Console.SetOut (old_stdout);
				Console.SetError (old_stderr);
				interpreter.IsScript = old_is_script;
				interpreter.IsInteractive = old_is_interactive;
			}

			command_thread.Abort ();
		}

		public string ReadInput (bool is_complete)
		{
			++line;
		again:
			string result;
			string the_prompt = is_complete ? prompt : "... ";
			if (interpreter.IsScript) {
				Console.Write (the_prompt);
				result = Console.ReadLine ();
				if (result == null)
					return null;
				if (result != "") {
					;
				} else if (is_complete) {
					engine.Repeat ();
					goto again;
				}
				return result;
			} else {
				result = GnuReadLine.ReadLine (the_prompt);
				if (result == null)
					return null;
				if (result != "")
					GnuReadLine.AddHistory (result);
				else if (is_complete) {
					engine.Repeat ();
					goto again;
				}
				return result;
			}
		}

		public void Error (int pos, string message)
		{
			if (interpreter.Options.IsScript) {
				// If we're reading from a script, abort.
				Console.Write ("ERROR in line {0}, column {1}: ", line, pos);
				Console.WriteLine (message);
				Environment.Exit (1);
			}
			else {
				string prefix = new String (' ', pos + prompt.Length);
				Console.WriteLine ("{0}^", prefix);
				Console.Write ("ERROR: ");
				Console.WriteLine (message);
			}
		}

		void command_thread_main ()
		{
			do {
				Semaphore.Wait ();
				if (mono_debugger_server_get_pending_sigint () == 0)
					continue;

				if (interpreter.Interrupt () > 2)
					main_thread.Abort ();
			} while (true);
		}

		public static void Main (string[] args)
		{
			bool is_terminal = GnuReadLine.IsTerminal (0);

			DebuggerConfiguration config = new DebuggerConfiguration ();
			config.LoadConfiguration ();

			DebuggerOptions options = DebuggerOptions.ParseCommandLine (args);

			Console.WriteLine ("Mono Debugger");

			CommandLineInterpreter interpreter = new CommandLineInterpreter (
				is_terminal, config, options);

			interpreter.RunMainLoop ();
		}
	}
}
