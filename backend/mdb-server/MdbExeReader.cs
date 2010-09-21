using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbExeReader : ServerObject, IExecutableReader
	{
		public MdbExeReader (Connection connection, int id)
			: base (connection, id, ServerObjectKind.ExeReader)
		{ }

		enum CmdExeReader {
			GET_FILENAME = 1,
			GET_START_ADDRESS = 2,
			LOOKUP_SYMBOL = 3,
			GET_TARGET_NAME = 4,
			HAS_SECTION = 5,
			GET_SECTION_ADDRESS = 6,
			GET_SECTION_CONTENTS = 7,
			GET_BASE_ADDRESS = 8,
			GET_END_ADDRESS = 9
		}

		bool initialized;
		long start_address;
		long base_address;
		long end_address;
		string file_name;
		string target_name;

		void initialize ()
		{
			lock (this) {
				if (initialized)
					return;

				start_address = Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_START_ADDRESS, new Connection.PacketWriter ().WriteInt (ID)).ReadLong ();
				file_name = Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_FILENAME, new Connection.PacketWriter ().WriteInt (ID)).ReadString ();
				target_name = Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_TARGET_NAME, new Connection.PacketWriter ().WriteInt (ID)).ReadString ();
				base_address = Connection.SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_BASE_ADDRESS, new Connection.PacketWriter ().WriteInt (ID)).ReadLong ();
				end_address = Connection.SendReceive (CommandSet.EXE_READER, (int) CmdExeReader.GET_END_ADDRESS, new Connection.PacketWriter ().WriteInt (ID)).ReadLong ();
				initialized = true;

				Console.WriteLine ("EXE READER: {0} {1:x} {2:x} {3:x}", file_name,
					base_address, start_address, end_address);
			}
		}

		public long StartAddress {
			get {
				initialize ();
				return start_address;
			}
		}

		public long EndAddress
		{
			get {
				initialize ();
				return end_address;
			}
		}

		public string FileName {
			get {
				initialize ();
				return file_name;
			}
		}

		public string TargetName {
			get {
				initialize ();
				return target_name;
			}
		}

		public long BaseAddress
		{
			get {
				initialize ();
				return base_address;
			}
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
	}
}
