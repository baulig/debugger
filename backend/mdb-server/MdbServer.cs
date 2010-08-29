using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbServer : ServerObject
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

		public DebuggerServer.ServerType GetServerType ()
		{
			return (DebuggerServer.ServerType) Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_SERVER_TYPE, null).ReadInt ();
		}

		public DebuggerServer.ArchTypeEnum GetArchType ()
		{
			return (DebuggerServer.ArchTypeEnum) Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_ARCH_TYPE, null).ReadInt ();
		}

		public DebuggerServer.ServerCapabilities GetCapabilities ()
		{
			return (DebuggerServer.ServerCapabilities) Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_CAPABILITIES, null).ReadInt ();
		}

		public MdbInferior CreateInferior (MdbBreakpointManager bpm)
		{
			int iid = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_INFERIOR, new Connection.PacketWriter ().WriteInt (bpm.ID)).ReadInt ();
			return new MdbInferior (Connection, iid);
		}

		public MdbBreakpointManager CreateBreakpointManager ()
		{
			int iid = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_BPM, null).ReadInt ();
			return new MdbBreakpointManager (Connection, iid);
		}

		public MdbExeReader CreateExeReader (string filename)
		{
			int iid = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_EXE_READER, new Connection.PacketWriter ().WriteString (filename)).ReadInt ();
			return new MdbExeReader (Connection, iid);
		}

		internal override void HandleEvent (ServerEvent e)
		{
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
