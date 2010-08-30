using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbExeReader : ServerObject, IExecutableReader
	{
		public MdbExeReader (Connection connection, int id)
			: base (connection, id, ServerObjectKind.ExeReader)
		{
			StartAddress = connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_START_ADDRESS, new Connection.PacketWriter ().WriteInt (ID)).ReadLong ();
			FileName = connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_FILENAME, new Connection.PacketWriter ().WriteInt (ID)).ReadString ();
			TargetName = connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_TARGET_NAME, new Connection.PacketWriter ().WriteInt (ID)).ReadString ();
		}

		enum CmdExeReader {
			GET_FILENAME = 1,
			GET_START_ADDRESS = 2,
			LOOKUP_SYMBOL = 3,
			GET_TARGET_NAME = 4,
			HAS_SECTION = 5,
			GET_SECTION_ADDRESS = 6,
			GET_SECTION_CONTENTS = 7
		}

		public long StartAddress {
			get; private set;
		}

		public string FileName {
			get; private set;
		}

		public string TargetName {
			get; private set;
		}

		public long LookupSymbol (string name)
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.LOOKUP_SYMBOL, new Connection.PacketWriter ().WriteInt (ID).WriteString (name)).ReadLong ();
		}

		public bool HasSection (string name)
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.HAS_SECTION, new Connection.PacketWriter ().WriteInt (ID).WriteString (name)).ReadByte () != 0;
		}

		public long GetSectionAddress (string name)
		{
			return Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_SECTION_ADDRESS, new Connection.PacketWriter ().WriteInt (ID).WriteString (name)).ReadLong ();
		}

		public byte[] GetSectionContents (string name)
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
