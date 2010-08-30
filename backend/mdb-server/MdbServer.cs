using System;
using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal class MdbServer : ServerObject, IDebuggerServer
	{
		public MdbServer (Connection connection)
			: base (connection, -1, ServerObjectKind.Server)
		{
			var info_reader = connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_TARGET_INFO, null);
			TargetInfo = new TargetInfo (info_reader.ReadInt (), info_reader.ReadInt (), info_reader.ReadInt (), info_reader.ReadByte () != 0);

			ServerType = (ServerType) connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_SERVER_TYPE, null).ReadInt ();
			ArchType = (ArchType) connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_ARCH_TYPE, null).ReadInt ();
			Capabilities = (ServerCapabilities) connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_CAPABILITIES, null).ReadInt ();

			var bpm_iid = connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_BPM, null).ReadInt ();
			BreakpointManager = new MdbBreakpointManager (connection, bpm_iid);
		}

		enum CmdServer {
			GET_TARGET_INFO = 1,
			GET_SERVER_TYPE = 2,
			GET_ARCH_TYPE = 3,
			GET_CAPABILITIES = 4,
			CREATE_BPM = 6,
			CREATE_EXE_READER = 7,

			GET_BPM = 100,
			SPAWN
		}

		public TargetInfo TargetInfo {
			get; private set;
		}

		public ServerType ServerType {
			get; private set;
		}

		public ArchType ArchType {
			get; private set;
		}

		public ServerCapabilities Capabilities {
			get; private set;
		}

		public MdbBreakpointManager BreakpointManager {
			get; private set;
		}

		IBreakpointManager IDebuggerServer.BreakpointManager {
			get { return BreakpointManager; }
		}

		public MdbInferior Spawn (SingleSteppingEngine sse, string cwd, string[] argv, string[] envp)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteString (cwd ?? "");

			int argc = argv.Length;
			if (argv [argc-1] == null)
				argc--;
			writer.WriteInt (argc);
			for (int i = 0; i < argc; i++)
				writer.WriteString (argv [i] ?? "dummy");
			var reader = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.SPAWN, writer);
			int inferior_iid = reader.ReadInt ();
			int pid = reader.ReadInt ();
			return new MdbInferior (Connection, sse, pid, inferior_iid);
		}

		IInferior IDebuggerServer.Spawn (SingleSteppingEngine sse, string cwd, string[] argv, string[] envp)
		{
			return Spawn (sse, cwd, argv, envp);
		}

		public MdbInferior Attach (SingleSteppingEngine sse, int pid)
		{
			throw new NotImplementedException ();
		}

		IInferior IDebuggerServer.Attach (SingleSteppingEngine sse, int pid)
		{
			return Attach (sse, pid);
		}

		public MdbExeReader CreateExeReader (string filename)
		{
			int iid = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_EXE_READER, new Connection.PacketWriter ().WriteString (filename)).ReadInt ();
			return new MdbExeReader (Connection, iid);
		}

		IExecutableReader IDebuggerServer.CreateExeReader (string filename)
		{
			return CreateExeReader (filename);
		}

		internal override void HandleEvent (ServerEvent e)
		{
			Console.WriteLine ("SERVER EVENT: {0}", e);
		}
	}
}
