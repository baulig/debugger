using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbServer : ServerObject, IDebuggerServer
	{
		public MdbServer (Connection connection)
			: base (connection, -1, ServerObjectKind.Server)
		{ }

		enum CmdServer {
			GET_TARGET_INFO = 1,
			GET_SERVER_TYPE = 2,
			GET_ARCH_TYPE = 3,
			GET_CAPABILITIES = 4,
			CREATE_INFERIOR = 5,
			CREATE_BPM = 6,
			CREATE_EXE_READER = 7
		}

		public TargetInfo GetTargetInfo ()
		{
			var reader = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_TARGET_INFO, null);
			return new TargetInfo (reader.ReadInt (), reader.ReadInt (), reader.ReadInt (), reader.ReadByte () != 0);
		}

		public DebuggerServer.ServerType ServerType {
			get { return (DebuggerServer.ServerType) Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_SERVER_TYPE, null).ReadInt (); }
		}

		public DebuggerServer.ArchTypeEnum Architecture {
			get { return (DebuggerServer.ArchTypeEnum) Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_ARCH_TYPE, null).ReadInt (); }
		}

		public DebuggerServer.ServerCapabilities Capabilities {
			get { return (DebuggerServer.ServerCapabilities) Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_CAPABILITIES, null).ReadInt (); }
		}

		public MdbInferior CreateInferior (MdbBreakpointManager bpm)
		{
			int iid = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_INFERIOR, new Connection.PacketWriter ().WriteInt (bpm.ID)).ReadInt ();
			return new MdbInferior (Connection, iid);
		}

		IInferior IDebuggerServer.CreateInferior (IBreakpointManager bpm)
		{
			return CreateInferior ((MdbBreakpointManager) bpm);
		}

		public MdbBreakpointManager CreateBreakpointManager ()
		{
			int iid = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_BPM, null).ReadInt ();
			return new MdbBreakpointManager (Connection, iid);
		}

		IBreakpointManager IDebuggerServer.CreateBreakpointManager ()
		{
			return CreateBreakpointManager ();
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
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
