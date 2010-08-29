using System;
using System.Runtime.InteropServices;
using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbBreakpointManager : ServerObject
	{
		public MdbBreakpointManager (Connection connection, int id)
			: base (connection, id, ServerObjectKind.BreakpointManager)
		{ }

		enum CmdBpm {
			LOOKUP_BY_ADDR = 1,
			LOOKUP_BY_ID = 2
		}

		public int LookupBreakpointByAddr (long address, out bool enabled)
		{
			var reader = Connection.SendReceive (CommandSet.BPM, (int)CmdBpm.LOOKUP_BY_ADDR, new Connection.PacketWriter ().WriteInt (ID).WriteLong (address));
			var index = reader.ReadInt ();
			enabled = reader.ReadByte () != 0;
			return index;
		}

		public bool LookupBreakpointById (int id, out bool enabled)
		{
			var reader = Connection.SendReceive (CommandSet.BPM, (int)CmdBpm.LOOKUP_BY_ID, new Connection.PacketWriter ().WriteInt (ID).WriteInt (id));
			var success = reader.ReadByte () != 0;
			if (!success) {
				enabled = false;
				return false;
			}

			enabled = reader.ReadByte () != 0;
			return true;
		}

		internal override void HandleEvent (ServerEvent e)
		{
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
