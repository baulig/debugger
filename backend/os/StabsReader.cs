using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal class StabsReader : DebuggerMarshalByRefObject
	{
		protected readonly OperatingSystemBackend os;
		protected readonly ExecutableReader bfd;
		protected readonly Module module;

		ObjectCache stab_reader;
		ObjectCache stabstr_reader;

		public StabsReader (OperatingSystemBackend os, ExecutableReader bfd, Module module)
		{
			this.os = os;
			this.bfd = bfd;
			this.module = module;

			stab_reader = create_reader (".stab", false);
			stabstr_reader = create_reader (".stabstr", true);

			var reader = GetStabReader ();
			var strreader = GetStabStrReader ();
			bool initialized = false;

			Console.WriteLine ("TEST: {0}", reader.Size);

			while (!reader.IsEof) {
				var strtab_idx = reader.ReadInt32 ();
				var stab_type = reader.ReadByte ();
				var stab_other = reader.ReadByte ();
				var stab_desc = reader.ReadInt16 ();
				var stab_value = reader.ReadInt32 ();

#if FIXME
				Console.WriteLine ("STAB: {0:x} {1:x} {2:x} {3:x} {4:x}",
					strtab_idx, stab_type, stab_other, stab_desc, stab_value);
#endif

				if (!initialized) {
					initialized = true;
					continue;
				}

				string symbol = null;
				if (strtab_idx > 0) {
					symbol = strreader.PeekString (strtab_idx);
				}

				if (stab_type == 0x64) { // N_SO
					Console.WriteLine ("SOURCE FILE: {0}", symbol);
				} else if (stab_type == 0x22) { // N_FNAME
					Console.WriteLine ("FUNCTION: {0}", symbol);
				} else if (stab_type == 0x44) { // N_SLINE
				} else if (stab_type == 0x80) { // N_LSYM
				} else if (stab_type == 0x82) { // N_BINCL
				} else if (stab_type == 0xA2) { // N_EINCL
				} else {
					Console.WriteLine ("STAB: {0:x} {1:x} {2:x} {3:x} {4:x}",
						strtab_idx, stab_type, stab_other, stab_desc, stab_value);
					;
				}
			}
		}

		public static bool IsSupported (ExecutableReader bfd)
		{
			Console.WriteLine ("STABS: {0}", bfd.TargetName);
			if ((bfd.TargetName == "pe-i386") || (bfd.TargetName == "pei-i386"))
				return bfd.HasSection (".stab");
			else
				return false;
		}

		protected StabsBinaryReader GetStabReader ()
		{
			return new StabsBinaryReader (os, bfd, (TargetBlob) stab_reader.Data, false);
		}

		protected StabsBinaryReader GetStabStrReader ()
		{
			return new StabsBinaryReader (os, bfd, (TargetBlob) stabstr_reader.Data, false);
		}

		object create_reader_func (object user_data)
		{
			try {
				byte[] contents = bfd.GetSectionContents ((string) user_data);
				return new TargetBlob (contents, bfd.TargetMemoryInfo);
			} catch {
				Report.Debug (DebugFlags.DwarfReader,
					      "{1} Can't find DWARF 2 debugging info in section `{0}'",
					      bfd.FileName, (string) user_data);
				return null;
			}
		}

		ObjectCache create_reader (string section_name, bool optional)
		{
			if (!bfd.HasSection (section_name)) {
				if (optional)
					return null;

				throw new DwarfException (bfd, "Missing section '{0}'.", section_name);
			}

			return new ObjectCache (new ObjectCacheFunc (create_reader_func), section_name, 5);
		}

		internal class StabsBinaryReader : TargetBinaryReader
		{
			ExecutableReader bfd;
			OperatingSystemBackend os;
			bool is64bit;

			public StabsBinaryReader (OperatingSystemBackend os, ExecutableReader bfd,
						  TargetBlob blob, bool is64bit)
				: base (blob)
			{
				this.os = os;
				this.bfd = bfd;
				this.is64bit = is64bit;
			}

			public ExecutableReader ExecutableReader
			{
				get
				{
					return bfd;
				}
			}

			public OperatingSystemBackend OperatingSystem
			{
				get
				{
					return os;
				}
			}

			public long PeekOffset (long pos)
			{
				if (is64bit)
					return PeekInt64 (pos);
				else
					return PeekInt32 (pos);
			}

			public long PeekOffset (long pos, out int size)
			{
				if (is64bit) {
					size = 8;
					return PeekInt64 (pos);
				} else {
					size = 4;
					return PeekInt32 (pos);
				}
			}

			public long ReadOffset ()
			{
				if (is64bit)
					return ReadInt64 ();
				else
					return ReadInt32 ();
			}
		}
	}
}
