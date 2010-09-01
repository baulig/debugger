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

		void initialize ()
		{
			if (initialized)
				return;

			int reader_iid = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.GET_MAIN_READER, new Connection.PacketWriter ().WriteInt (ID)).ReadInt ();
			main_reader = new MdbExeReader (Connection, reader_iid);
			initialized = true;
		}

		enum CmdProcess {
			GET_MAIN_READER = 1,
			INITIALIZE_PROCESS = 2,
			SPAWN =3
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

		public void InitializeProcess (MdbInferior inferior)
		{
			Connection.SendReceive (CommandSet.PROCESS, (int)CmdProcess.INITIALIZE_PROCESS, new Connection.PacketWriter ().WriteInt (ID).WriteInt (inferior.ID));
		}

		void IProcess.InitializeProcess (IInferior inferior)
		{
			InitializeProcess ((MdbInferior) inferior);
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
			throw new NotImplementedException ();
		}

		IInferior IProcess.Attach (int pid)
		{
			return Attach (pid);
		}
	}
}
