using System;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal class MdbProcess : ServerObject, IProcess
	{
		public MdbProcess (Connection connection, int id)
			: base (connection, id, ServerObjectKind.Process)
		{ }

		bool initialized;
		MdbExeReader main_reader;
		MonoRuntime mono_runtime;

		void initialize ()
		{
			if (initialized)
				return;

			int reader_iid = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.GET_MAIN_READER, new Connection.PacketWriter ().WriteInt (ID)).ReadInt ();
			main_reader = new MdbExeReader (Connection, reader_iid);
			initialized = true;

			int runtime_iid = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.GET_MONO_RUNTIME, new Connection.PacketWriter ().WriteInt (ID)).ReadInt ();
			if (runtime_iid != 0)
				mono_runtime = new MonoRuntime (Connection, runtime_iid);

			initialized = true;
		}

		enum CmdProcess {
			GET_MAIN_READER = 1,
			INITIALIZE_PROCESS = 2,
			SPAWN = 3,
			ATTACH = 4,
			SUSPEND = 5,
			RESUME = 6,
			GET_ALL_THREADS = 7,
			GET_MONO_RUNTIME = 8,
			GET_ALL_MODULES = 9
		}

		public MdbExeReader MainReader {
			get { 
				lock (this) {
					initialize ();
					return main_reader;
				}
			}
		}

		IExecutableReader IProcess.MainReader {
			get { return MainReader; }
		}

		public MonoRuntime MonoRuntime {
			get {
				lock (this) {
					initialize ();
					return mono_runtime;
				}
			}
		}

		IMonoRuntime IProcess.MonoRuntime {
			get { return MonoRuntime; }
		}

		public MdbInferior Spawn (string cwd, string[] argv, string[] envp)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteId (ID);
			writer.WriteString (cwd ?? "");

			int argc = argv.Length;
			if (argv[argc - 1] == null)
				argc--;
			writer.WriteInt (argc);
			for (int i = 0; i < argc; i++)
				writer.WriteString (argv[i] ?? "dummy");
			var reader = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.SPAWN, writer);
			int inferior_iid = reader.ReadInt ();
			int pid = reader.ReadInt ();

			return new MdbInferior (Connection, inferior_iid);
		}

		IInferior IProcess.Spawn (string cwd, string[] argv, string[] envp)
		{
			return Spawn (cwd, argv, envp);
		}

		public MdbInferior Attach (int pid)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteId (ID);
			writer.WriteInt (pid);

			var reader = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.ATTACH, writer);
			int inferior_iid = reader.ReadInt ();

			return new MdbInferior (Connection, inferior_iid);
		}

		IInferior IProcess.Attach (int pid)
		{
			return Attach (pid);
		}

		public void Suspend (IInferior caller)
		{
			Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.SUSPEND, new Connection.PacketWriter ().WriteId (ID).WriteId (caller.ID));
		}

		public void Resume (IInferior caller)
		{
			Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.RESUME, new Connection.PacketWriter ().WriteId (ID).WriteId (caller.ID));
		}

		public MdbInferior[] GetAllThreads ()
		{
			var reader = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.GET_ALL_THREADS, new Connection.PacketWriter ().WriteId (ID));

			int count = reader.ReadInt ();
			MdbInferior[] threads = new MdbInferior [count];

			for (int i = 0; i < count; i++) {
				threads [i] = (MdbInferior) ServerObject.GetOrCreateObject (Connection, reader.ReadId (), ServerObjectKind.Inferior);
			}

			return threads;
		}

		IInferior[] IProcess.GetAllThreads ()
		{
			return GetAllThreads ();
		}

		public MdbExeReader[] GetAllModules ()
		{
			var reader = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.GET_ALL_MODULES, new Connection.PacketWriter ().WriteId (ID));

			int count = reader.ReadInt ();
			MdbExeReader[] modules = new MdbExeReader [count];

			for (int i = 0; i < count; i++) {
				modules [i] = (MdbExeReader) ServerObject.GetOrCreateObject (Connection, reader.ReadId (), ServerObjectKind.ExeReader);
			}

			return modules;
		}

		IExecutableReader[] IProcess.GetAllModules ()
		{
			return GetAllModules ();
		}
	}
}
