using System;
using System.IO;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;

namespace Mono.Debugger.Backend
{
	internal sealed class ProcessStart : DebuggerMarshalByRefObject
	{
		public readonly int PID;
		public readonly string CoreFile;
		public readonly bool IsNative;
		public readonly bool LoadNativeSymbolTable = true;

		string cwd;
		bool stop_in_main = true;
		bool redirect_output = false;
		string[] argv;
		string[] envp;
		DebuggerOptions options;
		DebuggerSession session;

		public static string MonoPath;

		static ProcessStart ()
		{
			MonoPath = Path.GetFullPath (BuildInfo.mono);
		}

		static bool IsMonoAssembly (string filename)
		{
			try {
				using (FileStream stream = new FileStream (filename, FileMode.Open, FileAccess.Read)) {
					byte[] data = new byte [128];
					if (stream.Read (data, 0, 128) != 128)
						return false;

					if ((data [0] != 'M') || (data [1] != 'Z'))
						return false;

					int offset = data [60] + (data [61] << 8) +
						(data [62] << 16) + (data [63] << 24);

					stream.Position = offset;

					data = new byte [28];
					if (stream.Read (data, 0, 28) != 28)
						return false;

					if ((data [0] != 'P') && (data [1] != 'E') &&
					    (data [2] != 0) && (data [3] != 0))
						return false;

					return true;
				}
			} catch {
				return false;
			}
		}

		internal ProcessStart (DebuggerSession session)
		{
			if (session == null)
				throw new ArgumentNullException ();

			this.session = session;
			this.options = session.Options;

#if HAVE_XSP
			if (options.StartXSP)
				options.File = BuildInfo.xsp;
#endif

			if ((options.File == null) || (options.File == ""))
				throw new ArgumentException ("options.File null or empty", "options");
			if (options.InferiorArgs == null)
				throw new ArgumentException ("InferiorArgs null", "options");

			stop_in_main = options.StopInMain;
			redirect_output = session.Config.RedirectOutput;

			cwd = options.WorkingDirectory;
			string mono_path = options.MonoPath ?? MonoPath;

#if HAVE_XSP
			if (options.StartXSP) {
				IsNative = false;

				ArrayList start_argv = new ArrayList ();
				start_argv.Add (mono_path);
				start_argv.Add ("--inside-mdb");
				if (options.JitOptimizations != null)
					start_argv.Add ("--optimize=" + options.JitOptimizations);
				if (options.JitArguments != null)
					start_argv.AddRange (options.JitArguments);
				start_argv.Add (BuildInfo.xsp);
				start_argv.Add ("--nonstop");
				if (options.XSP_Root != null) {
					start_argv.Add ("--root");
					start_argv.Add (options.XSP_Root);
				}

				this.argv = new string [options.InferiorArgs.Length + start_argv.Count];
				start_argv.CopyTo (this.argv, 0);
				if (options.InferiorArgs.Length > 0)
					options.InferiorArgs.CopyTo (this.argv, start_argv.Count);

				SetupEnvironment ();
				return;
			}
#endif

			if (options.RemoteServer != null) {
				IsNative = true;

				this.argv = new string [options.InferiorArgs.Length + 1];
				argv [0] = options.File;
				options.InferiorArgs.CopyTo (this.argv, 1);

				SetupEnvironment ();
				return;
			}

			if (!File.Exists (options.File))
				throw new TargetException (TargetError.CannotStartTarget,
							   "No such file or directory: `{0}'",
							   options.File);

			if (IsMonoAssembly (options.File)) {
				IsNative = false;

				ArrayList start_argv = new ArrayList ();
				start_argv.Add (mono_path);
				start_argv.Add ("--inside-mdb");
				if (options.JitOptimizations != null)
					start_argv.Add ("--optimize=" + options.JitOptimizations);
				if (options.JitArguments != null)
					start_argv.AddRange (options.JitArguments);

				this.argv = new string [options.InferiorArgs.Length + start_argv.Count + 1];
				start_argv.CopyTo (this.argv, 0);
				argv [start_argv.Count] = options.File;
				options.InferiorArgs.CopyTo (this.argv, start_argv.Count + 1);
			} else {
				IsNative = true;

				this.argv = new string [options.InferiorArgs.Length + 1];
				argv [0] = options.File;
				options.InferiorArgs.CopyTo (this.argv, 1);
			}

			SetupEnvironment ();
		}

		internal ProcessStart (DebuggerSession session, int pid)
		{
			this.session = session;
			this.options = session.Options;
			this.PID = pid;

			stop_in_main = options.StopInMain;

			IsNative = true;
		}

		internal ProcessStart (DebuggerSession session, string core_file)
			: this (session)
		{
			this.CoreFile = core_file;
		}

		internal ProcessStart (ProcessStart parent, int pid)
		{
			this.PID = pid;

			this.session = parent.session;
			this.options = parent.options;
			this.cwd = parent.cwd;
			this.argv = parent.argv;

			SetupEnvironment ();
		}

		public void SetupApplication (string exe_file, string cwd, string[] cmdline_args)
		{
			this.cwd = cwd;

			cmdline_args [0] = exe_file;
			this.argv = cmdline_args;

			options = options.Clone ();
			options.File = exe_file;
			options.InferiorArgs = cmdline_args;
			options.WorkingDirectory = cwd;

			SetupEnvironment ();
		}

		public DebuggerSession Session {
			get { return session; }
		}

		public DebuggerOptions Options {
			get { return options; }
		}

		public string[] CommandLineArguments {
			get { return argv; }
		}

		public string[] Environment {
			get { return envp; }
		}

		public string TargetApplication {
			get { return argv [0]; }
		}

		public string WorkingDirectory {
			get { return cwd; }
		}

		public bool StopInMain {
			get { return stop_in_main; }
			internal set { stop_in_main = value; }
		}

		public bool RedirectOutput {
			get { return redirect_output; }
		}

		void AddUserEnvironment (Hashtable hash)
		{
			if (options.UserEnvironment == null)
				return;
			foreach (string name in options.UserEnvironment.Keys)
				hash.Add (name, (string) options.UserEnvironment [name]);
		}

		void add_env_path (Hashtable hash, string name, string value)
		{
			if (!hash.Contains (name))
				hash.Add (name, value);
			else {
				hash [name] = value + ":" + value;
			}
		}

		void SetupEnvironment ()
		{
			Hashtable hash = new Hashtable ();
			AddUserEnvironment (hash);

			IDictionary env_vars = System.Environment.GetEnvironmentVariables ();
			foreach (string var in env_vars.Keys) {
				if (var == "GC_DONT_GC")
					continue;

				// Allow `UserEnvironment' to override env vars.
				if (hash.Contains (var))
					continue;
				hash.Add (var, env_vars [var]);
			}

			add_env_path (hash, "MONO_SHARED_HOSTNAME", "mdb");

			add_env_path (hash, "MONO_GENERIC_SHARING", "none");

			ArrayList list = new ArrayList ();
			foreach (DictionaryEntry entry in hash) {
				string key = (string) entry.Key;
				string value = (string) entry.Value;

				list.Add (key + "=" + value);
			}

			envp = new string [list.Count];
			list.CopyTo (envp, 0);
		}


		string GetFullPath (string path)
		{
			string full_path;
			if (path.StartsWith ("./"))
				full_path = Path.GetFullPath (path.Substring (2));
			else if (path.Length > 0)
				full_path = Path.GetFullPath (path);
			else // FIXME: should search $PATH or something too
				full_path = Path.GetFullPath ("./");

			if (full_path.EndsWith ("/."))
				full_path = full_path.Substring (0, full_path.Length-2);
			return full_path;
		}

		string print_argv (string[] argv)
		{
			if (argv == null)
				return "null";
			else
				return String.Join (":", argv);
		}

		public string CommandLine {
			get { return print_argv (argv); }
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3})",
					      GetType (), WorkingDirectory,
					      print_argv (argv), IsNative);
		}
	}
}
