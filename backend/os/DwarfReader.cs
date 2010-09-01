using System;
using System.IO;
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
	internal class DwarfException : Exception
	{
		public DwarfException (ExecutableReader bfd, string message, params object[] args)
			: base (String.Format ("{0}: {1}", bfd.FileName,
					       String.Format (message, args)))
		{ }

		public DwarfException (ExecutableReader bfd, string message, Exception inner)
			: base (String.Format ("{0}: {1}", bfd.FileName, message), inner)
		{ }
	}

	internal class DwarfBinaryReader : TargetBinaryReader
	{
		ExecutableReader bfd;
		OperatingSystemBackend os;
		bool is64bit;

		public DwarfBinaryReader (OperatingSystemBackend os, ExecutableReader bfd,
					  TargetBlob blob, bool is64bit)
			: base (blob)
		{
			this.os = os;
			this.bfd = bfd;
			this.is64bit = is64bit;
		}

		public ExecutableReader ExecutableReader {
			get {
				return bfd;
			}
		}

		public OperatingSystemBackend OperatingSystem {
			get {
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

		public long ReadInitialLength ()
		{
			bool is64bit;
			return ReadInitialLength (out is64bit);
		}

		public long ReadInitialLength (out bool is64bit)
		{
			long length = ReadInt32 ();

			if (length < 0xfffffff0) {
				is64bit = false;
				return length;
			} else if (length == 0xffffffff) {
				is64bit = true;
				return ReadInt64 ();
			} else
				throw new DwarfException (
					bfd, "Unknown initial length field {0:x}",
					length);
		}
	}

	internal class DwarfReader : DebuggingFileReader
	{
		bool is64bit;
		byte address_size;

		ObjectCache debug_loc_reader;
		ObjectCache debug_info_reader;
		ObjectCache debug_abbrev_reader;
		ObjectCache debug_line_reader;
		ObjectCache debug_aranges_reader;
		ObjectCache debug_pubnames_reader;
		ObjectCache debug_pubtypes_reader;
		ObjectCache debug_str_reader;
		ObjectCache debug_ranges_reader;

		Hashtable source_file_hash;
		Hashtable method_source_hash;
		Hashtable method_hash;
		Hashtable compile_unit_hash;
		DwarfSymbolTable symtab;
		ArrayList aranges;
		Hashtable pubnames;
		// Hashtable pubtypes;
		TargetInfo target_info;

		public DwarfReader (OperatingSystemBackend os, ExecutableReader bfd, Module module)
			: base (os, bfd, module)
		{
			this.target_info = bfd.TargetInfo;

			debug_info_reader = create_reader (".debug_info", false);

			DwarfBinaryReader reader = DebugInfoReader;

			reader.ReadInitialLength (out is64bit);
			int version = reader.ReadInt16 ();
			if (version < 2)
				throw new DwarfException (
					bfd, "Wrong DWARF version: {0}", version);

			reader.ReadOffset ();
			address_size = reader.ReadByte ();

			if ((address_size != 4) && (address_size != 8))
				throw new DwarfException (
					bfd, "Unknown address size: {0}", address_size);

			debug_abbrev_reader = create_reader (".debug_abbrev", false);
			debug_line_reader = create_reader (".debug_line", false);
			debug_aranges_reader = create_reader (".debug_aranges", true);
			debug_pubnames_reader = create_reader (".debug_pubnames", true);
			debug_pubtypes_reader = create_reader (".debug_pubtypes", true);
			debug_str_reader = create_reader (".debug_str", true);
			debug_loc_reader = create_reader (".debug_loc", false);
			debug_ranges_reader = create_reader (".debug_ranges", true);

			compile_unit_hash = Hashtable.Synchronized (new Hashtable ());
			method_source_hash = Hashtable.Synchronized (new Hashtable ());
			method_hash = Hashtable.Synchronized (new Hashtable ());
			source_file_hash = Hashtable.Synchronized (new Hashtable ());

			if (bfd.IsLoaded) {
				aranges = ArrayList.Synchronized (read_aranges ());
				symtab = new DwarfSymbolTable (this, aranges);
				pubnames = read_pubnames ();
				// pubtypes = read_pubtypes ();
			}

			long offset = 0;
			while (offset < reader.Size) {
				CompileUnitBlock block = new CompileUnitBlock (this, offset);
				compile_unit_hash.Add (offset, block);
				offset += block.length;
			}
		}

		public void ModuleLoaded ()
		{
			if (aranges != null)
				return;

			aranges = ArrayList.Synchronized (read_aranges ());
			symtab = new DwarfSymbolTable (this, aranges);

			pubnames = read_pubnames ();
			// pubtypes = read_pubtypes ();
		}

		public static bool IsSupported (ExecutableReader bfd)
		{
			if ((bfd.TargetName == "elf32-i386") || (bfd.TargetName == "elf64-x86-64") ||
			    (bfd.TargetName == "elf32-littlearm"))
				return bfd.HasSection (".debug_info");
			else
				return false;
		}

		public TargetInfo TargetInfo {
			get { return target_info; }
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

		protected ISymbolTable get_symtab_at_offset (long offset)
		{
			CompileUnitBlock block = (CompileUnitBlock) compile_unit_hash [offset];

			// This either return the already-read symbol table or acquire the
			// thread lock and read it.
			return block.SymbolTable;
		}

		public override SourceFile[] Sources {
			get {
				SourceFile[] retval = new SourceFile [source_file_hash.Count];
				source_file_hash.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		public Method GetMethod (long handle)
		{
			DwarfTargetMethod method = (DwarfTargetMethod) method_hash [handle];
			if ((method == null) || !method.CheckLoaded ())
				return null;
			return method;
		}

		public override MethodSource[] GetMethods (SourceFile file)
		{
			ArrayList list = new ArrayList ();

			foreach (CompileUnitBlock block in compile_unit_hash.Values) {
				foreach (CompilationUnit comp_unit in block.CompilationUnits) {
					if (comp_unit.DieCompileUnit.SourceFile != file)
						continue;

					foreach (Die child in comp_unit.DieCompileUnit.Subprograms) {
						DieSubprogram subprog = child as DieSubprogram;
						if ((subprog == null) || (subprog.MethodSource == null))
							continue;

						list.Add (subprog.MethodSource);
					}
				}
			}

			MethodSource[] methods = new MethodSource [list.Count];
			list.CopyTo (methods, 0);
			return methods;
		}

		public override MethodSource FindMethod (string name)
		{
			if (pubnames == null)
				return null;

			NameEntry entry = (NameEntry) pubnames [name];
			if (entry == null)
				return null;

			MethodSource source;
			source = (MethodSource) method_source_hash [entry.AbsoluteOffset];
			if (source != null)
				return source;

			CompileUnitBlock block = (CompileUnitBlock) compile_unit_hash [entry.FileOffset];
			return block.GetMethod (entry.AbsoluteOffset);
		}

		protected DwarfMethodSource GetMethodSource (DieSubprogram subprog,
							     int start_row, int end_row)
		{
			DwarfMethodSource source;
			source = (DwarfMethodSource) method_source_hash [subprog.Offset];
			if (source != null)
				return source;

			source = new DwarfMethodSource (subprog, start_row, end_row);
			method_source_hash.Add (subprog.Offset, source);
			return source;
		}

		protected SourceFile GetSourceFile (string filename)
		{
			SourceFile file = (SourceFile) source_file_hash [filename];
			if (file == null) {
				file = new DwarfSourceFile (OS.Process.Session, Module, filename);
				source_file_hash.Add (filename, file);
			}
			return file;
		}

		protected void AddType (DieType type)
		{
			((NativeLanguage) OS.Process.NativeLanguage).AddType (type);
		}

		bool types_initialized;
		public void ReadTypes ()
		{
			if (types_initialized)
				return;

			foreach (CompileUnitBlock block in compile_unit_hash.Values)
				block.ReadSymbolTable ();

			types_initialized = true;
		}

		protected class DwarfSourceFile : SourceFile
		{
			public DwarfSourceFile (DebuggerSession session, Module module,
						string filename)
				: base (session, module, filename)
			{ }

			public override bool IsAutoGenerated {
				get { return false; }
			}

			public override bool CheckModified ()
			{
				return false;
			}
		}

		protected class CompileUnitBlock
		{
			public readonly DwarfReader dwarf;
			public readonly long offset;
			public readonly long length;

			SymbolTableCollection symtabs;
			ArrayList compile_units;
			bool initialized;
			bool symbols_initialized;

			public Method Lookup (TargetAddress address)
			{
				build_symtabs ();
				return symtabs.Lookup (address);
			}

			public CompilationUnit[] CompilationUnits {
				get {
					CompilationUnit[] list = new CompilationUnit [compile_units.Count];
					compile_units.CopyTo (list, 0);
					return list;
				}
			}

			public ISymbolTable SymbolTable {
				get {
					build_symtabs ();
					return symtabs;
				}
			}

			public void ReadSymbolTable ()
			{
				read_children ();
			}

			CompilationUnit get_comp_unit (long offset)
			{
				foreach (CompilationUnit comp_unit in compile_units) {
					long start = comp_unit.RealStartOffset;
					long end = start + comp_unit.UnitLength;

					if ((offset >= start) && (offset < end))
						return comp_unit;
				}

				return null;
			}

			public MethodSource GetMethod (long offset)
			{
				build_symtabs ();
				CompilationUnit comp_unit = get_comp_unit (offset);
				if (comp_unit == null)
					return null;

				DieCompileUnit die = comp_unit.DieCompileUnit;
				DieSubprogram subprog = die.GetSubprogram (offset);
				if (subprog == null)
					return null;

				return subprog.MethodSource;
			}

			void read_children ()
			{
				// If we're already initialized, we don't need to do any locking,
				// so do this check here without locking.
				if (initialized)
					return;

				lock (this) {
					// We need to check this again after we acquired the thread
					// lock to avoid a race condition.
					if (initialized)
						return;

					foreach (CompilationUnit comp_unit in compile_units)
						comp_unit.DieCompileUnit.ReadChildren ();

					initialized = true;
				}
			}

			void build_symtabs ()
			{
				// If we're already initialized, we don't need to do any locking,
				// so do this check here without locking.
				if (symbols_initialized)
					return;

				lock (this) {
					// We need to check this again after we acquired the thread
					// lock to avoid a race condition.
					if (symbols_initialized)
						return;

					symtabs = new SymbolTableCollection ();
					symtabs.Lock ();

					foreach (CompilationUnit comp_unit in compile_units)
						symtabs.AddSymbolTable (comp_unit.SymbolTable);

					symtabs.UnLock ();

					symbols_initialized = true;
				}
			}

			public CompileUnitBlock (DwarfReader dwarf, long start)
			{
				this.dwarf = dwarf;
				this.offset = start;

				DwarfBinaryReader reader = dwarf.DebugInfoReader;
				reader.Position = offset;
				long length_field = reader.ReadInitialLength ();
				long stop = reader.Position + length_field;
				length = stop - offset;
				int version = reader.ReadInt16 ();

				if (version < 2)
					throw new DwarfException (
						dwarf.NativeReader, "Wrong DWARF version: {0}", version);

				reader.ReadOffset ();
				int address_size = reader.ReadByte ();
				reader.Position = offset;

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (
						dwarf.NativeReader, "Unknown address size: {0}",
						address_size);

				compile_units = new ArrayList ();

				while (reader.Position < stop) {
					CompilationUnit comp_unit = new CompilationUnit (dwarf, reader);
					compile_units.Add (comp_unit);
				}
			}

			public override string ToString ()
			{
				return String.Format ("CompileUnitBlock ({0}:{1}:{2})",
						      dwarf.FileName, offset, length);
			}
		}

		public override ISymbolTable SymbolTable {
			get {
				return symtab;
			}
		}

		protected class DwarfSymbolTable : SymbolTable
		{
			DwarfReader dwarf;
			ArrayList ranges;

			public DwarfSymbolTable (DwarfReader dwarf, ArrayList ranges)
			{
				this.dwarf = dwarf;
				this.ranges = ranges;
				this.ranges.Sort ();
			}

			public override bool HasRanges {
				get {
					return true;
				}
			}

			public override ISymbolRange[] SymbolRanges {
				get {
					ISymbolRange[] retval = new ISymbolRange [ranges.Count];
					ranges.CopyTo (retval, 0);
					return retval;
				}
			}

			public override bool HasMethods {
				get {
					return false;
				}
			}

			protected override ArrayList GetMethods ()
			{
				throw new InvalidOperationException ();
			}

			public ArrayList GetAllMethods ()
			{
				ArrayList methods = new ArrayList ();

				foreach (RangeEntry range in ranges) {
					ISymbolTable symtab = dwarf.get_symtab_at_offset (range.FileOffset);

					if (!symtab.IsLoaded || !symtab.HasMethods)
						continue;

					methods.AddRange (symtab.Methods);
				}

				return methods;
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2})", GetType (),
						      dwarf.FileName, ranges.Count);
			}
		}

		private class RangeEntry : SymbolRangeEntry
		{
			public readonly long FileOffset;

			DwarfReader dwarf;

			public RangeEntry (DwarfReader dwarf, long offset,
					   TargetAddress address, long size)
				: base (address, address + size)
			{
				this.dwarf = dwarf;
				this.FileOffset = offset;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return dwarf.get_symtab_at_offset (FileOffset);
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry ({0}:{1}:{2})",
						      StartAddress, EndAddress, FileOffset);
			}
		}

		ArrayList read_aranges ()
		{
			ArrayList ranges = new ArrayList ();

			if (debug_aranges_reader == null)
				return ranges;

			DwarfBinaryReader reader = new DwarfBinaryReader (
				OS, NativeReader, (TargetBlob) debug_aranges_reader.Data, Is64Bit);

			while (!reader.IsEof) {
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long offset = reader.ReadOffset ();
				int address_size = reader.ReadByte ();
				int segment_size = reader.ReadByte ();

				if ((address_size != 4) && (address_size != 8))
					throw new DwarfException (
						NativeReader, "Unknown address size: {0}", address_size);
				if (segment_size != 0)
					throw new DwarfException (
						NativeReader, "Segmented address mode not supported");

				if (version != 2)
					throw new DwarfException (
						NativeReader, "Wrong version in .debug_aranges: {0}",
						version);

				if (AddressSize == 8)
					reader.Position = ((reader.Position+15) >> 4) * 16;
				else
					reader.Position = ((reader.Position+7) >> 3) * 8;

				while (reader.Position < stop) {
					long address = reader.ReadAddress ();
					long size = reader.ReadAddress ();

					if ((address == 0) && (size == 0))
						break;

					TargetAddress taddress = GetAddress (address);
					ranges.Add (new RangeEntry (this, offset, taddress, size));
				}
			}

			return ranges;
		}

		private class NameEntry
		{
			public readonly long FileOffset;
			public readonly long Offset;

			public long AbsoluteOffset {
				get { return FileOffset + Offset; }
			}

			public NameEntry (long file_offset, long offset)
			{
				this.FileOffset = file_offset;
				this.Offset = offset;
			}

			public override string ToString ()
			{
				return String.Format ("NameEntry ({0}:{1})",
						      FileOffset, Offset);
			}
		}

		Hashtable read_pubnames ()
		{
			if (debug_pubnames_reader == null)
				return null;

			DwarfBinaryReader reader = new DwarfBinaryReader (
				OS, NativeReader, (TargetBlob) debug_pubnames_reader.Data, Is64Bit);

			Hashtable names = Hashtable.Synchronized (new Hashtable ());

			while (!reader.IsEof) {
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long debug_offset = reader.ReadOffset ();
				reader.ReadOffset ();

				if (version != 2)
					throw new DwarfException (
						NativeReader, "Wrong version in .debug_pubnames: {0}",
						version);

				while (reader.Position < stop) {
					long offset = reader.ReadInt32 ();
					if (offset == 0)
						break;

					string name = reader.ReadString ();
					if (!names.Contains (name))
						names.Add (name, new NameEntry (debug_offset, offset));
				}
			}

			return names;
		}

		Hashtable read_pubtypes ()
		{
			if (debug_pubtypes_reader == null)
				return null;

			DwarfBinaryReader reader = new DwarfBinaryReader (
				OS, NativeReader, (TargetBlob) debug_pubtypes_reader.Data, Is64Bit);

			Hashtable names = Hashtable.Synchronized (new Hashtable ());

			while (!reader.IsEof) {
				long length = reader.ReadInitialLength ();
				long stop = reader.Position + length;
				int version = reader.ReadInt16 ();
				long debug_offset = reader.ReadOffset ();
				reader.ReadOffset ();

				if (version != 2)
					throw new DwarfException (
						NativeReader, "Wrong version in .debug_pubtypes: {0}",
						version);

				while (reader.Position < stop) {
					long offset = reader.ReadInt32 ();
					if (offset == 0)
						break;

					string name = reader.ReadString ();
					if (!names.Contains (name))
						names.Add (name, new NameEntry (debug_offset, offset));
				}
			}

			return names;
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

		//
		// These properties always create a new DwarfBinaryReader instance, but all these instances
		// share the buffer they're reading from.  A DwarfBinaryReader just contains a reference to
		// the data and the current position - so by creating a new instance each time we start a
		// read operation, reading will be thread-safe.
		//

		public DwarfBinaryReader DebugInfoReader {
			get {
				return new DwarfBinaryReader (
					OS, NativeReader, (TargetBlob) debug_info_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugAbbrevReader {
			get {
				return new DwarfBinaryReader (
					OS, NativeReader, (TargetBlob) debug_abbrev_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugLineReader {
			get {
				return new DwarfBinaryReader (
					OS, NativeReader, (TargetBlob) debug_line_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugStrReader {
			get {
				if (debug_str_reader == null)
					return null;
				return new DwarfBinaryReader (
					OS, NativeReader, (TargetBlob) debug_str_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugLocationReader {
			get {
				return new DwarfBinaryReader (
					OS, NativeReader, (TargetBlob) debug_loc_reader.Data, Is64Bit);
			}
		}

		public DwarfBinaryReader DebugRangesReader {
			get {
				return new DwarfBinaryReader (
					OS, NativeReader, (TargetBlob) debug_ranges_reader.Data, Is64Bit);
			}
		}

		public bool Is64Bit {
			get {
				return is64bit;
			}
		}

		public byte AddressSize {
			get {
				return address_size;
			}
		}

		[Conditional("REAL_DEBUG")]
		static void debug (string message, params object[] args)
		{
			// Console.WriteLine (String.Format (message, args));
		}

		protected enum DwarfLang {
			C89         = 0x0001,
			C           = 0x0002,
			Ada83       = 0x0003,
			C_plus_plus = 0x0004,
			Cobol74     = 0x0005,
			Cobol85     = 0x0006,
			Fortran77   = 0x0007,
			Fortran90   = 0x0008,
			Pascal83    = 0x0009,
			Modula2     = 0x000a,
			None        = 0x8001
		}

		protected enum DwarfTag {
			array_type		= 0x01,
			class_type		= 0x02,
			entry_point             = 0x03,
			enumeration_type	= 0x04,
			formal_parameter	= 0x05,
			imported_declaration    = 0x08,
			label                   = 0x0a,
			lexical_block           = 0x0b,
			member			= 0x0d,
			pointer_type		= 0x0f,
			reference_type          = 0x10,
			compile_unit		= 0x11,
			string_type             = 0x12,
			structure_type		= 0x13,
			subroutine_type		= 0x15,
			typedef			= 0x16,
			union_type		= 0x17,
			unspecified_parameters  = 0x18,
			variant                 = 0x19,
			common_block            = 0x1a,
			comp_dir		= 0x1b,
			inheritance		= 0x1c,
			inlined_subroutine      = 0x1d,
			module                  = 0x1e,
			ptr_to_member_type      = 0x1f,
			set_type                = 0x20,
			subrange_type		= 0x21,
			with_stmt               = 0x22,
			access_declaration	= 0x23,
			base_type		= 0x24,
			catch_block             = 0x25,
			const_type		= 0x26,
			constant                = 0x27,
			enumerator		= 0x28,
			file_type               = 0x29,
			friend                  = 0x2a,
			namelist                = 0x2b,
			namelist_item           = 0x2c,
			packed_type             = 0x2d,
			subprogram		= 0x2e,
			template_type_param     = 0x2f,
			template_value_param    = 0x30,
			thrown_type             = 0x31,
			try_block               = 0x32,
			variant_block           = 0x33,
			variable		= 0x34,
			volatile_type           = 0x35,
			dwarf3_namespace        = 0x39
		}

		protected enum DwarfAttribute {
			sibling                 = 0x01,
			location	        = 0x02,
			name			= 0x03,
			ordering                = 0x09,
			byte_size		= 0x0b,
			bit_offset		= 0x0c,
			bit_size		= 0x0d,
			stmt_list		= 0x10,
			low_pc			= 0x11,
			high_pc			= 0x12,
			language		= 0x13,
			discr                   = 0x15,
			discr_value             = 0x16,
			visibility              = 0x17,
			import                  = 0x18,
			string_length           = 0x19,
			common_reference        = 0x1a,
			comp_dir		= 0x1b,
			const_value		= 0x1c,
			containing_type         = 0x1d,
			default_value           = 0x1e,
			inline                  = 0x20,
			is_optional             = 0x21,
			lower_bound		= 0x22,
			producer		= 0x25,
			prototyped		= 0x27,
			return_addr             = 0x2a,
			start_scope		= 0x2c,
			stride_size             = 0x2e,
			upper_bound		= 0x2f,
			abstract_origin         = 0x31,
			accessibility		= 0x32,
			address_class           = 0x33,
			artificial		= 0x34,
			base_types              = 0x35,
			calling_convention	= 0x36,
			count			= 0x37,
			data_member_location	= 0x38,
			decl_column             = 0x39,
			decl_file               = 0x3a,
			decl_line               = 0x3b,
			declaration             = 0x3c,
			discr_list              = 0x3d,
			encoding		= 0x3e,
			external		= 0x3f,
			frame_base              = 0x40,
			friend                  = 0x41,
			identifier_case         = 0x42,
			macro_info              = 0x43,
			namelist_item           = 0x44,
			priority                = 0x45,
			segment                 = 0x46,
			specification           = 0x47,
			static_link             = 0x48,
			type			= 0x49,
			use_location            = 0x4a,
			variable_parameter      = 0x4b,
			virtuality		= 0x4c,
			vtable_elem_location	= 0x4d,
			entry_pc		= 0x52,
			extension		= 0x54,
			ranges			= 0x55
		}

		protected enum DwarfBaseTypeEncoding {
			address			= 0x01,
			boolean			= 0x02,
			complex_float		= 0x03,
			normal_float		= 0x04,
			signed			= 0x05,
			signed_char		= 0x06,
			unsigned		= 0x07,
			unsigned_char		= 0x08,
			imaginary_float		= 0x09
		}

		protected enum DwarfForm {
			addr			= 0x01,
			block2			= 0x03,
			block4			= 0x04,
			data2			= 0x05,
			data4		        = 0x06,
			data8			= 0x07,
			cstring			= 0x08,
			block			= 0x09,
			block1			= 0x0a,
			data1			= 0x0b,
			flag			= 0x0c,
			sdata			= 0x0d,
			strp			= 0x0e,
			udata			= 0x0f,
			ref_addr		= 0x10,
			ref1			= 0x11,
			ref2			= 0x12,
			ref4			= 0x13,
			ref8			= 0x14,
			ref_udata		= 0x15,
			indirect                = 0x16
		}

		protected enum DwarfInline {
			not_inlined             = 0x00,
			inlined                 = 0x01,
			declared_not_inlined    = 0x02,
			declared_inline         = 0x03
		}

		protected struct LineNumber : IComparable
		{
			public readonly long Offset;
			public readonly int Line;
			public readonly int File;

			public LineNumber (int file, int line, long offset)
			{
				this.File = file;
				this.Line = line;
				this.Offset = offset;
			}

			public int CompareTo (object obj)
			{
				LineNumber entry = (LineNumber) obj;

				if (entry.Offset < Offset)
					return 1;
				else if (entry.Offset > Offset)
					return -1;
				else
					return 0;
			}

			public override string ToString ()
			{
				return String.Format ("LineNumber ({0}:{1}:{2:x})",
						      File, Line, Offset);
			}
		}

		protected class LineNumberEngine : LineNumberTable
		{
			protected DieCompileUnit comp_unit;
			protected DwarfBinaryReader reader;
			protected byte minimum_insn_length;
			protected bool default_is_stmt;
			protected byte opcode_base;
			protected int line_base, line_range;
			protected int const_add_pc_range;
			protected ArrayList source_files;

			long offset;

			long length;
			int version;
			long header_length, data_offset, end_offset;

			int[] standard_opcode_lengths;
			ArrayList include_dirs;
			string compilation_dir;
			ArrayList lines;

			LineNumber[] addresses;

			StatementMachine stm;

			protected class StatementMachine
			{
				public LineNumberEngine engine;			      
				public long st_address;
				public int st_line;
				public int st_file;
				public int st_column;
				public bool is_stmt;
				public bool basic_block;
				public bool end_sequence;
				public bool prologue_end;
				public bool epilogue_begin;
				public long start_offset;
				public long end_offset;

				public int start_file;
				public int end_line;

				public readonly int const_add_pc_range;

				public StatementMachine (LineNumberEngine engine, long offset,
							 long end_offset)
				{
					this.engine = engine;
					this.start_offset = offset;
					this.end_offset = end_offset;
					this.st_address = 0;
					this.st_file = 1;
					this.st_line = 1;
					this.st_column = 0;
					this.is_stmt = this.engine.default_is_stmt;
					this.basic_block = false;
					this.end_sequence = false;
					this.prologue_end = false;
					this.epilogue_begin = false;
					this.start_file = st_file;

					this.const_add_pc_range =
						((0xff - engine.opcode_base) / engine.line_range) *
						engine.minimum_insn_length;
				}

				public void set_end_sequence ()
				{
					engine.debug ("SET END SEQUENCE");

					end_sequence = true;

					end_line = st_line;

					st_address = 0;
					st_file = 1;
					st_line = 1;
					st_column = 0;
					is_stmt = engine.default_is_stmt;
					basic_block = false;
					end_sequence = false;
					prologue_end = false;
					epilogue_begin = false;
					start_file = st_file;
				}
			}

			protected enum StandardOpcode
			{
				extended_op		= 0,
				copy			= 1,
				advance_pc		= 2,
				advance_line		= 3,
				set_file		= 4,
				set_column		= 5,
				negate_stmt		= 6,
				set_basic_block		= 7,
				const_add_pc		= 8,
				fixed_advance_pc	= 9,
				set_prologue_end	= 10,
				set_epilogue_begin	= 11,
				set_isa			= 12
			}

			protected enum ExtendedOpcode
			{
				end_sequence		= 1,
				set_address		= 2,
				define_file		= 3
			}

			protected struct FileEntry {
				public readonly string FileName;
				public readonly int Directory;
				public readonly int LastModificationTime;
				public readonly int Length;

				public readonly SourceFile File;

				public FileEntry (LineNumberEngine engine, DwarfBinaryReader reader)
				{
					FileName = reader.ReadString ();
					Directory = reader.ReadLeb128 ();
					LastModificationTime = reader.ReadLeb128 ();
					Length = reader.ReadLeb128 ();

					string dir_name;
					if (Directory > 0)
						dir_name = (string) engine.include_dirs [Directory - 1];
					else
						dir_name = engine.compilation_dir;

					string full_name;
					if (dir_name != null)
						full_name = Path.Combine (dir_name, FileName);
					else
						full_name = FileName;

					File = engine.comp_unit.dwarf.GetSourceFile (full_name);
				}

				public override string ToString ()
				{
					return String.Format ("FileEntry({0},{1})", FileName, Directory);
				}
			}

			void commit ()
			{
				debug ("COMMIT: {0:x} {1} {2} {3}", stm.st_address, stm.st_line,
				       stm.st_file, stm.start_file);

				lines.Add (new LineNumber (stm.st_file, stm.st_line, stm.st_address));

				stm.basic_block = false;
				stm.prologue_end = false;
				stm.epilogue_begin = false;
			}

			void warning (string message, params object[] args)
			{
				Console.WriteLine (message, args);
			}

			void error (string message, params object[] args)
			{
				throw new DwarfException (comp_unit.dwarf.NativeReader, message, args);
			}

			[Conditional("REAL_DEBUG")]
			void debug (string message, params object[] args)
			{
				// Console.WriteLine (String.Format (message, args));
			}

			public LineNumberEngine (DieCompileUnit comp_unit, long offset,
						 string compilation_dir)
			{
				this.comp_unit = comp_unit;
				this.offset = offset;
				this.reader = comp_unit.dwarf.DebugLineReader;
				this.compilation_dir = compilation_dir;

				debug ("NEW LNE: {0}", offset);

				reader.Position = offset;
				length = reader.ReadInitialLength ();
				end_offset = reader.Position + length;
				version = reader.ReadInt16 ();
				header_length = reader.ReadOffset ();
				data_offset = reader.Position + header_length;
				minimum_insn_length = reader.ReadByte ();
				default_is_stmt = reader.ReadByte () != 0;
				line_base = (sbyte) reader.ReadByte ();
				line_range = reader.ReadByte ();
				opcode_base = reader.ReadByte ();
				standard_opcode_lengths = new int [opcode_base - 1];
				for (int i = 0; i < opcode_base - 1; i++)
					standard_opcode_lengths [i] = reader.ReadByte ();
				include_dirs = new ArrayList ();
				while (reader.PeekByte () != 0)
					include_dirs.Add (reader.ReadString ());
				reader.Position++;
				source_files = new ArrayList ();
				while (reader.PeekByte () != 0)
					source_files.Add (new FileEntry (this, reader));
				reader.Position++;

				const_add_pc_range = ((0xff - opcode_base) / line_range) *
					minimum_insn_length;

				debug ("NEW LNE #1: {0} {1} - {2} {3} {4}",
				       reader.Position, offset, length,
				       data_offset, end_offset);

				lines = new ArrayList ();

				stm = new StatementMachine (this, data_offset, end_offset);
				Read ();

				lines.Sort ();
				addresses = new LineNumber [lines.Count];
				lines.CopyTo (addresses, 0);
			}

			protected void Read ()
			{
				reader.Position = data_offset;

				while (reader.Position < end_offset) {
					byte opcode = reader.ReadByte ();
					debug ("OPCODE: {0:x}", opcode);

					if (opcode == 0)
						do_extended_opcode ();
					else if (opcode < opcode_base)
						do_standard_opcode (opcode);
					else {
						opcode -= opcode_base;

						int addr_inc = (opcode / line_range) * minimum_insn_length;
						int line_inc = line_base + (opcode % line_range);

						debug ("INC: {0:x} {1:x} {2:x} {3:x} - {4} {5}",
						       opcode, opcode_base, addr_inc, line_inc,
						       opcode % line_range, opcode / line_range);

						stm.st_line += line_inc;
						stm.st_address += addr_inc;

						commit ();
					}
				}
			}

			void do_standard_opcode (byte opcode)
			{
				debug ("STANDARD OPCODE: {0:x} {1}", opcode, (StandardOpcode) opcode);

				switch ((StandardOpcode) opcode) {
				case StandardOpcode.copy:
					commit ();
					break;

				case StandardOpcode.advance_pc:
					stm.st_address += minimum_insn_length * reader.ReadLeb128 ();
					break;

				case StandardOpcode.advance_line:
					stm.st_line += reader.ReadSLeb128 ();
					break;

				case StandardOpcode.set_file:
					stm.st_file = reader.ReadLeb128 ();
					debug ("FILE: {0}", stm.st_file);
					break;

				case StandardOpcode.set_column:
					debug ("SET COLUMN");
					stm.st_column = reader.ReadLeb128 ();
					break;

				case StandardOpcode.const_add_pc:
					stm.st_address += const_add_pc_range;
					break;

				case StandardOpcode.set_prologue_end:
					debug ("PROLOGUE END");
					stm.prologue_end = true;
					break;

				case StandardOpcode.set_epilogue_begin:
					debug ("EPILOGUE BEGIN");
					stm.epilogue_begin = true;
					break;

				default:
					error ("Unknown standard opcode {0:x} in line number engine",
					       opcode);
					break;
				}
			}

			void do_extended_opcode ()
			{
				byte size = reader.ReadByte ();
				long end_pos = reader.Position + size;
				byte opcode = reader.ReadByte ();

				debug ("EXTENDED OPCODE: {0:x} {1:x}", size, opcode);

				switch ((ExtendedOpcode) opcode) {
				case ExtendedOpcode.set_address:
					stm.st_address = reader.ReadAddress ();
					debug ("SETTING ADDRESS TO {0:x}", stm.st_address);
					break;

				case ExtendedOpcode.end_sequence:
					stm.set_end_sequence ();
					break;

				default:
					warning ("Unknown extended opcode {0:x} in line number " +
						 "engine at offset {1}", opcode, reader.Position);
					break;
				}

				reader.Position = end_pos;
			}

			public override TargetAddress Lookup (int line)
			{
				for (int i = 0; i < addresses.Length; i++) {
					LineNumber entry = (LineNumber) addresses [i];

					if (entry.Line != line)
						continue;

					return comp_unit.dwarf.GetAddress (entry.Offset);
				}

				return TargetAddress.Null;
			}

			SourceAddress do_lookup (TargetAddress address, int start_pos, int end_pos)
			{
				if (end_pos - start_pos >= 4) {
					int middle_pos = (start_pos + end_pos) / 2;
					LineNumber middle = (LineNumber) addresses [middle_pos];

					TargetAddress maddr = comp_unit.dwarf.GetAddress (middle.Offset);
					if (address < maddr)
						return do_lookup (address, start_pos, middle_pos);
					else
						return do_lookup (address, middle_pos, end_pos);
				}

				TargetAddress next_address;
				if (end_pos == addresses.Length)
					next_address = comp_unit.EndAddress;
				else {
					LineNumber end_line = addresses [end_pos];
					next_address = comp_unit.dwarf.GetAddress (end_line.Offset);
				}

				for (int i = end_pos-1; i >= start_pos; i--) {
					LineNumber line = addresses [i];

					int range = (int) (next_address - address);
					next_address = comp_unit.dwarf.GetAddress (line.Offset);

					if (next_address > address)
						continue;

					int offset = (int) (address - next_address);

					FileEntry file = (FileEntry) source_files [line.File - 1];
					return new SourceAddress (
						file.File, null, line.Line, offset, range);
				}

				return null;
			}

			public override SourceAddress Lookup (TargetAddress address)
			{
				return do_lookup (address, 0, addresses.Length-1);
			}

			public override bool HasMethodBounds {
				get { return false; }
			}

			public override TargetAddress MethodStartAddress {
				get { throw new InvalidOperationException (); }
			}

			public override TargetAddress MethodEndAddress {
				get { throw new InvalidOperationException (); }
			}

			public override void DumpLineNumbers (TextWriter writer)
			{
				writer.WriteLine ("--------");
				writer.WriteLine ("DUMPING DWARF LINE NUMBER TABLE");
				writer.WriteLine ("--------");
				for (int i = 0; i < addresses.Length; i++) {
					LineNumber line = (LineNumber) addresses [i];
					writer.WriteLine ("{0,4} {1,4}  {2:x}", i,
							  line.Line, line.Offset);
				}
				writer.WriteLine ("--------");
			}

			public override string ToString ()
			{
				return String.Format (
					"LineNumberEngine ({0:x},{1:x},{2},{3} - {4},{5},{6},{7})",
					offset, length, version, header_length,
					default_is_stmt, line_base, line_range, opcode_base);
			}
		}

		protected struct AttributeEntry
		{
			DwarfReader dwarf;
			DwarfAttribute attr;
			DwarfForm form;

			public AttributeEntry (DwarfReader dwarf, DwarfAttribute attr, DwarfForm form)
			{
				this.dwarf = dwarf;
				this.attr = attr;
				this.form = form;
			}

			public DwarfAttribute DwarfAttribute {
				get {
					return attr;
				}
			}

			public DwarfForm DwarfForm {
				get {
					return form;
				}
			}

			public Attribute ReadAttribute (long offset)
			{
				return new Attribute (dwarf, offset, attr, form);
			}

			public override string ToString ()
			{
				return String.Format ("AttributeEntry ({0}:{1})", DwarfAttribute,
						      DwarfForm);
			}
		}

		protected class Attribute
		{
			DwarfReader dwarf;
			DwarfAttribute attr;
			DwarfForm form;
			long offset;

			bool has_datasize, has_data;
			int data_size;
			object data;

			public Attribute (DwarfReader dwarf, long offset,
					  DwarfAttribute attr, DwarfForm form)
			{
				this.dwarf = dwarf;
				this.offset = offset;
				this.attr = attr;
				this.form = form;
			}

			public DwarfAttribute DwarfAttribute {
				get {
					return attr;
				}
			}

			public DwarfForm DwarfForm {
				get {
					return form;
				}
			}

			int get_datasize ()
			{
				switch (form) {
				case DwarfForm.ref1:
				case DwarfForm.data1:
				case DwarfForm.flag:
					return 1;

				case DwarfForm.ref2:
				case DwarfForm.data2:
					return 2;

				case DwarfForm.ref4:
				case DwarfForm.data4:
					return 4;

				case DwarfForm.ref8:
				case DwarfForm.data8:
					return 8;

				case DwarfForm.addr:
				case DwarfForm.ref_addr:
					return dwarf.AddressSize;

				case DwarfForm.block1:
					return dwarf.DebugInfoReader.PeekByte (offset) + 1;

				case DwarfForm.block2:
					return dwarf.DebugInfoReader.PeekInt16 (offset) + 2;

				case DwarfForm.block4:
					return dwarf.DebugInfoReader.PeekInt32 (offset) + 4;

				case DwarfForm.block:
				case DwarfForm.ref_udata: {
					int size, size2;
					size2 = dwarf.DebugInfoReader.PeekLeb128 (offset, out size);
					return size + size2;
				}

				case DwarfForm.udata:
				case DwarfForm.sdata: {
					int size;
					dwarf.DebugInfoReader.PeekLeb128 (offset, out size);
					return size;
				}

				case DwarfForm.strp:
					return dwarf.Is64Bit ? 8 : 4;

				case DwarfForm.cstring: {
					string str = dwarf.DebugInfoReader.PeekString (offset);
					return str.Length + 1;
				}

				default:
					throw new DwarfException (
						dwarf.NativeReader, "Unknown DW_FORM: 0x{0:x}",
						(int) form);
				}
			}

			public int DataSize {
				get {
					if (has_datasize)
						return data_size;

					data_size = get_datasize ();
					has_datasize = true;
					return data_size;
				}
			}

			object read_data ()
			{
				DwarfBinaryReader reader = dwarf.DebugInfoReader;

				switch (form) {
				case DwarfForm.flag:
					data_size = 1;
					return reader.PeekByte (offset) != 0;

				case DwarfForm.ref1:
				case DwarfForm.data1:
					data_size = 1;
					return (long) reader.PeekByte (offset);

				case DwarfForm.ref2:
				case DwarfForm.data2:
					data_size = 2;
					return (long) reader.PeekInt16 (offset);

				case DwarfForm.ref4:
				case DwarfForm.data4:
					data_size = 4;
					return (long) reader.PeekInt32 (offset);

				case DwarfForm.ref8:
				case DwarfForm.data8:
					data_size = 8;
					return (long) reader.PeekInt64 (offset);

				case DwarfForm.addr:
					data_size = dwarf.AddressSize;
					return (long) reader.PeekAddress (offset);

				case DwarfForm.cstring: {
					string retval = reader.PeekString (offset);
					data_size = retval.Length + 1;
					return retval;
				}

				case DwarfForm.block1:
					data_size = reader.PeekByte (offset) + 1;
					return reader.PeekBuffer (offset + 1, data_size - 1);

				case DwarfForm.block2:
					data_size = reader.PeekInt16 (offset) + 2;
					return reader.PeekBuffer (offset + 2, data_size - 2);

				case DwarfForm.block4:
					data_size = reader.PeekInt32 (offset) + 4;
					return reader.PeekBuffer (offset + 4, data_size - 4);

				case DwarfForm.block: {
					int size;
					data_size = reader.PeekLeb128 (offset, out size);
					return reader.PeekBuffer (offset + size, data_size);
				}

				case DwarfForm.strp: {
					if (dwarf.DebugStrReader == null)
						throw new DwarfException (
							dwarf.NativeReader, "Got DW_FORM_strp, but " +
							"'.debug_str' section is missing.");
					long str_offset = reader.PeekOffset (offset, out data_size);
					return dwarf.DebugStrReader.PeekString (str_offset);
				}

				case DwarfForm.ref_udata:
				case DwarfForm.udata:
				case DwarfForm.sdata:
					return (long) reader.PeekLeb128 (offset, out data_size);

				case DwarfForm.ref_addr:
					return (long) reader.PeekOffset (offset, out data_size);

				default:
					throw new DwarfException (
						dwarf.NativeReader, "Unknown DW_FORM: 0x{0:x}",
						(int) form);
				}
			}

			public object Data {
				get {
					if (has_data)
						return data;

					data = read_data ();
					has_datasize = true;
					has_data = true;
					return data;
				}
			}

			public override string ToString ()
			{
				return String.Format ("Attribute ({2}({0:x}),{3}({1:x}))",
						      (int) attr, (int) form, attr, form);
			}
		}

		protected class AbbrevEntry
		{
			int abbrev_id;
			DwarfTag tag;
			bool has_children;

			public readonly ArrayList Attributes;

			public AbbrevEntry (DwarfReader dwarf, DwarfBinaryReader reader)
			{
				abbrev_id = reader.ReadLeb128 ();
				tag = (DwarfTag) reader.ReadLeb128 ();
				has_children = reader.ReadByte () != 0;

				Attributes = new ArrayList ();

				do {
					int attr = reader.ReadLeb128 ();
					int form = reader.ReadLeb128 ();

					if ((attr == 0) && (form == 0))
						break;

					Attributes.Add (new AttributeEntry (
						dwarf, (DwarfAttribute) attr, (DwarfForm) form));
				} while (true);
			}

			public int ID {
				get {
					return abbrev_id;
				}
			}

			public DwarfTag Tag {
				get {
					return tag;
				}
			}

			public bool HasChildren {
				get {
					return has_children;
				}
			}

			public override string ToString ()
			{
				return String.Format ("AbbrevEntry ({0},{1},{2})",
						      abbrev_id, tag, has_children);
			}
		}

		// <summary>
		// Base class for all DIE's - The DWARF Debugging Information Entry.
		// </summary>
		protected class Die
		{
			public readonly CompilationUnit comp_unit;
			public readonly DwarfReader dwarf;
			public readonly AbbrevEntry abbrev;
			public readonly long Offset;
			public readonly long ChildrenOffset;

			protected virtual int ReadAttributes (DwarfBinaryReader reader)
			{
				int total_size = 0;

				foreach (AttributeEntry entry in abbrev.Attributes) {
					Attribute attribute = entry.ReadAttribute (Offset + total_size);
					ProcessAttribute (attribute);
					total_size += attribute.DataSize;
				}

				return total_size;
			}

			protected virtual void ProcessAttribute (Attribute attribute)
			{ }

			ArrayList children;

			protected virtual ArrayList ReadChildren (DwarfBinaryReader reader)
			{
				if (!abbrev.HasChildren)
					return null;

				children = new ArrayList ();

				while (reader.PeekByte () != 0) {
					Die child = CreateDie (reader, comp_unit);
					child.ReadChildren (reader);
					children.Add (child);
				}

				reader.Position++;
				return children;
			}

			public ArrayList Children {
				get {
					if (!abbrev.HasChildren)
						return null;

					if (children == null) {
						DwarfBinaryReader reader = dwarf.DebugInfoReader;

						long old_pos = reader.Position;
						reader.Position = ChildrenOffset;
						children = ReadChildren (reader);
						reader.Position = old_pos;
					}

					return children;
				}
			}

			protected Die (DwarfBinaryReader reader, CompilationUnit comp_unit,
				       AbbrevEntry abbrev)
			{
				this.comp_unit = comp_unit;
				this.dwarf = comp_unit.DwarfReader;
				this.abbrev = abbrev;

				Offset = reader.Position;
				ChildrenOffset = Offset + ReadAttributes (reader);
				reader.Position = ChildrenOffset;

				debug ("NEW DIE: {0} {1} {2} {3}", GetType (),
				       abbrev, Offset, ChildrenOffset);
			}

			public Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit)
			{
				long offset = reader.Position;
				int abbrev_id = reader.ReadLeb128 ();
				AbbrevEntry abbrev = comp_unit [abbrev_id];

				return CreateDie (reader, comp_unit, offset, abbrev);
			}

			public static DieCompileUnit CreateDieCompileUnit (DwarfBinaryReader reader,
									   CompilationUnit comp_unit)
			{
				int abbrev_id = reader.ReadLeb128 ();
				AbbrevEntry abbrev = comp_unit [abbrev_id];

				return new DieCompileUnit (reader, comp_unit, abbrev);
			}

			protected virtual Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							 long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.compile_unit:
					throw new InternalError ();

				case DwarfTag.subprogram:
					return new DieSubprogram (reader, comp_unit, offset, abbrev);

				case DwarfTag.base_type:
					return new DieBaseType (reader, comp_unit, offset, abbrev);

				case DwarfTag.const_type:
					return new DieConstType (reader, comp_unit, offset, abbrev);

				case DwarfTag.pointer_type:
					return new DiePointerType (reader, comp_unit, offset, abbrev);

				case DwarfTag.class_type: // for now just treat classes and structs the same.
				case DwarfTag.structure_type:
					return new DieStructureType (reader, comp_unit, offset, abbrev, false);

				case DwarfTag.union_type:
					return new DieStructureType (reader, comp_unit, offset, abbrev, true);

				case DwarfTag.array_type:
					return new DieArrayType (reader, comp_unit, offset, abbrev);

				case DwarfTag.subrange_type:
					return new DieSubrangeType (reader, comp_unit, abbrev);

				case DwarfTag.enumeration_type:
					return new DieEnumerationType (reader, comp_unit, offset, abbrev);

				case DwarfTag.enumerator:
					return new DieEnumerator (reader, comp_unit, abbrev);

				case DwarfTag.typedef:
					return new DieTypedef (reader, comp_unit, offset, abbrev);

				case DwarfTag.subroutine_type:
					return new DieSubroutineType (reader, comp_unit, offset, abbrev);

				case DwarfTag.member:
					return new DieMember (reader, comp_unit, abbrev);

				case DwarfTag.inlined_subroutine:
					debug ("INLINED SUBROUTINE!");
					return new DieSubprogram (reader, comp_unit, offset, abbrev);

				case DwarfTag.dwarf3_namespace:
					return new DieNamespace (reader, comp_unit, offset, abbrev);

				default:
					return new Die (reader, comp_unit, abbrev);
				}
			}

			public DieCompileUnit DieCompileUnit {
				get { return comp_unit.DieCompileUnit; }
			}
		}

		// <summary>
		// The Debugging Information Entry corresponding to compilation units.
		// </summary>
		// <remarks>
		// From the DWARF spec: <em>A compilation unit typically
		// represents the text and data contributed to an executable
		// by a single relocatable object file.  It may be derived
		// from several source files, including pre-processed
		// ``include files.''</em>
		// </remarks>
		protected class DieCompileUnit : Die, ISymbolContainer
		{
			public DieCompileUnit (DwarfBinaryReader reader, CompilationUnit comp_unit,
					       AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				if ((start_pc != null) && (end_pc != null))
					is_continuous = true;

				string file_name;
				if (comp_dir != null)
					file_name = String.Concat (
						comp_dir, Path.DirectorySeparatorChar, name);
				else
					file_name = name;
				file = dwarf.GetSourceFile (file_name);
			}

			long? start_pc, end_pc, entry_pc;
			string name;
			string comp_dir;
			bool is_continuous;
			DwarfLang language;
			SourceFile file;
			CompileUnitSymbolTable symtab;
			ArrayList children;
			LineNumberEngine engine;
			bool children_initialized;

			protected long line_offset;
			protected bool has_lines;

			void read_children ()
			{
				if (children != null)
					return;

				children = new ArrayList ();

				if (abbrev.HasChildren) {
					foreach (Die child in Children) {
						DieSubprogram subprog = child as DieSubprogram;
						if ((subprog == null) || !subprog.IsContinuous)
							continue;

						children.Add (subprog);
					}
				}
			}

			void initialize_children ()
			{
				if (children_initialized)
					return;

				read_children ();

				children.Sort ();

				if (has_lines) {
					engine = new LineNumberEngine (this, line_offset, comp_dir);

					foreach (DieSubprogram subprog in children)
						subprog.SetEngine (engine);
				}

				children_initialized = true;
			}

			public void ReadChildren ()
			{
				read_children ();
			}

			void read_symtab ()
			{
				if ((symtab != null) || !dwarf.NativeReader.IsLoaded)
					return;

				initialize_children ();
				symtab = new CompileUnitSymbolTable (this);
			}

			protected LineNumberEngine Engine {
				get {
					initialize_children ();
					return engine;
				}
			}

			public ArrayList Subprograms {
				get {
					initialize_children ();
					return children;
				}
			}

			public DieSubprogram GetSubprogram (long offset)
			{
				initialize_children ();
				foreach (DieSubprogram subprog in children) {
					if (subprog.RealOffset == offset)
						return subprog;
				}

				return null;
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.low_pc:
					start_pc = (long) attribute.Data;
					break;

				case DwarfAttribute.high_pc:
					end_pc = (long) attribute.Data;
					break;

				case DwarfAttribute.entry_pc:
					entry_pc = (long) attribute.Data;
					break;

				case DwarfAttribute.stmt_list:
					line_offset = (long) attribute.Data;
					has_lines = true;
					break;

				case DwarfAttribute.comp_dir:
					comp_dir = (string) attribute.Data;
					break;

				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;

				case DwarfAttribute.language:
#if FIXME
					language = (DwarfLang)attribute.Data;
					Console.WriteLine ("DieCompileUnit {0} has language {1}", name, language);
#endif
					break;
				}
			}

			public string ImageFile {
				get {
					return dwarf.FileName;
				}
			}

			public string CompilationDirectory {
				get {
					return comp_dir;
				}
			}

			public bool IsContinuous {
				get {
					return is_continuous;
				}
			}

			public long LineNumberOffset {
				get {
					if (!has_lines)
						return -1;

					return line_offset;
				}
			}

			public ISymbolTable SymbolTable {
				get {
					read_symtab ();
					return symtab;
				}
			}

			public TargetAddress StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return dwarf.GetAddress ((long) start_pc);
				}
			}

			public TargetAddress EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return dwarf.GetAddress ((long) end_pc);
				}
			}

			public TargetAddress BaseAddress {
				get {
					if (entry_pc != null)
						return dwarf.GetAddress ((long) entry_pc);
					else if (start_pc != null)
						return dwarf.GetAddress ((long) start_pc);
					else
						return TargetAddress.Null;
				}
			}

			public SourceFile SourceFile {
				get {
					return file;
				}
			}

			protected class CompileUnitSymbolTable : SymbolTable
			{
				DieCompileUnit comp_unit_die;

				public CompileUnitSymbolTable (DieCompileUnit comp_unit_die)
					: base (comp_unit_die)
				{
					this.comp_unit_die = comp_unit_die;
				}

				public override bool HasRanges {
					get {
						return false;
					}
				}

				public override ISymbolRange[] SymbolRanges {
					get {
						throw new InvalidOperationException ();
					}
				}

				public override bool HasMethods {
					get {
						return true;
					}
				}

				protected override ArrayList GetMethods ()
				{
					ArrayList methods = new ArrayList ();

					ArrayList list = comp_unit_die.Subprograms;

					foreach (DieSubprogram subprog in list)
						methods.Add (subprog.Method);

					return methods;
				}
			}
		}

		// <summary>
		// The Debugging Information Entry corresponding to a
		// subprogram, which in most languages means a method or
		// function or subroutine.
		// </summary>
		protected class DieSubprogram : Die, IComparable, ISymbolContainer
		{
			long abstract_origin, specification;
			long real_offset, start_pc, end_pc;
			bool is_continuous, resolved;
			string full_name, name;
			DwarfTargetMethod method;
			LineNumberEngine engine;
			ArrayList param_dies, local_dies;
			TargetVariable this_var;
			TargetVariable[] parameters, locals;
			DwarfLocation frame_base;

			protected override void ProcessAttribute (Attribute attribute)
			{
				debug ("SUBPROG PROCESS ATTRIBUTE: {0} {1}", Offset, attribute);

				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.low_pc:
					start_pc = (long) attribute.Data;
					debug ("{0}: start_pc = {1:x}", Offset, start_pc);
					break;

				case DwarfAttribute.high_pc:
					end_pc = (long) attribute.Data;
					debug ("{0}: end_pc = {1:x}", Offset, end_pc);
					break;

				case DwarfAttribute.name:
					name = (string) attribute.Data;
					debug ("{0}: name = {1}", Offset, name);
					break;

				case DwarfAttribute.frame_base:
					frame_base = new DwarfLocation (this, attribute, false);
					break;

				case DwarfAttribute.decl_file:
					debug ("{0}: decl_file = {1}",
					       Offset, (long) attribute.Data);
					break;

				case DwarfAttribute.decl_line:
					debug ("{0}: decl_line = {1}",
					       Offset, (long) attribute.Data);
					break;

				case DwarfAttribute.inline:
					debug ("{0}: inline = {1}",
					       Offset, (DwarfInline) (long)attribute.Data);
					break;

				case DwarfAttribute.abstract_origin:
					abstract_origin = (long) attribute.Data;
					debug ("{0} ABSTRACT ORIGIN: {1}", Offset, abstract_origin);
					break;

				case DwarfAttribute.specification:
					specification = (long) attribute.Data;
					debug ("{0} SPECIFICATION: {1}", Offset, specification);
					break;
				}
			}

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.formal_parameter:
					return new DieFormalParameter (this, reader, comp_unit, abbrev);

				case DwarfTag.variable:
					return new DieVariable (this, reader, comp_unit, abbrev);

				case DwarfTag.lexical_block:
					return new DieLexicalBlock (this, reader, comp_unit, abbrev);

				default:
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			public DieSubprogram (DwarfBinaryReader reader, CompilationUnit comp_unit,
					      long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.real_offset = offset;
				if ((start_pc != 0) && (end_pc != 0))
					is_continuous = true;

				comp_unit.AddSubprogram (offset, this);
			}

			public SourceFile SourceFile {
				get {
					return DieCompileUnit.SourceFile;
				}
			}

			public MethodSource MethodSource {
				get {
					if (method == null)
						return null;

					return method.MethodSource;
				}
			}

			public DwarfLocation FrameBase {
				get {
					return frame_base;
				}
			}

			public string ImageFile {
				get {
					return dwarf.NativeReader.FileName;
				}
			}

			public string Name {
				get {
					return full_name ?? name ?? "<unknown>";
				}
			}

			public bool IsContinuous {
				get {
					return is_continuous;
				}
			}

			TargetAddress ISymbolContainer.StartAddress {
				get {
					return dwarf.GetAddress (StartAddress);
				}
			}

			TargetAddress ISymbolContainer.EndAddress {
				get {
					return dwarf.GetAddress (EndAddress);
				}
			}

			public long StartAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return start_pc;
				}
			}

			public long EndAddress {
				get {
					if (!is_continuous)
						throw new InvalidOperationException ();

					return end_pc;
				}
			}

			internal long RealOffset {
				get {
					return real_offset;
				}
			}

			public int CompareTo (object obj)
			{
				DieSubprogram die = (DieSubprogram) obj;

				if (die.start_pc < start_pc)
					return 1;
				else if (die.start_pc > start_pc)
					return -1;
				else
					return 0;
			}

			public Method Method {
				get {
					if (method == null)
						throw new InvalidOperationException ();
					return method;
				}
			}

			public LineNumberEngine Engine {
				get {
					if (engine == null)
						throw new InvalidOperationException ();
					return engine;
				}
			}

			public void SetEngine (LineNumberEngine engine)
			{
				this.engine = engine;
				method = new DwarfTargetMethod (this, engine);
			}

			public void AddParameter (DieMethodVariable variable)
			{
				if (param_dies == null)
					param_dies = new ArrayList ();

				param_dies.Add (variable);
			}

			public void AddLocal (DieMethodVariable variable)
			{
				if (local_dies == null)
					local_dies = new ArrayList ();

				local_dies.Add (variable);
			}

			TargetVariable[] resolve_variables (ArrayList variables)
			{
				if (variables == null)
					return new TargetVariable [0];

				ArrayList list = new ArrayList ();
				foreach (DieMethodVariable variable in variables) {
					TargetVariable resolved = variable.Variable;
					if (resolved != null)
						list.Add (resolved);
				}

				TargetVariable[] retval = new TargetVariable [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}

			public bool HasThis {
				get {
					return this_var != null;
				}
			}

			public TargetVariable This {
				get {
					if (this_var == null)
						throw new InvalidOperationException ();

					return this_var;
				}
			}

			public TargetVariable[] Parameters {
				get {
					return parameters;
				}
			}

			public TargetVariable[] Locals {
				get {
					return locals;
				}
			}

			public void Resolve ()
			{
				if (resolved)
					return;

				try {
					DoResolve ();
					resolved = true;
				} catch (Exception ex) {
					debug ("{0} FUCK: {1}", Offset, ex);
					return;
				}
			}

			void DoResolveSpecification (DieSubprogram specification)
			{
				specification.Resolve ();

				if ((name == null) && (specification.name != null))
					name = specification.name;
				if ((local_dies == null) && (specification.local_dies != null))
					local_dies = specification.local_dies;
				if ((param_dies == null) && (specification.param_dies != null))
					param_dies = specification.param_dies;
			}

			void DoResolve ()
			{
				if (abstract_origin != 0) {
					DieSubprogram aorigin = comp_unit.GetSubprogram (abstract_origin);
					if (aorigin == null)
						throw new InternalError ();

					DoResolveSpecification (aorigin);
				}

				if (specification != 0) {
					DieSubprogram ssubprog = comp_unit.GetSubprogram (specification);
					if (ssubprog == null)
						throw new InternalError ();

					DoResolveSpecification (ssubprog);
				}

				locals = resolve_variables (local_dies);
				parameters = resolve_variables (param_dies);

				if (param_dies != null) {
					DieMethodVariable first_var = (DieMethodVariable) param_dies [0];
					if ((first_var.IsArtificial != null) && (first_var.Name == "this"))
						this_var = first_var.Variable;
				}

				debug ("{0} resolve: {1} {2} {3} {4}", Offset, param_dies != null, local_dies != null,
				       name != null, this_var != null);

				if ((param_dies != null) && (name != null)) {
					StringBuilder sb = new StringBuilder ();
					if (this_var != null) {
						sb.Append (this_var.Type.Name);
						sb.Append ("::");
					}
					sb.Append (name);
					sb.Append ("(");
					bool first = true;
					for (int i = 0; i < param_dies.Count; i++) {
						DieMethodVariable the_param = (DieMethodVariable) param_dies [i];
						if (!first)
							sb.Append (", ");
						if (the_param.Type == null)
							continue;
						sb.Append (the_param.Type.Name);
						if (the_param.Name != null) {
							sb.Append (" ");
							sb.Append (the_param.Name);
						}
						first = false;
					}
					sb.Append (")");
					full_name = sb.ToString ();
				}
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3}:{4:x}:{5:x})", GetType (),
						      name ?? "<unknown>", Offset, RealOffset,
						      start_pc, end_pc);
			}
		}

		protected class DwarfMethodSource : MethodSource
		{
			DieSubprogram subprog;
			int start_row;
			int end_row;

			public DwarfMethodSource (DieSubprogram subprog, int start_row, int end_row)
			{
				this.subprog = subprog;
				this.start_row = start_row;
				this.end_row = end_row;
			}

			internal long Handle {
				get { return subprog.Offset; }
			}

			public override Module Module {
				get { return subprog.dwarf.Module; }
			}

			public override string Name {
				get { return subprog.Name; }
			}

			public override bool IsManaged {
				get { return false; }
			}

			public override bool IsDynamic {
				get { return false; }
			}

			public override TargetClassType DeclaringType {
				get { throw new InvalidOperationException (); }
			}

			public override TargetFunctionType Function {
				get { throw new InvalidOperationException (); }
			}

			public override bool HasSourceFile {
				get { return subprog.SourceFile != null; }
			}

			public override SourceFile SourceFile {
				get { return subprog.SourceFile; }
			}

			public override bool HasSourceBuffer {
				get { return false; }
			}

			public override SourceBuffer SourceBuffer {
				get { throw new InvalidOperationException (); }
			}

			public override int StartRow {
				get { return start_row; }
			}

			public override int EndRow {
				get { return end_row; }
			}

			public override Method NativeMethod {
				get {return subprog.Method; }
			}

			public override string[] GetNamespaces ()
			{
				return null;
			}
		}

		protected class DwarfTargetMethod : Method
		{
			LineNumberEngine engine;
			DieSubprogram subprog;
			DwarfMethodSource source;
			int start_row, end_row;

			public DwarfTargetMethod (DieSubprogram subprog, LineNumberEngine engine)
				: base (subprog.Name, subprog.ImageFile, subprog.dwarf.Module)
			{
				this.subprog = subprog;
				this.engine = engine;

				CheckLoaded ();
				Name = subprog.Name;
			}

			public DwarfReader DwarfReader {
				get { return subprog.dwarf; }
			}

			public override object MethodHandle {
				get { return this; }
			}

			public override int Domain {
				get { return -1; }
			}

			public override TargetClassType GetDeclaringType (Thread target)
			{
				if (!subprog.HasThis)
					return null;

				var type = subprog.This.Type;

				var ptype = type as TargetPointerType;
				if (ptype != null)
					type = ptype.StaticType;

				return (TargetClassType) type;
			}

			public override bool HasThis {
				get { return subprog.HasThis; }
			}

			public override TargetVariable GetThis (Thread target)
			{
				return subprog.This;
			}

			public override TargetVariable[] GetParameters (Thread target)
			{
				return subprog.Parameters;
			}

			public override TargetVariable[] GetLocalVariables (Thread target)
			{
				return subprog.Locals;
			}

			public override bool IsWrapper {
				get { return false; }
			}

			public override bool IsCompilerGenerated {
				get { return false; }
			}

			public override bool HasSource {
				get {
					read_line_numbers ();
					return source != null;
				}
			}

			public override MethodSource MethodSource {
				get {
					read_line_numbers ();
					return source;
				}
			}

			public override string[] GetNamespaces ()
			{
				return null;
			}

			public int StartRow {
				get { return start_row; }
			}

			public int EndRow {
				get { return end_row; }
			}

			void read_line_numbers ()
			{
				if (source != null)
					return;

				subprog.Resolve ();

				SourceAddress start = engine.Lookup (StartAddress);
				SourceAddress end = engine.Lookup (EndAddress);

				if ((start == null) || (end == null))
					return;

				start_row = start.Row;
				end_row = end.Row;

				SetMethodBounds (StartAddress + start.LineRange,
						 EndAddress - end.LineOffset);

				source = subprog.dwarf.GetMethodSource (subprog, start_row, end_row);

				subprog.dwarf.method_hash.Add (source.Handle, this);
			}

			public bool CheckLoaded ()
			{
				if (!subprog.dwarf.NativeReader.IsLoaded)
					return false;

				ISymbolContainer sc = (ISymbolContainer) subprog;
				if (sc.IsContinuous)
					SetAddresses (sc.StartAddress, sc.EndAddress);

				SetLineNumbers (engine);

				read_line_numbers ();

				return true;
			}

			internal override MethodSource GetTrampoline (TargetMemoryAccess memory,
								      TargetAddress address)
			{
				return null;
			}
		}

		protected class CompilationUnit
		{
			DwarfReader dwarf;
			long real_start_offset, start_offset, unit_length, abbrev_offset;
			int version, address_size;
			DieCompileUnit comp_unit_die;
			Hashtable abbrevs;
			Hashtable types;
			Hashtable subprogs;
			Dictionary<long,DieNamespace> namespaces;

			public CompilationUnit (DwarfReader dwarf, DwarfBinaryReader reader)
			{
				this.dwarf = dwarf;

				real_start_offset = reader.Position;
				unit_length = reader.ReadInitialLength ();
				start_offset = reader.Position;
				version = reader.ReadInt16 ();
				abbrev_offset = reader.ReadOffset ();
				address_size = reader.ReadByte ();

				if (version < 2)
					throw new DwarfException (
						dwarf.NativeReader, "Wrong DWARF version: {0}",
						version);

				abbrevs = new Hashtable ();
				types = new Hashtable ();
				subprogs = new Hashtable ();
				namespaces = new Dictionary<long,DieNamespace> ();

				DwarfBinaryReader abbrev_reader = dwarf.DebugAbbrevReader;

				abbrev_reader.Position = abbrev_offset;
				while (abbrev_reader.PeekByte () != 0) {
					AbbrevEntry entry = new AbbrevEntry (dwarf, abbrev_reader);
					abbrevs.Add (entry.ID, entry);
				}

				comp_unit_die = Die.CreateDieCompileUnit (reader, this);

				reader.Position = start_offset + unit_length;
			}

			public DwarfReader DwarfReader {
				get {
					return dwarf;
				}
			}

			public DieCompileUnit DieCompileUnit {
				get {
					return comp_unit_die;
				}
			}

			public ISymbolTable SymbolTable {
				get {
					return DieCompileUnit.SymbolTable;
				}
			}

			internal long RealStartOffset {
				get {
					return real_start_offset;
				}
			}

			internal long UnitLength {
				get {
					return unit_length;
				}
			}

			internal string CurrentNamespace {
				get; set;
			}

			public AbbrevEntry this [int abbrev_id] {
				get {
					if (abbrevs.Contains (abbrev_id))
						return (AbbrevEntry) abbrevs [abbrev_id];

					throw new DwarfException (
						dwarf.NativeReader, "{0} does not contain an " +
						"abbreviation entry {1}", this, abbrev_id);
				}
			}

			public void AddType (long offset, DieType type)
			{
				types.Add (offset, type);
			}

			public void AddSubprogram (long offset, DieSubprogram subprog)
			{
				subprogs.Add (offset, subprog);
			}

			public void AddNamespace (long offset, DieNamespace ns)
			{
				namespaces.Add (offset, ns);
			}

			public DieType GetType (long offset)
			{
				return (DieType) types [real_start_offset + offset];
			}

			public DieSubprogram GetSubprogram (long offset)
			{
				return (DieSubprogram) subprogs [real_start_offset + offset];
			}

			public DieNamespace GetNamespace (long offset)
			{
				return namespaces [real_start_offset + offset];
			}

			public override string ToString ()
			{
				return String.Format ("CompilationUnit ({0},{1},{2} - {3},{4},{5})",
						      dwarf.Is64Bit ? "64-bit" : "32-bit", version,
						      address_size, real_start_offset,
						      unit_length, abbrev_offset);
			}
		}

		protected class DwarfLocation
		{
			CompilationUnit comp_unit;
			DwarfLocation frame_base;
			byte[] location_block;
			long loclist_offset;
			bool is_byref;

			public DwarfLocation (DieSubprogram subprog, Attribute attribute, bool is_byref)
				: this (subprog.comp_unit, subprog.FrameBase, attribute, is_byref)
			{ }

			public DwarfLocation (CompilationUnit comp_unit, DwarfLocation frame_base,
					      Attribute attribute, bool is_byref)
			{
				this.comp_unit = comp_unit;
				this.frame_base = frame_base;
				this.is_byref = is_byref;

				if (comp_unit == null)
					throw new InternalError ();

				switch (attribute.DwarfForm) {
				case DwarfForm.block1:
					location_block = (byte []) attribute.Data;
					break;
				case DwarfForm.data1:
				case DwarfForm.data2:
				case DwarfForm.data4:
				case DwarfForm.data8:
					loclist_offset = (long) attribute.Data;
					break;
				default:
					throw new InternalError  ();
				}
			}

			TargetLocation GetLocation (StackFrame frame, TargetMemoryAccess memory,
						    byte[] data)
			{
				TargetBinaryReader locreader = new TargetBinaryReader (
					data, comp_unit.DwarfReader.TargetInfo);

				byte opcode = locreader.ReadByte ();
				bool is_regoffset;
				int reg, off;

				if ((opcode >= 0x50) && (opcode <= 0x6f)) { // DW_OP_reg
					reg = opcode - 0x50 + 3;
					off = 0;
					is_regoffset = false;
				} else if ((opcode >= 0x70) && (opcode <= 0x8f)) { // DW_OP_breg
					reg = opcode - 0x70 + 3;
					off = locreader.ReadSLeb128 ();
					is_regoffset = true;
				} else if (opcode == 0x90) { // DW_OP_regx
					reg = locreader.ReadLeb128 () + 3;
					off = 0;
					is_regoffset = false;
				} else if (opcode == 0x91) { // DW_OP_fbreg
					off = locreader.ReadSLeb128 ();

					if (frame_base != null) {
						TargetLocation rloc = new RelativeTargetLocation (
							frame_base.GetLocation (frame, memory), off);
						if (is_byref)
							return new DereferencedTargetLocation (rloc);
						else
							return rloc;
					} else {
						is_regoffset = true;
						reg = 2;
					}
				} else if (opcode == 0x92) { // DW_OP_bregx
					reg = locreader.ReadLeb128 () + 3;
					off = locreader.ReadSLeb128 ();
					is_regoffset = true;
				} else if (opcode == 0x03) { // DW_OP_addr
					TargetAddress addr = new TargetAddress (
						memory.AddressDomain, locreader.ReadAddress ());
					TargetLocation aloc = new AbsoluteTargetLocation (addr);
					if (is_byref)
						return new DereferencedTargetLocation (aloc);
					else
						return aloc;
				} else {
					Console.WriteLine ("UNKNOWN OPCODE: {0:x}", opcode);
					return null;
				}

				reg = comp_unit.DwarfReader.OS.Process.Architecture.DwarfFrameRegisterMap [reg];

				MonoVariableLocation loc = MonoVariableLocation.Create (
					memory, is_regoffset, frame.Registers [reg],
					off, is_byref);

				if (!locreader.IsEof) {
					Console.WriteLine ("LOCREADER NOT AT EOF!");
					return null;
				}

				return loc;
			}

			public TargetLocation GetLocation (StackFrame frame, TargetMemoryAccess memory)
			{
				if (location_block != null)
					return GetLocation (frame, memory, location_block);

				DwarfBinaryReader reader = comp_unit.DwarfReader.DebugLocationReader;
				reader.Position = loclist_offset;

				TargetAddress address = frame.TargetAddress;
				TargetAddress base_address = comp_unit.DieCompileUnit.BaseAddress;


				while (true) {
					long start = reader.ReadAddress ();
					long end = reader.ReadAddress ();

					if (start == -1) {
						Console.WriteLine ("BASE SELECTION: {0:x}", end);
						base_address = comp_unit.DwarfReader.GetAddress (end);
						continue;
					}

					if ((start == 0) && (end == 0))
						break;

					int size = reader.ReadInt16 ();
					byte[] data = reader.ReadBuffer (size);

					if ((address < base_address+start) || (address >= base_address+end))
						continue;

					return GetLocation (frame, memory, data);
				}

				return null;
			}

			public TargetLocation GetLocation (TargetLocation location)
			{
				if (location_block == null)
					throw new NotImplementedException ();

				TargetBinaryReader locreader = new TargetBinaryReader (
					location_block, comp_unit.DwarfReader.TargetInfo);

				byte opcode = locreader.ReadByte ();

				if (opcode == 0x23) // DW_OP_plus_uconst
					location = new RelativeTargetLocation (location, locreader.ReadLeb128 ());
				else {
					Console.WriteLine ("UNKNOWN OPCODE: {0:x}", opcode);
					return null;
				}

				if (!locreader.IsEof) {
					Console.WriteLine ("LOCREADER NOT AT EOF!");
					return null;
				}

				return location;
			}
		}

		protected class DwarfTargetVariable : TargetVariable
		{
			readonly string name;
			readonly TargetType type;
			readonly DwarfLocation location;
			readonly DieLexicalBlock lexical_block;

			public DwarfTargetVariable (DieSubprogram subprog, string name, TargetType type,
						    DwarfLocation location, DieLexicalBlock lexical_block)
			{
				this.name = name;
				this.type = type;
				this.location = location;
				this.lexical_block = lexical_block;
			}

			public override string Name {
				get { return name; }
			}

			public override TargetType Type {
				get { return type; }
			}

			public override bool IsInScope (TargetAddress address)
			{
				if (lexical_block != null)
					return lexical_block.IsInRange (address);

				return true;
			}

			public override bool IsAlive (TargetAddress address)
			{
				return true;
			}

			public bool CheckValid (StackFrame frame)
			{
				return true;
			}

			internal override TargetObject GetObject (StackFrame frame,
								  TargetMemoryAccess target)
			{
				TargetLocation loc = location.GetLocation (frame, target);
				if (loc == null)
					return null;

				return type.GetObject (target, loc);
			}

			public override string PrintLocation (StackFrame frame)
			{
				return (string) frame.Thread.ThreadServant.DoTargetAccess (
					delegate (TargetMemoryAccess memory) {
						TargetLocation loc = location.GetLocation (frame, memory);
						if (loc == null)
							return null;

						return loc.Print ();
				});
			}

			public override bool CanWrite {
				get { return type.Kind == TargetObjectKind.Fundamental; }
			}

			public override void SetObject (StackFrame frame, TargetObject obj)
			{
				if (obj.Type != Type)
					throw new InvalidOperationException ();

				TargetFundamentalObject var_object = GetObject (frame) as TargetFundamentalObject;
				if (var_object == null)
					return;

				var_object.SetObject (frame.Thread, obj);
			}

			public override string ToString ()
			{
				return String.Format ("NativeVariable [{0}:{1}]", Name, Type);
			}
		}

		protected class DieNamespace : Die
		{
			long offset;
			string name;
			DieNamespace extension;

			public DieNamespace (DwarfBinaryReader reader, CompilationUnit comp_unit,
					     long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.offset = offset;
				comp_unit.AddNamespace (offset, this);

				if (extension != null) {
					if (extension.name != null) {
						if (name != null)
							name = extension.name + "::" + name;
						else
							name = extension.name;
					}
				}
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				debug ("NAMESPACE ATTRIBUTE: {0}", attribute);
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				case DwarfAttribute.extension:
					debug ("NAMESPACE EXTENSION: {0}", attribute);
					extension = comp_unit.GetNamespace ((long) attribute.Data);
					break;
				}
			}

			protected override ArrayList ReadChildren (DwarfBinaryReader reader)
			{
				string old_ns = comp_unit.CurrentNamespace;
				if (name == null)
					comp_unit.CurrentNamespace = null;
				else if (comp_unit.CurrentNamespace != null)
					comp_unit.CurrentNamespace = comp_unit.CurrentNamespace + "::" + name;
				else
					comp_unit.CurrentNamespace = name;

				try {
					debug ("NS CHILDREN: {0} -> {1}", name, comp_unit.CurrentNamespace);
					return base.ReadChildren (reader);
					debug ("NS CHILDREN DONE: {0}", name);
				} finally {
					comp_unit.CurrentNamespace = old_ns;
				}
			}
		}

		protected abstract class DieType : Die, ITypeEntry
		{
			string name;
			protected long offset;
			DieType specification;
			bool resolved, type_created;
			protected readonly Language language;
			TargetType type;

			public DieType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.offset = offset;
				this.language = reader.OperatingSystem.Process.NativeLanguage;
				comp_unit.AddType (offset, this);

				if (specification != null) {
					if ((name == null) && (specification.name != null))
						name = specification.Name;
				}

				if (name != null) {
					if (comp_unit.CurrentNamespace != null)
						name = comp_unit.CurrentNamespace + "::" + name;
					comp_unit.DwarfReader.AddType (this);
				}
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				case DwarfAttribute.specification:
					specification = comp_unit.GetType ((long) attribute.Data);
					break;
				}
			}

			protected DieType GetReference (long offset)
			{
				return comp_unit.GetType (offset);
			}

			public virtual bool IsComplete {
				get { return true; }
			}

			public TargetType ResolveType ()
			{
				if (resolved)
					return type;

				type = CreateType ();
				resolved = true;

				if (type == null) {
					debug ("RESOLVE TYPE FAILED: {0}", this);
					type_created = true;
					return null;
				}

				if (!type_created) {
					type_created = true;
					PopulateType ();
				}

				return type;
			}

			protected abstract TargetType CreateType ();

			protected virtual void PopulateType ()
			{ }

			public bool HasType {
				get {
					if (!resolved)
						throw new InvalidOperationException ();
					return type != null;
				}
			}
			
			public TargetType Type {
				get {
					if (!HasType)
						return (TargetType) language.VoidType;
					else
						return type;
				}
			}

			public string Name {
				get {
					return name;
				}
			}

			internal TargetType GetAlias (string name)
			{
				if (this.name == null) {
					this.name = name;
					return Type;
				} else
					return new NativeTypeAlias (language, name, this.name, Type);
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3})", GetType (),
						      offset, Name, type);
			}
		}

		// <summary>
		// Debugging Information Entry corresponding to base types.
		// </summary>
		// <remarks>
		// From the DWARF spec: <em>A base type is a data type that
		// is not defined in terms of other data types.  Each
		// programming language has a set of base types that are
		// considered to be built into that language. </em>
		// </remarks>
		protected class DieBaseType : DieType
		{
			int byte_size;
			int encoding;
			FundamentalKind kind;

			public DieBaseType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					    long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.encoding:
					encoding = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override TargetType CreateType ()
			{
				kind = GetMonoType (
					(DwarfBaseTypeEncoding) encoding, byte_size);

				if (kind == FundamentalKind.Unknown)
					return new NativeOpaqueType (language, Name, byte_size);

				return new NativeFundamentalType (language, Name, kind, byte_size);
			}

			protected FundamentalKind GetMonoType (DwarfBaseTypeEncoding encoding,
							       int byte_size)
			{
				switch (encoding) {
				case DwarfBaseTypeEncoding.signed:
					if (byte_size == 1)
						return FundamentalKind.SByte;
					else if (byte_size == 2)
						return FundamentalKind.Int16;
					else if (byte_size <= 4)
						return FundamentalKind.Int32;
					else if (byte_size <= 8)
						return FundamentalKind.Int64;
					break;

				case DwarfBaseTypeEncoding.unsigned:
					if (byte_size == 1)
						return FundamentalKind.Byte;
					else if (byte_size == 2)
						return FundamentalKind.UInt16;
					else if (byte_size <= 4)
						return FundamentalKind.UInt32;
					else if (byte_size <= 8)
						return FundamentalKind.UInt64;
					break;

				case DwarfBaseTypeEncoding.signed_char:
					if (byte_size == 1)
						return FundamentalKind.SByte;
					else
						return FundamentalKind.Char;

				case DwarfBaseTypeEncoding.unsigned_char:
					if (byte_size == 1)
						return FundamentalKind.Byte;
					else
						return FundamentalKind.Char;

				case DwarfBaseTypeEncoding.normal_float:
					if (byte_size <= 4)
						return FundamentalKind.Single;
					else if (byte_size <= 8)
						return FundamentalKind.Double;
					break;

				case DwarfBaseTypeEncoding.boolean:
					if (byte_size == 1)
						return FundamentalKind.Boolean;
					break;
				}

				return FundamentalKind.Unknown;
			}
		}

		protected class DiePointerType : DieType
		{
			int byte_size;
			long type_offset;
			DieType reference;

			public DiePointerType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					       long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override TargetType CreateType ()
			{
				TargetType ref_type;
				if (type_offset == 0)
					ref_type = language.VoidType;
				else {
					reference = GetReference (type_offset);
					if (reference == null) {
						Console.WriteLine (
							"UNKNOWN POINTER: {0}",
							comp_unit.RealStartOffset + type_offset);
						return null;
					}

					ref_type = reference.ResolveType ();
					if (ref_type == null)
						return null;
				}

				TargetFundamentalType fundamental = ref_type as TargetFundamentalType;
				if ((fundamental != null) && (fundamental.Name == "char"))
					return new NativeStringType (language, byte_size);

				TargetFunctionType func = ref_type as TargetFunctionType;
				if (func != null)
					return new NativeFunctionPointer (language, func);

				string name;
				if (Name != null)
					name = Name;
				else
					name = NativePointerType.MakePointerName (ref_type);

				return new NativePointerType (language, name, ref_type, byte_size);
			}
		}

		protected class DieSubrangeType : Die
		{
			public int? UpperBound {
				get;
				private set;
			}

			public int? LowerBound {
				get;
				private set;
			}

			public DieSubrangeType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{  }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.upper_bound:
					UpperBound = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.lower_bound:
					LowerBound = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.count:
					LowerBound = 0;
				  	UpperBound = (int) (long) attribute.Data;
					break;
				}
			}
		}

		protected class DieArrayType : DieType
		{
			int ordering;
			int byte_size;
			int stride_size;
			long type_offset;
			DieType reference;

			public DieArrayType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					     long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{  }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.stride_size:
					stride_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				case DwarfAttribute.ordering:
					ordering = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public int Ordering {
				get { return ordering; }
			}

			public int ByteSize {
				get { return byte_size; }
			}

			public int StrideSize {
				get { return stride_size; }
			}

			protected override TargetType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null) {
					Console.WriteLine (
						"UNKNOWN POINTER: {0}",
						comp_unit.RealStartOffset + type_offset);
					return null;
				}

				TargetType ref_type = reference.ResolveType ();
				if (ref_type == null)
					return null;

#if false
				/* not sure we want this */
				if (ref_type.TypeHandle == typeof (char))
					return new NativeStringType (byte_size);
#endif

				string name;
				if (Name != null)
					name = Name;
				else
					name = String.Format ("{0} []", ref_type.Name);

				List<DieSubrangeType> list = new List<DieSubrangeType> ();

				foreach (Die die in Children) {
					DieSubrangeType subrange = die as DieSubrangeType;
					if (subrange != null)
						list.Add (subrange);
				}

				TargetArrayBounds bounds;
				if (list.Count == 0)
					bounds = TargetArrayBounds.MakeUnboundArray ();
				else if ((list.Count == 1) && (list [0].UpperBound == null))
					bounds = TargetArrayBounds.MakeUnboundArray ();
				else if ((list.Count == 1) && ((list [0].LowerBound ?? 0) == 0))
					bounds = TargetArrayBounds.MakeSimpleArray ((int) list [0].UpperBound + 1);
				else {
					int[] lower = new int [list.Count];
					int[] upper = new int [list.Count];

					for (int i = 0; i < list.Count; i++) {
						lower [i] = list [i].LowerBound ?? 0;
						upper [i] = (int) list [i].UpperBound;
					}

					bounds = TargetArrayBounds.MakeMultiArray (lower, upper);
				}

				return new NativeArrayType (
					language, name, ref_type, bounds, byte_size);
			}
		}

		protected class DieEnumerator : Die
		{
			string name;
			int const_value;

			public DieEnumerator (DwarfBinaryReader reader, CompilationUnit comp_unit,
					      AbbrevEntry abbrev)
			  : base (reader, comp_unit, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;
				case DwarfAttribute.const_value:
					const_value = (int) (long) attribute.Data;
					break;
				}
			}

			public string Name {
				get {
					return name;
				}
			}

			public int ConstValue {
				get {
					return const_value;
				}
			}
		}

		protected class DieEnumerationType : DieType
		{
			int byte_size;

			public DieEnumerationType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						   long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{  }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.specification:
					Console.WriteLine ("ugh, specification");
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override TargetType CreateType ()
			{
				int num_elements = 0;
				string name;

				foreach (Die d in Children)
					if (d is DieEnumerator) num_elements ++;

				if (Name != null)
					name = Name;
				else
					name = "<unknown enum>";

				string[] names = new string [num_elements];
				int[] values = new int [num_elements];

				int i = 0;
				foreach (Die d in Children) {
					DieEnumerator e = d as DieEnumerator;
					if (e == null) continue;

					names[i] = e.Name;
					values[i] = e.ConstValue;
					i++;
				}

				return new NativeEnumType (language, name, byte_size, names, values);
			}
		}

		protected class DieConstType : DieType
		{
			long type_offset;
			DieType reference;

			public DieConstType (DwarfBinaryReader reader, CompilationUnit comp_unit,
					     long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override TargetType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null)
					return null;

				return reference.ResolveType ();
			}
		}

		// <summary>
		// Debugging Information Entry corresponding to arbitrary
		// types that are assigned names by the programmer.
		// </summary>
		protected class DieTypedef : DieType
		{
			long type_offset;
			DieType reference;
			NativeTypeAlias type;

			public DieTypedef (DwarfBinaryReader reader, CompilationUnit comp_unit,
					   long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public override bool IsComplete {
				get { return false; }
			}

			protected override TargetType CreateType ()
			{
				reference = GetReference (type_offset);
				if (reference == null)
					return null;

				type = new NativeTypeAlias (language, Name, reference.Name);
				return type;
			}

			protected override void PopulateType ()
			{
				type.SetTargetType (reference.ResolveType ());
			}
		}

		// <summary>
		// Debugging Information Entry corresponding to
		// inheritance information.
		// </summary>
		// <remarks>
		// From the DWARF spec: <em>The class type of
		// structure type entry that describes a derived class
		// or structure owns debugging information entries
		// describing each of the classes or structures it is
		// derived from, ordered as they were in the source
		// program.</em>
		// </remarks>
		protected class DieInheritance : Die
		{
			long type_offset;
			DwarfLocation data_member_location;
			DieType reference;

			DwarfBaseInfo base_info;
			NativeStructType base_type;
			bool resolved;

			public DieInheritance (DwarfBinaryReader reader,
					       CompilationUnit comp_unit,
					       AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				case DwarfAttribute.data_member_location:
					data_member_location = new DwarfLocation (comp_unit, null, attribute, false);
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public long TypeOffset {
				get { return type_offset; }
			}

			public bool HasDataMember {
				get { return data_member_location != null; }
			}

			public DwarfLocation DataMember {
				get { return data_member_location; }
			}

			public DwarfBaseInfo BaseInfo {
				get {
					Resolve ();
					return base_info;
				}
			}

			public bool Resolve ()
			{
				if (resolved)
					return base_info != null;

				try {
					DoResolve ();
					return base_info != null;
				} finally {
					resolved = true;
				}
			}

			void DoResolve ()
			{
				DieType type = comp_unit.GetType (type_offset);
				if (type == null)
					return;

				base_type = type.ResolveType () as NativeStructType;
				if (base_type == null)
					return;

				base_info = new DwarfBaseInfo (this, base_type);
			}
		}

		protected class DwarfBaseInfo : NativeBaseInfo
		{
			public readonly DieInheritance Inheritance;

			public DwarfBaseInfo (DieInheritance inheritance, NativeStructType base_type)
				: base (base_type)
			{
				this.Inheritance = inheritance;
			}

			public override TargetLocation GetBaseLocation (TargetMemoryAccess memory,
									TargetLocation location)
			{
				return Inheritance.DataMember.GetLocation (location);
			}
		}

		protected class DieStructureType : DieType
		{
			int byte_size;
			public readonly bool IsUnion;

			public DieStructureType (DwarfBinaryReader reader,
						 CompilationUnit comp_unit, long offset,
						 AbbrevEntry abbrev, bool is_union)
				: base (reader, comp_unit, offset, abbrev)
			{
				this.IsUnion = is_union;
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.byte_size:
					byte_size = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.inheritance:
					return new DieInheritance (reader, comp_unit, abbrev);

				default:
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			ArrayList members;
			NativeFieldInfo[] fields;
			NativeStructType type;
			DwarfBaseInfo base_info;

			public override bool IsComplete {
				get { return abbrev.HasChildren; }
			}

			protected override TargetType CreateType ()
			{
				if (!abbrev.HasChildren)
					return new NativeTypeAlias (language, Name, Name);

				foreach (Die child in Children) {
					DieInheritance inheritance = child as DieInheritance;
					if ((inheritance == null) || !inheritance.HasDataMember)
						continue;
					if (!inheritance.Resolve ())
						continue;
					debug ("INHERITANCE: {0} {1}", inheritance, inheritance.BaseInfo);
					base_info = inheritance.BaseInfo;
					break;
				}

				type = new NativeStructType (language, Name, byte_size, base_info);
				return type;
			}

			protected override void PopulateType ()
			{
				if (!abbrev.HasChildren)
					return;

				ArrayList list = new ArrayList ();

				foreach (Die child in Children) {
					DieMember member = child as DieMember;
					if ((member == null) || !member.Resolve (this))
						continue;

					TargetType mtype = member.Type;
					if (mtype == null)
						mtype = (TargetType) language.VoidType;

					NativeFieldInfo field;
					if (member.IsBitfield)
						field = new NativeFieldInfo (
							mtype, member.Name, list.Count,
							member.DataOffset, member.BitOffset,
							member.BitSize);
					else
						field = new NativeFieldInfo (
							mtype, member.Name, list.Count,
							member.DataOffset);
					list.Add (field);
				}

				fields = new NativeFieldInfo [list.Count];
				list.CopyTo (fields);

				type.SetFields (fields);
			}
		}

		protected class DieSubroutineType : DieType
		{
			long type_offset;
			bool prototyped;
			DieType return_type;
			NativeFunctionType function_type;

			public DieSubroutineType (DwarfBinaryReader reader, CompilationUnit comp_unit,
						  long offset, AbbrevEntry abbrev)
				: base (reader, comp_unit, offset, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				case DwarfAttribute.prototyped:
					prototyped = (bool) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.formal_parameter:
					return new DieFormalParameter (null, reader, comp_unit, abbrev);

				default:
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			protected override TargetType CreateType ()
			{
				if (type_offset != 0) {
					return_type = GetReference (type_offset);
					if (return_type == null)
						return null;
				}

				function_type = new NativeFunctionType (language);
				return function_type;
			}

			protected override void PopulateType ()
			{
				TargetType ret_type = null;
				if (return_type != null)
					ret_type = return_type.ResolveType ();
				if (ret_type == null)
					ret_type = (TargetType) language.VoidType;

				List<TargetType> param_list = new List<TargetType> ();

				if (abbrev.HasChildren) {
					foreach (Die child in Children) {
						DieFormalParameter formal = child as DieFormalParameter;
						if (formal == null) {
							Console.WriteLine ("UNKNOWN DIE IN PROTOTYPE: {0}",
									   child);
							continue;
						}

						param_list.Add (formal.Type);
					}
				}

				function_type.SetPrototype (ret_type, param_list.ToArray ());
			}
		}

		protected class DieLexicalBlock : Die
		{
			DieSubprogram subprog;
			long? ranges_offset;

			public DieLexicalBlock (DieSubprogram subprog, DwarfBinaryReader reader,
						CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.subprog = subprog;
			}

			protected override Die CreateDie (DwarfBinaryReader reader, CompilationUnit comp_unit,
							  long offset, AbbrevEntry abbrev)
			{
				switch (abbrev.Tag) {
				case DwarfTag.formal_parameter:
					return new DieFormalParameter (subprog, reader, comp_unit, abbrev);

				case DwarfTag.variable:
					return new DieVariable (subprog, reader, comp_unit, abbrev, this);

				case DwarfTag.lexical_block:
					return new DieLexicalBlock (subprog, reader, comp_unit, abbrev);

				default:
					debug ("LEXICAL BLOCK ({0}): unknown die: {1}", Offset, abbrev.Tag);
					return base.CreateDie (reader, comp_unit, offset, abbrev);
				}
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.ranges:
					ranges_offset = (long) attribute.Data;
					break;

				default:
					debug ("UNKNOWN ATTRIBUTE: {0} {1}", this, attribute);
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public bool IsInRange (TargetAddress address)
			{
				if (ranges_offset == null)
					return true;

				DwarfBinaryReader reader = comp_unit.DwarfReader.DebugRangesReader;
				reader.Position = (long) ranges_offset;

				TargetAddress base_address = comp_unit.DieCompileUnit.BaseAddress;

				while (true) {
					long start = reader.ReadAddress ();
					long end = reader.ReadAddress ();

					if (start == -1) {
						Console.WriteLine ("BASE SELECTION: {0:x}", end);
						base_address = comp_unit.DwarfReader.GetAddress (end);
						continue;
					}

					if ((start == 0) && (end == 0))
						break;

					if ((address < base_address+start) || (address >= base_address+end))
						continue;

					return true;
				}

				return false;
			}
		}

		protected abstract class DieVariableBase : Die
		{
			string name;
			long type_offset;

			public DieVariableBase (DwarfBinaryReader reader, CompilationUnit comp_unit,
						AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{ }

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.name:
					name = (string) attribute.Data;
					break;

				case DwarfAttribute.type:
					type_offset = (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			public string Name {
				get {
					return name;
				}
			}

			public long TypeOffset {
				get {
					return type_offset;
				}
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2})", GetType ().Name, type_offset, name);
			}
		}

		protected abstract class DieMethodVariable : DieVariableBase
		{
			public DieMethodVariable (DieSubprogram subprog, DwarfBinaryReader reader,
						  CompilationUnit comp_unit, AbbrevEntry abbrev,
						  DieLexicalBlock lexical_block, bool is_local)
				: base (reader, comp_unit, abbrev)
			{
				this.subprog = subprog;
				this.lexical_block = lexical_block;

				if (subprog != null) {
					if (is_local)
						subprog.AddLocal (this);
					else
						subprog.AddParameter (this);
				}
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.location:
					location_attr = attribute;
					break;

				case DwarfAttribute.artificial:
					artificial = (bool) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			Attribute location_attr;
			DwarfTargetVariable variable;
			DieSubprogram subprog;
			TargetType type;
			DieLexicalBlock lexical_block;
			bool artificial;
			bool resolved;

			protected bool DoResolveType ()
			{
				if (type != null)
					return true;

				if (TypeOffset == 0)
					return false;
				DieType reference = comp_unit.GetType (TypeOffset);
				if (reference == null)
					return false;

				type = reference.ResolveType ();
				return type != null;
			}

			protected bool DoResolve ()
			{
				if (!DoResolveType ())
					return false;

				if ((Name == null) || (subprog == null) || (location_attr == null))
					return false;

				DwarfLocation location = new DwarfLocation (
					subprog, location_attr, type.IsByRef);
				variable = new DwarfTargetVariable (
					subprog, Name, type, location, lexical_block);
				return true;
			}

			public TargetType Type {
				get {
					DoResolveType ();
					return type;
				}
			}

			public DwarfTargetVariable Variable {
				get {
					if (!resolved) {
						DoResolve ();
						resolved = true;
					}

					return variable;
				}
			}

			public bool IsArtificial {
				get {
					return artificial;
				}
			}
		}

		protected class DieFormalParameter : DieMethodVariable
		{
			public DieFormalParameter (DieSubprogram parent, DwarfBinaryReader reader,
						   CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (parent, reader, comp_unit, abbrev, null, false)
			{ }
		}

		protected class DieVariable : DieMethodVariable
		{
			public DieVariable (DieSubprogram parent, DwarfBinaryReader reader,
					    CompilationUnit comp_unit, AbbrevEntry abbrev)
				: base (parent, reader, comp_unit, abbrev, null, true)
			{ }

			public DieVariable (DieSubprogram parent, DwarfBinaryReader reader,
					    CompilationUnit comp_unit, AbbrevEntry abbrev,
					    DieLexicalBlock lexical)
				: base (parent, reader, comp_unit, abbrev, lexical, true)
			{ }
		}

		protected class DieMember : DieVariableBase
		{
			public DieMember (DwarfBinaryReader reader, CompilationUnit comp_unit,
					  AbbrevEntry abbrev)
				: base (reader, comp_unit, abbrev)
			{
				this.target_info = reader.TargetMemoryInfo;
			}

			protected override void ProcessAttribute (Attribute attribute)
			{
				switch (attribute.DwarfAttribute) {
				case DwarfAttribute.data_member_location:
					location = (byte []) attribute.Data;
					break;

				case DwarfAttribute.bit_offset:
					bit_offset = (int) (long) attribute.Data;
					break;

				case DwarfAttribute.bit_size:
					bit_size = (int) (long) attribute.Data;
					break;

				default:
					base.ProcessAttribute (attribute);
					break;
				}
			}

			byte[] location;
			bool resolved, ok;
			DieType type_die;
			TargetType type;
			TargetMemoryInfo target_info;
			int bit_offset, bit_size;
			int offset;

			public bool Resolve (DieStructureType die_struct)
			{
				if (resolved)
					return ok;

				type = ResolveType (die_struct);
				resolved = true;
				ok = type != null;
				return ok;
			}

			public bool IsBitfield {
				get { return bit_size != 0; }
			}

			public int BitOffset {
				get { return bit_offset; }
			}

			public int BitSize {
				get { return bit_size; }
			}

			bool read_location ()
			{
				TargetBinaryReader locreader = new TargetBinaryReader (
					location, target_info);

				switch (locreader.ReadByte ()) {
				case 0x23: // DW_OP_plus_uconstant
					offset = locreader.ReadLeb128 ();
					return locreader.IsEof;

				default:
					return false;
				}
			}

			protected TargetType ResolveType (DieStructureType die_struct)
			{
				if ((TypeOffset == 0) || (Name == null))
					return null;

				if ((location == null) && !die_struct.IsUnion)
					return null;

				type_die = comp_unit.GetType (TypeOffset);
				if (type_die == null)
					return null;

				if ((location != null) && !read_location ())
					return null;

				type = type_die.ResolveType ();
				return type;
			}

			public TargetType Type {
				get {
					if (!resolved)
						throw new InvalidOperationException ();

					return type;
				}
			}

			public int DataOffset {
				get {
					if (!resolved)
						throw new InvalidOperationException ();

					return offset;
				}
			}
		}
	}
}
