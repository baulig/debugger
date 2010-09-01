using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Mono.Debugger.Server;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal class StabsReader : DebuggingFileReader
	{
		ObjectCache stab_reader;
		ObjectCache stabstr_reader;

		List<StabsTargetMethod> methods = new List<StabsTargetMethod> ();
		Dictionary<string, StabsSourceFile> sources = new Dictionary<string, StabsSourceFile> ();
		StabsSymbolTable symtab;

		public StabsReader (OperatingSystemBackend os, ExecutableReader bfd, Module module)
			: base (os, bfd, module)
		{
			stab_reader = create_reader (".stab", false);
			stabstr_reader = create_reader (".stabstr", true);

			var reader = GetStabReader ();
			var strreader = GetStabStrReader ();
			bool initialized = false;

			Console.WriteLine ("TEST: {0}", reader.Size);

			StabsSourceFile current_file = null;
			StabsTargetMethod current_method = null;

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
					if (symbol != null) {
						if (sources.ContainsKey (symbol))
							current_file = sources[symbol];
						else {
							current_file = new StabsSourceFile (os.Process.Session, module, symbol);
							sources.Add (symbol, current_file);
						}
					} else {
						current_file = null;
					}
				} else if ((stab_type == 0x22) || (stab_type == 0x24)) { // N_FNAME | N_FUN
					Console.WriteLine ("FUNCTION: {0} {1:x}", symbol, stab_value);
					if (symbol != null) {
						current_method = new StabsTargetMethod (this, current_file, symbol, stab_value);
						methods.Add (current_method);
					} else {
						if (current_method != null)
							current_method.CloseMethod (stab_value);
						current_method = null;
					}
				} else if (stab_type == 0x44) { // N_SLINE
#if FIXME
					Console.WriteLine ("LINE: {0:x} {1:x} {2:x} {3}",
						stab_other, stab_desc, stab_value, symbol);
#endif
					if (current_method != null)
						current_method.LineNumberTable.AddLine ((int) stab_desc, stab_value);
				} else if (stab_type == 0x80) { // N_LSYM
				} else if (stab_type == 0x82) { // N_BINCL
				} else if (stab_type == 0xA2) { // N_EINCL
				} else {
#if FIXME
					Console.WriteLine ("STAB: {0:x} {1:x} {2:x} {3:x} {4:x}",
						strtab_idx, stab_type, stab_other, stab_desc, stab_value);
					;
#endif
				}
			}

			symtab = new StabsSymbolTable (this);

		}

		public static bool IsSupported (ExecutableReader bfd)
		{
			Console.WriteLine ("STABS: {0}", bfd.TargetName);
			if ((bfd.TargetName == "pe-i386") || (bfd.TargetName == "pei-i386"))
				return bfd.HasSection (".stab");
			else
				return false;
		}

		public override SourceFile[] Sources
		{
			get { return sources.Values.ToArray (); }
		}

		public override ISymbolTable SymbolTable
		{
			get { return symtab; }
		}

		public override MethodSource[] GetMethods (SourceFile file)
		{
			return methods.Where (x => x.SourceFile == file).Select (x => x.MethodSource).ToArray ();
		}

		public override MethodSource FindMethod (string name)
		{
			return null;
		}

		protected TargetAddress GetAddress (long address)
		{
			if (!NativeReader.IsLoaded)
				throw new InvalidOperationException (
					"Trying to get an address from not-loaded " +
					"symbol file `" + NativeReader.FileName + "'");

			if (NativeReader.BaseAddress.IsNull)
				return new TargetAddress (AddressDomain.Global, address);
			else
				return NativeReader.BaseAddress + address;
		}

		protected StabsBinaryReader GetStabReader ()
		{
			return new StabsBinaryReader (OS, NativeReader, (TargetBlob) stab_reader.Data, false);
		}

		protected StabsBinaryReader GetStabStrReader ()
		{
			return new StabsBinaryReader (OS, NativeReader, (TargetBlob) stabstr_reader.Data, false);
		}

		object create_reader_func (object user_data)
		{
			try {
				byte[] contents = NativeReader.GetSectionContents ((string) user_data);
				return new TargetBlob (contents, NativeReader.TargetInfo);
			} catch {
				Report.Debug (DebugFlags.DwarfReader,
					      "{1} Can't find DWARF 2 debugging info in section `{0}'",
					      NativeReader.FileName, (string) user_data);
				return null;
			}
		}

		ObjectCache create_reader (string section_name, bool optional)
		{
			if (!NativeReader.HasSection (section_name)) {
				if (optional)
					return null;

				throw new DwarfException (NativeReader, "Missing section '{0}'.", section_name);
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

		class StabsSourceFile : SourceFile
		{
			public StabsSourceFile (DebuggerSession session, Module module, string filename)
				: base (session, module, filename)
			{ }

			public override bool IsAutoGenerated
			{
				get { return false; }
			}

			public override bool CheckModified ()
			{
				return false;
			}
		}

		class StabsTargetMethod : Method
		{
			StabsReader stabs;
			StabsSourceFile file;
			StabsMethodSource source;
			StabsLineNumberTable lnt;
			long start_addr;

			public StabsTargetMethod (StabsReader stabs, StabsSourceFile file, string name, long start_addr)
				: base (name, stabs.NativeReader.FileName, stabs.Module)
			{
				this.stabs = stabs;
				this.file = file;
				this.start_addr = start_addr;

				lnt = new StabsLineNumberTable (this, start_addr);
			}

			internal StabsReader StabsReader
			{
				get { return stabs; }
			}

			internal StabsSourceFile SourceFile
			{
				get { return file; }
			}

			new internal StabsLineNumberTable LineNumberTable
			{
				get { return lnt; }
			}

			internal void CloseMethod (long end_addr)
			{
				source = new StabsMethodSource (this);
				SetAddresses (stabs.GetAddress (start_addr), stabs.GetAddress (start_addr + end_addr));
				SetLineNumbers (lnt);
			}

			public override int Domain
			{
				get { return -1; }
			}

			public override bool IsWrapper
			{
				get { return false; }
			}

			public override bool IsCompilerGenerated
			{
				get { return false; }
			}

			public override bool HasSource
			{
				get { return source != null; }
			}

			public override MethodSource MethodSource
			{
				get {
					if (source == null)
						throw new InvalidOperationException ();

					return source;
				}
			}

			public override object MethodHandle
			{
				get { throw new NotImplementedException (); }
			}

			public override TargetClassType GetDeclaringType (Thread target)
			{
				throw new NotImplementedException ();
			}

			public override bool HasThis
			{
				get { return false; }
			}

			public override TargetVariable GetThis (Thread target)
			{
				throw new NotImplementedException ();
			}

			public override TargetVariable[] GetParameters (Thread target)
			{
				throw new NotImplementedException ();
			}

			public override TargetVariable[] GetLocalVariables (Thread target)
			{
				throw new NotImplementedException ();
			}

			public override string[] GetNamespaces ()
			{
				throw new NotImplementedException ();
			}

			internal override MethodSource GetTrampoline (TargetMemoryAccess memory, TargetAddress address)
			{
				throw new NotImplementedException ();
			}
		}

		class StabsMethodSource : MethodSource
		{
			StabsTargetMethod method;

			public StabsMethodSource (StabsTargetMethod method)
			{
				this.method = method;
			}

			public override Module Module
			{
				get { return method.Module; }
			}

			public override string Name
			{
				get { return method.Name; }
			}

			public override bool IsManaged
			{
				get { return false; }
			}

			public override bool IsDynamic
			{
				get { return false; }
			}

			public override TargetClassType DeclaringType
			{
				get { throw new NotImplementedException (); }
			}

			public override TargetFunctionType Function
			{
				get { throw new NotImplementedException (); }
			}

			public override bool HasSourceFile
			{
				get { return true; }
			}

			public override SourceFile SourceFile
			{
				get { return method.SourceFile; }
			}

			public override bool HasSourceBuffer
			{
				get { return false; }
			}

			public override SourceBuffer SourceBuffer
			{
				get { throw new InvalidOperationException (); }
			}

			public override int StartRow
			{
				get { return method.LineNumberTable.StartRow; }
			}

			public override int EndRow
			{
				get { return method.LineNumberTable.EndRow; }
			}

			public override string[] GetNamespaces ()
			{
				throw new NotImplementedException ();
			}

			public override Method NativeMethod
			{
				get { return method; }
			}
		}

		class StabsLineNumberTable : LineNumberTable
		{
			StabsTargetMethod method;
			long start_addr;

			List<LineEntry> addresses = new List<LineEntry> ();

			int start_row;
			int end_row;

			public StabsLineNumberTable (StabsTargetMethod method, long start_addr)
			{
				this.method = method;
				this.start_addr = start_addr;
			}

			public override bool HasMethodBounds
			{
				get { return false; }
			}

			public override TargetAddress MethodStartAddress
			{
				get { throw new InvalidOperationException (); }
			}

			public override TargetAddress MethodEndAddress
			{
				get { throw new InvalidOperationException (); }
			}

			internal int StartRow
			{
				get { return start_row; }
			}

			internal int EndRow
			{
				get { return end_row; }
			}

			internal void AddLine (int line, long address)
			{
				if (start_row == 0)
					start_row = line;
				end_row = line;
				var target_address = method.StabsReader.GetAddress (start_addr + address);
				addresses.Add (new LineEntry (target_address, 0, line));
			}

			public override TargetAddress Lookup (int line, int column)
			{
				if (column == -1)
					return Lookup (line);

				if ((addresses == null) || (line < StartRow) || (line > EndRow))
					return TargetAddress.Null;

				bool found_a_range = false;

				for (int i = 0; i < addresses.Count; i++) {
					LineEntry entry = addresses[i];

					if (entry.SourceRange == null)
						continue;

					SourceRange range = entry.SourceRange.Value;
					found_a_range = true;

					if ((line < range.StartLine) || (line > range.EndLine))
						continue;
					if ((line == range.StartLine) && (column < range.StartColumn))
						continue;
					if ((line == range.EndLine) && (column > range.EndColumn))
						continue;

					return entry.Address;
				}

				//
				// If the symbol file doesn't contain any source ranges for this
				// method, default to traditional line-based lookup.
				//

				if (!found_a_range)
					return Lookup (line);

				return TargetAddress.Null;
			}

			public override TargetAddress Lookup (int line)
			{
				if ((addresses == null) || (line < StartRow) || (line > EndRow))
					return TargetAddress.Null;

				for (int i = 0; i < addresses.Count; i++) {
					LineEntry entry = addresses[i];

					if (line <= entry.Line)
						return entry.Address;
				}

				return TargetAddress.Null;
			}

			public override SourceAddress Lookup (TargetAddress address)
			{
				if (address.IsNull || (address < method.StartAddress) || (address >= method.EndAddress))
					return null;

				if (addresses.Count < 1)
					return null;

				TargetAddress next_address = method.EndAddress;
				TargetAddress next_not_hidden = method.EndAddress;

				for (int i = addresses.Count - 1; i >= 0; i--) {
					LineEntry entry = addresses[i];

					int range = (int) (next_not_hidden - address);

					next_address = entry.Address;
					if (!entry.IsHidden)
						next_not_hidden = entry.Address;

					if (next_address > address)
						continue;

					int offset = (int) (address - next_address);
					return new SourceAddress (method.SourceFile, null, entry.Line, offset, range);
				}

				if (addresses.Count < 1)
					return null;

				return new SourceAddress (method.SourceFile, null, addresses[0].Line,
					(int) (address - method.StartAddress), (int) (addresses[0].Address - address),
					addresses[0].SourceRange);
			}

			public override void DumpLineNumbers (TextWriter writer)
			{
				writer.WriteLine ();
				writer.Write ("Dumping Line Number Table: {0} - {1} {2}",
					       method.Name, method.StartAddress, method.EndAddress);
				if (method.HasMethodBounds)
					writer.Write (" - {0} {1}", method.MethodStartAddress,
						      method.MethodEndAddress);
				writer.WriteLine ();
				writer.WriteLine ();

				writer.WriteLine ("Generated Lines (file / line / address):");
				writer.WriteLine ("----------------------------------------");

				for (int i = 0; i < addresses.Count; i++) {
					LineEntry entry = addresses[i];
					writer.WriteLine ("{0,4} {1,4} {2,4}  {3}", i,
							  entry.File, entry.Line, entry.Address);
				}

				writer.WriteLine ("----------------------------------------");
			}
		}

		class StabsSymbolTable : SymbolTable
		{
			StabsReader stabs;

			public StabsSymbolTable (StabsReader stabs)
			{
				this.stabs = stabs;
			}

			public override bool HasRanges
			{
				get { return false; }
			}

			public override ISymbolRange[] SymbolRanges
			{
				get { throw new InvalidOperationException (); }
			}

			public override bool HasMethods
			{
				get { return true; }
			}

			protected override ArrayList GetMethods ()
			{
				return new ArrayList (stabs.methods.ToArray ());
			}
		}
	}
}
