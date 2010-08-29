using System;
using System.Runtime.InteropServices;
using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbExeReader : ServerObject
	{
		public MdbExeReader (Connection connection, int id)
			: base (connection, id, ServerObjectKind.ExeReader)
		{ }

		enum CmdExeReader {
			GET_START_ADDRESS = 1,
			LOOKUP_SYMBOL = 2,
			GET_TARGET_NAME = 3,
			HAS_SECTION = 4,
			GET_SECTION_ADDRESS = 5,
			GET_SECTION_CONTENTS = 6
		}

		public long BfdGetStartAddress ()
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_START_ADDRESS, new Connection.PacketWriter ().WriteInt (ID)).ReadLong ();
		}

		public long BfdLookupSymbol (string name)
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.LOOKUP_SYMBOL, new Connection.PacketWriter ().WriteInt (ID).WriteString (name)).ReadLong ();
		}

		public string BfdGetTargetName ()
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_TARGET_NAME, new Connection.PacketWriter ().WriteInt (ID)).ReadString ();
		}

		public bool BfdHasSection (string name)
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.HAS_SECTION, new Connection.PacketWriter ().WriteInt (ID).WriteString (name)).ReadByte () != 0;
		}

		public long BfdGetSectionAddress (string name)
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_SECTION_ADDRESS, new Connection.PacketWriter ().WriteInt (ID).WriteString (name)).ReadLong ();
		}

		public byte[] BfdGetSectionContents (string name)
		{
			var reader = Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_SECTION_CONTENTS, new Connection.PacketWriter ().WriteInt (ID).WriteString (name));
			int size = reader.ReadInt ();
			if (size < 0)
				return null;
			return reader.ReadData (size);
		}

		internal override void HandleEvent (ServerEvent e)
		{
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
