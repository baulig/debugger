using System;
using System.IO;
using System.Text;
using R = System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Collections;
using System.Threading;
using C = Mono.CSharp.Debugger;
using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Architecture;

namespace Mono.Debugger.Languages.CSharp
{
	internal delegate void BreakpointHandler (Inferior inferior, TargetAddress address,
						  object user_data);

	internal class VariableInfo
	{
		public readonly int Index;
		public readonly int Offset;
		public readonly int Size;
		public readonly AddressMode Mode;
		public readonly bool HasLivenessInfo;
		public readonly int BeginLiveness;
		public readonly int EndLiveness;

		internal enum AddressMode : long
		{
			Register	= 0,
			RegOffset	= 0x10000000,
			TwoRegisters	= 0x20000000
		}

		const long AddressModeFlags = 0xf0000000;

		public static int StructSize {
			get {
				return 20;
			}
		}

		// FIXME: Map mono/arch/x86/x86-codegen.h registers to
		//        debugger/arch/IArchitectureI386.cs registers.
		int[] register_map = { (int)I386Register.EAX, (int)I386Register.ECX,
				       (int)I386Register.EDX, (int)I386Register.EBX,
				       (int)I386Register.ESP, (int)I386Register.EBP,
				       (int)I386Register.ESI, (int)I386Register.EDI };

		public VariableInfo (TargetBinaryReader reader)
		{
			Index = reader.ReadInt32 ();
			Offset = reader.ReadInt32 ();
			Size = reader.ReadInt32 ();
			BeginLiveness = reader.ReadInt32 ();
			EndLiveness = reader.ReadInt32 ();

			Mode = (AddressMode) (Index & AddressModeFlags);
			Index = (int) ((long) Index & ~AddressModeFlags);

			if (Mode == AddressMode.Register)
				Index = register_map [Index];

			HasLivenessInfo = (BeginLiveness != 0) && (EndLiveness != 0);
		}

		public override string ToString ()
		{
			return String.Format ("[VariableInfo {0}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}]",
					      Mode, Index, Offset, Size, BeginLiveness, EndLiveness);
		}
	}

	internal struct JitLineNumberEntry
	{
		public readonly int Offset;
		public readonly int Address;

		public JitLineNumberEntry (TargetBinaryReader reader)
		{
			Offset = reader.ReadInt32 ();
			Address = reader.ReadInt32 ();
		}

		public override string ToString ()
		{
			return String.Format ("[JitLineNumberEntry {0}:{1:x}]", Offset, Address);
		}
	}

	internal struct JitLexicalBlockEntry
	{
		public readonly int StartAddress;
		public readonly int EndAddress;

		public JitLexicalBlockEntry (TargetBinaryReader reader)
		{
			StartAddress = reader.ReadInt32 ();
			EndAddress = reader.ReadInt32 ();
		}

		public override string ToString ()
		{
			return String.Format ("[JitLexicalBlockEntry {0:x}:{1:x}]", StartAddress, EndAddress);
		}
	}

	internal class MethodAddress
	{
		public readonly TargetAddress StartAddress;
		public readonly TargetAddress EndAddress;
		public readonly TargetAddress MethodStartAddress;
		public readonly TargetAddress MethodEndAddress;
		public readonly TargetAddress WrapperAddress;
		public readonly JitLineNumberEntry[] LineNumbers;
		public readonly JitLexicalBlockEntry[] LexicalBlocks;
		public readonly VariableInfo ThisVariableInfo;
		public readonly VariableInfo[] ParamVariableInfo;
		public readonly VariableInfo[] LocalVariableInfo;
		public readonly bool HasThis;
		public readonly int ClassTypeInfoOffset;
		public readonly int[] ParamTypeInfoOffsets;
		public readonly int[] LocalTypeInfoOffsets;

		protected TargetAddress ReadAddress (TargetBinaryReader reader, AddressDomain domain)
		{
			long address = reader.ReadAddress ();
			if (address != 0)
				return new TargetAddress (domain, address);
			else
				return TargetAddress.Null;
		}

		public MethodAddress (C.MethodEntry entry, TargetBinaryReader reader, AddressDomain domain)
		{
			reader.Position = 4;
			StartAddress = ReadAddress (reader, domain);
			EndAddress = ReadAddress (reader, domain);
			MethodStartAddress = ReadAddress (reader, domain);
			MethodEndAddress = ReadAddress (reader, domain);
			WrapperAddress = ReadAddress (reader, domain);

			HasThis = reader.ReadInt32 () != 0;
			int variables_offset = reader.ReadInt32 ();
			int type_table_offset = reader.ReadInt32 ();

			int num_line_numbers = reader.ReadInt32 ();
			LineNumbers = new JitLineNumberEntry [num_line_numbers];

			int line_number_offset = reader.ReadInt32 ();

			int lexical_block_table_offset = reader.ReadInt32 ();

			Report.Debug (DebugFlags.MethodAddress,
				      "METHOD ADDRESS: {0} {1} {2} {3} {4} {5} {6} {7}",
				      StartAddress, EndAddress, MethodStartAddress, MethodEndAddress,
				      WrapperAddress, variables_offset, type_table_offset, num_line_numbers);

			if (num_line_numbers > 0) {
				reader.Position = line_number_offset;
				for (int i = 0; i < num_line_numbers; i++)
					LineNumbers [i] = new JitLineNumberEntry (reader);
				MethodStartAddress = StartAddress + LineNumbers [0].Address;
				MethodEndAddress = StartAddress + LineNumbers [num_line_numbers-1].Address;
			}

			reader.Position = variables_offset;
			if (HasThis)
				ThisVariableInfo = new VariableInfo (reader);

			ParamVariableInfo = new VariableInfo [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamVariableInfo [i] = new VariableInfo (reader);

			LocalVariableInfo = new VariableInfo [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalVariableInfo [i] = new VariableInfo (reader);

			reader.Position = type_table_offset;
			ClassTypeInfoOffset = reader.ReadInt32 ();

			ParamTypeInfoOffsets = new int [entry.NumParameters];
			for (int i = 0; i < entry.NumParameters; i++)
				ParamTypeInfoOffsets [i] = reader.ReadInt32 ();

			LocalTypeInfoOffsets = new int [entry.NumLocals];
			for (int i = 0; i < entry.NumLocals; i++)
				LocalTypeInfoOffsets [i] = reader.ReadInt32 ();

			reader.Position = lexical_block_table_offset;
			LexicalBlocks = new JitLexicalBlockEntry [entry.LexicalBlocks.Length];
			for (int i = 0; i < LexicalBlocks.Length; i++)
				LexicalBlocks [i] = new JitLexicalBlockEntry (reader);
		}

		public override string ToString ()
		{
			return String.Format ("[Address {0:x}:{1:x}:{3:x}:{4:x},{5:x},{2}]",
					      StartAddress, EndAddress, LineNumbers.Length,
					      MethodStartAddress, MethodEndAddress, WrapperAddress);
		}
	}

	internal class MonoBuiltinTypes
	{
		public readonly MonoObjectType ObjectType;
		public readonly MonoFundamentalType ByteType;
		public readonly MonoOpaqueType VoidType;
		public readonly MonoFundamentalType BooleanType;
		public readonly MonoFundamentalType SByteType;
		public readonly MonoFundamentalType Int16Type;
		public readonly MonoFundamentalType UInt16Type;
		public readonly MonoFundamentalType Int32Type;
		public readonly MonoFundamentalType UInt32Type;
		public readonly MonoFundamentalType IntType;
		public readonly MonoFundamentalType UIntType;
		public readonly MonoFundamentalType Int64Type;
		public readonly MonoFundamentalType UInt64Type;
		public readonly MonoFundamentalType SingleType;
		public readonly MonoFundamentalType DoubleType;
		public readonly MonoFundamentalType CharType;
		public readonly MonoStringType StringType;
		public readonly MonoClass EnumType;
		public readonly MonoClass ArrayType;
		public readonly MonoClass ExceptionType;

		protected readonly int TypeInfoSize;

		public MonoBuiltinTypes (MonoSymbolTable table, ITargetMemoryAccess memory, TargetAddress address,
					 MonoSymbolFile corlib)
		{
			int size = memory.ReadInteger (address);
			ITargetMemoryReader reader = memory.ReadMemory (address, size);

			reader.Offset = reader.TargetIntegerSize;
			TypeInfoSize = reader.ReadInteger ();
			ObjectType = (MonoObjectType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			ByteType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			VoidType = (MonoOpaqueType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			BooleanType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			SByteType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			Int16Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UInt16Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			Int32Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UInt32Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			IntType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UIntType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			Int64Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			UInt64Type = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			SingleType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			DoubleType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			CharType = (MonoFundamentalType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			StringType = (MonoStringType) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			EnumType = (MonoClass) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			ArrayType = (MonoClass) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
			ExceptionType = (MonoClass) BuiltinTypeInfo.GetType (this, memory, reader.ReadGlobalAddress (), corlib);
		}

		private struct BuiltinTypeInfo
		{
			public readonly TargetAddress Klass;
			public readonly TargetAddress ClassInfoAddress;
			public readonly int TypeInfo;
			public readonly int ClassInfo;
			public readonly TargetAddress TypeData;

			public BuiltinTypeInfo (ITargetMemoryReader reader)
			{
				Klass = reader.ReadGlobalAddress ();
				ClassInfoAddress = reader.ReadGlobalAddress ();
				TypeInfo = reader.ReadInteger ();
				ClassInfo = reader.ReadInteger ();
				TypeData = reader.ReadGlobalAddress ();
			}

			public override string ToString ()
			{
				return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})", GetType (), Klass,
						      ClassInfoAddress, TypeInfo, ClassInfo, TypeData);
			}

			public static MonoType GetType (MonoBuiltinTypes builtin, ITargetMemoryAccess memory,
							TargetAddress address, MonoSymbolFile corlib)
			{
				int size = builtin.TypeInfoSize;
				ITargetMemoryReader reader = memory.ReadMemory (address, size);

				BuiltinTypeInfo info = new BuiltinTypeInfo (reader);
				return corlib.GetType (memory, info.ClassInfoAddress);
			}
		}
	}

	// <summary>
	//   Holds all the symbol tables from the target's JIT.
	// </summary>
	internal class MonoSymbolTable : Module, ISymbolFile, ISimpleSymbolTable,
		ILanguage, IDisposable
	{
		public const int  MinDynamicVersion = 43;
		public const int  MaxDynamicVersion = 44;
		public const long DynamicMagic   = 0x7aff65af4253d427;

		internal ArrayList SymbolFiles;
		public readonly MonoCSharpLanguageBackend CSharpLanguage;
		public readonly DebuggerBackend Backend;
		public readonly ITargetMemoryInfo TargetInfo;
		// ArrayList ranges;
		Hashtable class_table;
		Hashtable types;
		Hashtable image_hash;
		Hashtable assembly_hash;
		ArrayList wrappers;

		MonoBuiltinTypes builtin;
		MonoSymbolFile corlib;
		TargetAddress StartAddress;
		SymbolTable symtab;
		int TotalSize;

		int address_size;

		int last_num_type_tables;
		int last_type_table_offset;
		ArrayList type_table;

		int last_num_misc_tables;
		int last_misc_table_offset = 1;

		public MonoSymbolTable (DebuggerBackend backend, MonoCSharpLanguageBackend language,
					ITargetMemoryAccess memory, TargetAddress address)
		{
			this.CSharpLanguage = language;
			this.Backend = backend;
			this.TargetInfo = memory;

			address_size = memory.TargetAddressSize;

			image_hash = new Hashtable ();
			assembly_hash = new Hashtable ();
			type_table = new ArrayList ();

			types = new Hashtable ();
			class_table = new Hashtable ();
			wrappers = new ArrayList ();

			SymbolFiles = new ArrayList ();

			TotalSize = language.MonoDebuggerInfo.SymbolTableSize;
			StartAddress = address;

			symtab = new WrapperSymbolTable (this);
			backend.ModuleManager.AddModule (this);
		}

		internal void Update (ITargetMemoryAccess memory)
		{
			ITargetMemoryReader header = memory.ReadMemory (StartAddress, TotalSize);

			long magic = header.ReadLongInteger ();
			if (magic != DynamicMagic)
				throw new SymbolTableException (
					"Dynamic section has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version < MinDynamicVersion)
				throw new SymbolTableException (
					"Dynamic section has version {0}, but " +
					"expected at least {1}.", version,
					MinDynamicVersion);
			if (version > MaxDynamicVersion)
				throw new SymbolTableException (
					"Dynamic section has version {0}, but " +
					"expected at most {1}.", version,
					MaxDynamicVersion);

			int total_size = header.ReadInteger ();
			if (total_size != TotalSize)
				throw new InternalError ();

			header.ReadAddress ();
			TargetAddress corlib_address = header.ReadAddress ();
			TargetAddress builtin_types_address = header.ReadAddress ();

			int num_symbol_files = header.ReadInteger ();
			TargetAddress symbol_files = header.ReadAddress ();

			symbol_files += SymbolFiles.Count * address_size;

			for (int i = SymbolFiles.Count; i < num_symbol_files; i++) {
				TargetAddress address = memory.ReadAddress (symbol_files);
				symbol_files += address_size;

				MonoSymbolFile symfile = new MonoSymbolFile (
					this, Backend, memory, memory, address);
				SymbolFiles.Add (symfile);

				if (address == corlib_address)
					corlib = symfile;
			}

			read_type_table (memory, header);

			if (version == 44)
				read_misc_table (memory, header);

			if (corlib == null)
				throw new InternalError ();
			if (builtin == null) {
				builtin = new MonoBuiltinTypes (this, memory, builtin_types_address, corlib);
			}

			bool updated = false;

			foreach (MonoSymbolFile symfile in SymbolFiles) {
				if (!symfile.LoadSymbols)
					continue;

				if (symfile.Update (memory))
					updated = true;
			}
		}

		void read_type_table (ITargetMemoryAccess memory, ITargetMemoryReader header)
		{
			int num_type_tables = header.ReadInteger ();
			int chunk_size = header.ReadInteger ();
			TargetAddress type_tables = header.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, "TYPE TABLES: {0} {1} {2} {3}",
				      last_num_type_tables, num_type_tables, chunk_size,
				      type_tables);

			if (num_type_tables != last_num_type_tables) {
				int old_offset = 0;
				int old_count = num_type_tables;
				int old_size = old_count * chunk_size;
				byte[] old_data = new byte [old_size];

				for (int i = 0; i < num_type_tables; i++) {
					TargetAddress old_table = memory.ReadAddress (
						type_tables);
					type_tables += address_size;

					byte[] temp_data = memory.ReadBuffer (
						old_table, chunk_size);
					temp_data.CopyTo (old_data, old_offset);
					old_offset += chunk_size;
				}

				last_num_type_tables = num_type_tables;
				last_type_table_offset = old_size;

				type_table = new ArrayList ();
				type_table.Add (new TypeEntry (0, old_size, old_data));
			}

			TargetAddress type_table_address = header.ReadAddress ();
			int type_table_total_size = header.ReadInteger ();
			int offset = header.ReadInteger ();
			int start = header.ReadInteger ();

			int size = offset - last_type_table_offset;
			int read_offset = last_type_table_offset - start;

			Report.Debug (DebugFlags.JitSymtab,
				      "TYPE TABLE: {0} {1} {2} {3} - {4} {5}",
				      type_table_address, type_table_total_size,
				      offset, start, read_offset, size);

			if (size != 0) {
				byte[] data = memory.ReadBuffer (
					type_table_address + read_offset, size);
				type_table.Add (
					new TypeEntry (last_type_table_offset, size, data));
			}

			last_type_table_offset = offset;
		}

		void read_misc_table (ITargetMemoryAccess memory, ITargetMemoryReader header)
		{
			int num_misc_tables = header.ReadInteger ();
			int chunk_size = header.ReadInteger ();
			TargetAddress misc_tables = header.ReadAddress ();

			Report.Debug (DebugFlags.JitSymtab, "MISC TABLES: {0} {1} {2} {3}",
				      last_num_misc_tables, num_misc_tables, chunk_size,
				      misc_tables);

			if (num_misc_tables != last_num_misc_tables) {
				int old_offset = 0;
				int old_count = num_misc_tables;
				int old_size = old_count * chunk_size;
				byte[] old_data = new byte [old_size];

				for (int i = 0; i < num_misc_tables; i++) {
					TargetAddress old_table = memory.ReadAddress (
						misc_tables);
					misc_tables += address_size;

					byte[] temp_data = memory.ReadBuffer (
						old_table, chunk_size);
					temp_data.CopyTo (old_data, old_offset);
					old_offset += chunk_size;
				}

				last_num_misc_tables = num_misc_tables;
				last_misc_table_offset = old_size;
			}

			TargetAddress misc_table_address = header.ReadAddress ();
			int misc_table_total_size = header.ReadInteger ();
			int offset = header.ReadInteger ();
			int start = header.ReadInteger ();

			int size = offset - last_misc_table_offset;
			int read_offset = last_misc_table_offset - start;

			Report.Debug (DebugFlags.JitSymtab,
				      "MISC TABLE: {0} {1} {2} {3} - {4} {5}",
				      misc_table_address, misc_table_total_size,
				      offset, start, read_offset, size);

			if (!misc_table_address.IsNull && (size != 0)) {
				ITargetMemoryReader reader = memory.ReadMemory (
					misc_table_address + read_offset, size);
				process_misc_entry (memory, reader);
			}

			last_misc_table_offset = offset;
		}

		enum MiscEntryType {
			Unknown = 0,
			Wrapper
		}

		void process_misc_entry (ITargetMemoryAccess memory,
					 ITargetMemoryReader reader)
		{
			while (reader.BinaryReader.Position < reader.BinaryReader.Size) {
				long offset = reader.BinaryReader.Position;
				int size = reader.BinaryReader.ReadInt32 ();
				long end = reader.BinaryReader.Position + size;

				int type = reader.BinaryReader.ReadInt32 ();
				switch ((MiscEntryType) type) {
				case MiscEntryType.Wrapper:
					wrappers.Add (WrapperEntry.ReadWrapper ( this, reader));
					break;

				default:
					// Ignore unknown entries
					break;
				}

				reader.BinaryReader.Position = end;
			}
		}

		public MonoType GetType (Type type, int offset)
		{
			MonoType retval = (MonoType) types [type];
			if (retval != null)
				return retval;

			MonoSymbolFile reader = GetImage (type.Assembly);
			if (reader == null)
				throw new InternalError ();

			byte[] data = GetTypeInfo (offset);
			TargetBinaryReader info = new TargetBinaryReader (data, TargetInfo);
			retval = MonoType.GetType (type, info, reader);
			types.Add (type, retval);
			return retval;
		}

		public MonoType GetTypeFromClass (long klass_address)
		{
			ClassEntry entry = (ClassEntry) class_table [klass_address];

			if (entry == null) {
				Console.WriteLine ("Can't find class at address {0:x}", klass_address);
				throw new InternalError ();
			}

			return GetType (entry.Type, entry.TypeInfo);
		}

		public AddressDomain GlobalAddressDomain {
			get {
				return TargetInfo.GlobalAddressDomain;
			}
		}

		public AddressDomain AddressDomain {
			get {
				return Backend.ThreadManager.AddressDomain;
			}
		}

		public ArrayList Wrappers {
			get {
				lock (this) {
					return wrappers;
				}
			}
		}

		internal void AddType (ClassEntry type)
		{
			lock (this) {
				if (!class_table.Contains (type.KlassAddress.Address))
					class_table.Add (type.KlassAddress.Address, type);
			}
		}

		internal void AddImage (MonoSymbolFile reader)
		{
			lock (this) {
				image_hash.Add (reader.MonoImage.Address, reader);
				assembly_hash.Add (reader.Assembly, reader);
			}
		}

		public MonoSymbolFile GetImage (TargetAddress address)
		{
			lock (this) {
				return (MonoSymbolFile) image_hash [address.Address];
			}
		}

		public MonoSymbolFile GetImage (R.Assembly assembly)
		{
			lock (this) {
				return (MonoSymbolFile) assembly_hash [assembly];
			}
		}

		//
		// ILanguage
		//

		string ILanguage.Name {
			get { return "C#"; }
		}

		public MonoObjectType ObjectType {
			get { return builtin.ObjectType; }
		}

		public MonoStringType StringType {
			get { return builtin.StringType; }
		}

		ITargetFundamentalType ILanguage.IntegerType {
			get { return builtin.Int32Type; }
		}

		ITargetFundamentalType ILanguage.LongIntegerType {
			get { return builtin.Int64Type; }
		}

		ITargetFundamentalType ILanguage.StringType {
			get { return builtin.StringType; }
		}

		ITargetType ILanguage.PointerType {
			get { return builtin.IntType; }
		}

		private ITargetType LookupType (StackFrame frame, Type type, string name)
		{
			int offset = CSharpLanguage.LookupType (frame, name);
			if (offset == 0)
				return null;
			return GetType (type, offset);
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			switch (name) {
			case "short":
				name = "System.Int16";
				break;
			case "ushort":
				name = "System.UInt16";
				break;
			case "int":
				name = "System.Int32";
				break;
			case "uint":
				name = "System.UInt32";
				break;
			case "long":
				name = "System.Int64";
				break;
			case "ulong":
				name = "System.UInt64";
				break;
			case "float":
				name = "System.Single";
				break;
			case "double":
				name = "System.Double";
				break;
			case "char":
				name = "System.Char";
				break;
			case "byte":
				name = "System.Byte";
				break;
			case "sbyte":
				name = "System.SByte";
				break;
			case "object":
				name = "System.Object";
				break;
			case "string":
				name = "System.String";
				break;
			case "bool":
				name = "System.Boolean";
				break;
			case "void":
				name = "System.Void";
				break;
			case "decimal":
				name = "System.Decimal";
				break;
			}

			if (name.IndexOf ('[') >= 0)
				return null;

			foreach (MonoSymbolFile symfile in SymbolFiles) {
				Type type = symfile.Assembly.GetType (name);
				if (type == null)
					continue;

				MonoType mtype = (MonoType) types [type];
				if (mtype != null)
					return mtype;

				return LookupType (frame, type, name);
			}

			return null;
		}

		public MonoFundamentalType GetType (Type type)
		{
			if (type == typeof (byte))
				return builtin.ByteType;
			else if (type == typeof (sbyte))
				return builtin.SByteType;
			else if (type == typeof (bool))
				return builtin.BooleanType;
			else if (type == typeof (short))
				return builtin.Int16Type;
			else if (type == typeof (ushort))
				return builtin.UInt16Type;
			else if (type == typeof (int))
				return builtin.Int32Type;
			else if (type == typeof (uint))
				return builtin.UInt32Type;
			else if (type == typeof (long))
				return builtin.Int64Type;
			else if (type == typeof (ulong))
				return builtin.UInt64Type;
			else if (type == typeof (float))
				return builtin.SingleType;
			else if (type == typeof (double))
				return builtin.DoubleType;
			else if (type == typeof (char))
				return builtin.CharType;
			else if (type == typeof (string))
				return builtin.StringType;
			else if (type == typeof (IntPtr))
				return builtin.IntType;

			return null;
		}

		public bool CanCreateInstance (Type type)
		{
			return GetType (type) != null;
		}

		public ITargetObject CreateInstance (StackFrame frame, object obj)
		{
			MonoFundamentalType type = GetType (obj.GetType ());
			if (type == null)
				return null;

			return type.CreateInstance (frame, obj);
		}

		public ITargetObject CreateObject (StackFrame frame, TargetAddress address)
		{
			MonoObjectObject obj = builtin.ObjectType.CreateObject (frame, address);
			if (obj == null)
				return null;

			if (obj.HasDereferencedObject)
				return obj.DereferencedObject;
			else
				return obj;
		}

		private class WrapperEntry : SymbolRangeEntry
		{
			MonoSymbolTable table;
			TargetAddress func;
			TargetAddress method_start, method_end;
			public readonly string Name;

			private WrapperEntry (MonoSymbolTable table, string name,
					      TargetAddress start, TargetAddress end,
					      TargetAddress func, TargetAddress method_start,
					      TargetAddress method_end)
				: base (start, end)
			{
				this.table = table;
				this.Name = name;
				this.func = func;
				this.method_start = method_start;
				this.method_end = method_end;
			}

			public static WrapperEntry ReadWrapper (MonoSymbolTable table,
								ITargetMemoryReader reader)
			{
				string name = reader.BinaryReader.ReadString ();
				TargetAddress start = reader.ReadGlobalAddress ();
				TargetAddress end = reader.ReadGlobalAddress ();
				TargetAddress func = reader.ReadGlobalAddress ();
				TargetAddress method_start = reader.ReadGlobalAddress ();
				TargetAddress method_end = reader.ReadGlobalAddress ();

				return new WrapperEntry (table, name, start, end, func,
							 method_start, method_end);
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return new WrapperMethod (
					table, Name, StartAddress, EndAddress, func,
					method_start, method_end);
			}

			public override string ToString ()
			{
				return String.Format ("WrapperEntry [{0:x}:{1:x}:{2:x}:{3}]",
						      StartAddress, EndAddress, func, Name);
			}
		}

		protected class WrapperMethod : MethodBase
		{
			public WrapperMethod (MonoSymbolTable table, string name,
					      TargetAddress start, TargetAddress end,
					      TargetAddress func, TargetAddress m_start,
					      TargetAddress m_end)
				: base (name, null, table.corlib, start, end)
			{
				SetMethodBounds (m_start, m_end);
				SetWrapperAddress (func);
			}

			public override object MethodHandle {
				get {
					return null;
				}
			}

			public override IVariable[] Parameters {
				get {
					return null;
				}
			}

			public override IVariable[] Locals {
				get {
					return null;
				}
			}

			public override ITargetStructType DeclaringType {
				get {
					return null;
				}
			}

			public override bool HasThis {
				get {
					return false;
				}
			}

			public override IVariable This {
				get {
					return null;
				}
			}

			public override SourceMethod GetTrampoline (ITargetMemoryAccess memory,
								    TargetAddress address)
			{
				return null;
			}
		}

		private class WrapperSymbolTable : SymbolTable
		{
			MonoSymbolTable table;

			public WrapperSymbolTable (MonoSymbolTable table)
			{
				this.table = table;
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

			public override bool HasRanges {
				get {
					return true;
				}
			}

			public override ISymbolRange[] SymbolRanges {
				get {
					ArrayList ranges = table.Wrappers;
					ISymbolRange[] retval = new ISymbolRange [ranges.Count];
					ranges.CopyTo (retval, 0);
					return retval;
				}
			}
		}

		public override string Name {
			get { return "<Mono Runtime>"; }
		}

		public override ILanguage Language {
			get { return this; }
		}

		internal override ILanguageBackend LanguageBackend {
			get { return CSharpLanguage; }
		}

		public override ISymbolFile SymbolFile {
			get { return this; }
		}

		public override bool SymbolsLoaded {
			get { return true; }
		}

		public SourceFile[] Sources {
			get { return new SourceFile [0]; }
		}

		public override bool HasDebuggingInfo {
			get { return true; }
		}

		public override ISymbolTable SymbolTable {
			get { return symtab; }
		}

		public override ISimpleSymbolTable SimpleSymbolTable {
			get { return this; }
		}

		public override TargetAddress SimpleLookup (string name)
		{
			return TargetAddress.Null;
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (WrapperEntry wrapper in wrappers) {
				if ((address < wrapper.StartAddress) ||
				    (address > wrapper.EndAddress))
					continue;

				long offset = address - wrapper.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				return new Symbol (
					wrapper.Name, wrapper.StartAddress, (int) offset);
			}

			return null;
		}

		internal override IDisposable RegisterLoadHandler (Process process,
								   SourceMethod source,
								   MethodLoadedHandler handler,
								   object user_data)
		{
			return null;
		}

		internal override SimpleStackFrame UnwindStack (SimpleStackFrame frame,
								ITargetMemoryAccess memory)
		{
			return null;
		}

		void ISymbolFile.GetMethods (SourceFile file)
		{ }

		IMethod ISymbolFile.GetMethod (long handle)
		{
			return null;
		}

		SourceMethod ISymbolFile.FindMethod (string name)
		{
			return null;
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
			// If this is a call to Dispose, dispose all managed resources.
				if (disposing) {
					SymbolFiles = null;
					types = new Hashtable ();
					class_table = new Hashtable ();
				}

				// Release unmanaged resources
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
				// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~MonoSymbolTable ()
		{
			Dispose (false);
		}

		private struct TypeEntry {
			public readonly int Offset;
			public readonly int Size;
			public readonly byte[] Data;

			public TypeEntry (ITargetMemoryAccess memory, TargetAddress address,
					  int offset, int size)
			{
				this.Offset = offset;
				this.Size = size;
				this.Data = memory.ReadBuffer (address + offset, size);
			}

			public TypeEntry (int offset, int size, byte[] data)
			{
				this.Offset = offset;
				this.Size = size;
				this.Data = data;
			}
		}

		public byte[] GetTypeInfo (int offset)
		{
			int count = type_table.Count;
			for (int i = 0; i < count; i++) {
				TypeEntry entry = (TypeEntry) type_table [i];

				if (offset >= entry.Offset + entry.Size)
					continue;

				offset -= entry.Offset;
				int size = BitConverter.ToInt32 (entry.Data, offset);

				byte[] retval = new byte [size];
				Array.Copy (entry.Data, offset + 4, retval, 0, size);
				return retval;
			}

			throw new InternalError ("Can't find type entry at offset {0}.", offset);
		}
	}

	internal class MonoDebuggerInfo
	{
		public readonly TargetAddress GenericTrampolineCode;
		public readonly TargetAddress SymbolTable;
		public readonly int SymbolTableSize;
		public readonly TargetAddress CompileMethod;
		public readonly TargetAddress InsertBreakpoint;
		public readonly TargetAddress RemoveBreakpoint;
		public readonly TargetAddress RuntimeInvoke;
		public readonly TargetAddress CreateString;
		public readonly TargetAddress ClassGetStaticFieldData;
		public readonly TargetAddress LookupType;
		public readonly TargetAddress LookupAssembly;
		public readonly TargetAddress Heap;
		public readonly int HeapSize;

		internal MonoDebuggerInfo (ITargetMemoryReader reader)
		{
			reader.Offset = reader.TargetLongIntegerSize +
				2 * reader.TargetIntegerSize;
			GenericTrampolineCode = reader.ReadAddress ();
			SymbolTable = reader.ReadAddress ();
			SymbolTableSize = reader.ReadInteger ();
			CompileMethod = reader.ReadAddress ();
			InsertBreakpoint = reader.ReadAddress ();
			RemoveBreakpoint = reader.ReadAddress ();
			RuntimeInvoke = reader.ReadAddress ();
			CreateString = reader.ReadAddress ();
			ClassGetStaticFieldData = reader.ReadAddress ();
			LookupType = reader.ReadAddress ();
			LookupAssembly = reader.ReadAddress ();
			Heap = reader.ReadAddress ();
			HeapSize = reader.ReadInteger ();
			Report.Debug (DebugFlags.JitSymtab, this);
		}

		public override string ToString ()
		{
			return String.Format (
				"MonoDebuggerInfo ({0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x})",
				GenericTrampolineCode, SymbolTable, SymbolTableSize,
				CompileMethod, InsertBreakpoint, RemoveBreakpoint,
				RuntimeInvoke);
		}
	}

	internal class ClassEntry
	{
		public readonly TargetAddress KlassAddress;
		public readonly int Rank;
		public readonly int Token;
		public readonly int TypeInfo;
		public readonly Type Type;

		internal ClassEntry (MonoSymbolFile reader, ITargetMemoryReader memory)
		{
			KlassAddress = memory.ReadAddress ();
			Rank = memory.BinaryReader.ReadInt32 ();
			Token = memory.BinaryReader.ReadInt32 ();
			TypeInfo = memory.BinaryReader.ReadInt32 ();

			if (Token != 0)
				Type = C.MonoDebuggerSupport.GetType (reader.Assembly, Token);
		}

		public static void ReadClasses (MonoSymbolFile reader,
						ITargetMemoryReader memory, int count)
		{
			for (int i = 0; i < count; i++) {
				ClassEntry entry = new ClassEntry (reader, memory);
				reader.Table.AddType (entry);
			}
		}

		public override string ToString ()
		{
			return String.Format ("ClassEntry [{0:x}:{1}:{2:x}:{3:x}]",
					      KlassAddress, Rank, Token, TypeInfo);
		}
	}

	// <summary>
	//   A single Assembly's symbol table.
	// </summary>
	[Serializable]
	internal class MonoSymbolFile : Module, ISymbolFile, ISimpleSymbolTable, ISerializable
	{
		internal readonly int Index;
		internal readonly R.Assembly Assembly;
		internal readonly MonoSymbolTable Table;
		internal readonly TargetAddress MonoImage;
		internal readonly string ImageFile;
		internal readonly C.MonoSymbolFile File;
		internal ThreadManager ThreadManager;
		internal AddressDomain GlobalAddressDomain;
		internal ITargetInfo TargetInfo;
		protected DebuggerBackend backend;
		protected Hashtable range_hash;
		MonoCSharpSymbolTable symtab;
		ArrayList ranges;
		string name;

		TargetAddress dynamic_address;
		int class_entry_size;
		int address_size;
		int int_size;

		int generation;
		int num_range_entries;
		int num_class_entries;

		internal MonoSymbolFile (MonoSymbolTable table, DebuggerBackend backend,
					 ITargetInfo target_info, ITargetMemoryAccess memory,
					 TargetAddress address)
		{
			this.Table = table;
			this.TargetInfo = target_info;
			this.backend = backend;

			ThreadManager = backend.ThreadManager;
			GlobalAddressDomain = memory.GlobalAddressDomain;

			address_size = TargetInfo.TargetAddressSize;
			int_size = TargetInfo.TargetIntegerSize;

			ranges = new ArrayList ();
			range_hash = new Hashtable ();

			Index = memory.ReadInteger (address);
			address += int_size;
			address += address_size;
			MonoImage = memory.ReadAddress (address);
			address += address_size;
			TargetAddress image_file_addr = memory.ReadAddress (address);
			address += address_size;
			ImageFile = memory.ReadString (image_file_addr);

			Assembly = R.Assembly.LoadFrom (ImageFile);

			table.AddImage (this);

			Report.Debug (DebugFlags.JitSymtab, "SYMBOL TABLE READER: {0}", ImageFile);

			class_entry_size = memory.ReadInteger (address);
			address += int_size;
			dynamic_address = address;

			File = C.MonoSymbolFile.ReadSymbolFile (Assembly);

			symtab = new MonoCSharpSymbolTable (this);

			name = Assembly.GetName (true).Name;

			backend.ModuleManager.AddModule (this);

			OnModuleChangedEvent ();
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5})",
					      GetType (), ImageFile, IsLoaded,
					      SymbolsLoaded, StepInto, LoadSymbols);
		}

		// <remarks>
		//   Each time we reload the JIT's symbol tables, add the addresses of all
		//   methods which have been JITed since the last update.
		// </remarks>
		bool update_ranges (ITargetMemoryAccess memory, ref TargetAddress address)
		{
			TargetAddress range_table = memory.ReadAddress (address);
			address += address_size;
			int range_entry_size = memory.ReadInteger (address);
			address += int_size;
			int new_num_range_entries = memory.ReadInteger (address);
			address += int_size;

			Report.Debug (DebugFlags.JitSymtab, "RANGES: {0} {1} {2}", this,
				      num_range_entries, new_num_range_entries);

			if (new_num_range_entries == num_range_entries)
				return false;

			int count = new_num_range_entries - num_range_entries;
			ITargetMemoryReader range_reader = memory.ReadMemory (
				range_table + num_range_entries * range_entry_size,
				count * range_entry_size);

			ArrayList new_ranges = MethodRangeEntry.ReadRanges (
				this, memory, range_reader, count);

			ranges.AddRange (new_ranges);
			num_range_entries = new_num_range_entries;
			return true;
		}

		// <summary>
		//   Add all classes which have been created in the meantime.
		// </summary>
		bool update_classes (ITargetMemoryAccess memory, ref TargetAddress address)
		{
			TargetAddress class_table = memory.ReadAddress (address);
			address += address_size;
			int new_num_class_entries = memory.ReadInteger (address);
			address += int_size;

			if (new_num_class_entries == num_class_entries)
				return false;

			int count = new_num_class_entries - num_class_entries;
			ITargetMemoryReader class_reader = memory.ReadMemory (
				class_table + num_class_entries * class_entry_size,
				count * class_entry_size);

			ClassEntry.ReadClasses (this, class_reader, count);

			num_class_entries = new_num_class_entries;
			return true;
		}

		public bool Update (ITargetMemoryAccess memory)
		{
			TargetAddress address = dynamic_address;
			if (memory.ReadInteger (address) != 0)
				return false;
			address += int_size;

			int new_generation = memory.ReadInteger (address);
			if (new_generation == generation)
				return false;
			address += int_size;

			generation = new_generation;

			bool updated = false;

			updated |= update_ranges (memory, ref address);
			updated |= update_classes (memory, ref address);

			return true;
		}

		Hashtable method_hash = new Hashtable ();

		IMethod ISymbolFile.GetMethod (long handle)
		{
			MethodRangeEntry entry = (MethodRangeEntry) range_hash [(int) handle];
			if (entry == null)
				return null;

			return entry.GetMethod ();
		}

		void ISymbolFile.GetMethods (SourceFile file)
		{
			ensure_sources ();
			C.SourceFileEntry source = (C.SourceFileEntry) source_hash [file];

			foreach (C.MethodSourceEntry entry in source.Methods)
				GetSourceMethod (file, entry.Index);
		}

		public override SourceMethod FindMethod (string name)
		{
			return null;
		}

		protected MonoMethod GetMonoMethod (int index)
		{
			ensure_sources ();
			MonoMethod mono_method = (MonoMethod) method_hash [index];
			if (mono_method != null)
				return mono_method;

			SourceMethod method = GetSourceMethod (index);
			C.MethodEntry entry = File.GetMethod (index);

			mono_method = new MonoMethod (this, method, entry);
			method_hash.Add (index, mono_method);
			return mono_method;
		}

		protected MonoMethod GetMonoMethod (int index, byte[] contents)
		{
			MonoMethod method = GetMonoMethod (index);

			if (!method.IsLoaded) {
				TargetBinaryReader reader = new TargetBinaryReader (contents, TargetInfo);
				method.Load (reader, GlobalAddressDomain);
			}

			return method;
		}

		internal MonoType GetType (ITargetMemoryAccess memory, TargetAddress address)
		{
			ITargetMemoryReader reader = memory.ReadMemory (address, class_entry_size);
			ClassEntry entry = new ClassEntry (this, reader);

			Table.AddType (entry);
			return Table.GetType (entry.Type, entry.TypeInfo);
		}

		ArrayList sources = null;
		Hashtable source_hash = null;
		Hashtable source_file_hash = null;
		Hashtable method_index_hash = null;
		void ensure_sources ()
		{
			if (sources != null)
				return;

			sources = new ArrayList ();
			source_hash = new Hashtable ();
			source_file_hash = new Hashtable ();
			method_index_hash = new Hashtable ();

			if (File == null)
				return;

			foreach (C.SourceFileEntry source in File.Sources) {
				SourceFile info = new SourceFile (this, source.FileName);

				sources.Add (info);
				source_hash.Add (info, source);
				source_file_hash.Add (source, info);
			}
		}

		public override string Name {
			get { return name; }
		}

		public override ILanguage Language {
			get { return Table; }
		}

		internal override ILanguageBackend LanguageBackend {
			get { return Table.LanguageBackend; }
		}

		public override ISymbolFile SymbolFile {
			get { return this; }
		}

		public override bool SymbolsLoaded {
			get { return LoadSymbols; }
		}

		public SourceFile[] Sources {
			get { return GetSources (); }
		}

		public override bool HasDebuggingInfo {
			get { return File != null; }
		}

		public override ISimpleSymbolTable SimpleSymbolTable {
			get { return this; }
		}

		public override TargetAddress SimpleLookup (string name)
		{
			return TargetAddress.Null;
		}

		public SourceFile[] GetSources ()
		{
			ensure_sources ();
			SourceFile[] retval = new SourceFile [sources.Count];
			sources.CopyTo (retval, 0);
			return retval;
		}

		SourceMethod GetSourceMethod (int index)
		{
			ensure_sources ();
			SourceMethod method = (SourceMethod) method_index_hash [index];
			if (method != null)
				return method;

			C.MethodEntry entry = File.GetMethod (index);
			SourceFile file = (SourceFile) source_file_hash [entry.SourceFile];

			return CreateSourceMethod (file, index);
		}

		SourceMethod GetSourceMethod (SourceFile file, int index)
		{
			ensure_sources ();
			SourceMethod method = (SourceMethod) method_index_hash [index];
			if (method != null)
				return method;

			return CreateSourceMethod (file, index);
		}

		SourceMethod CreateSourceMethod (SourceFile file, int index)
		{
			C.MethodEntry entry = File.GetMethod (index);
			C.MethodSourceEntry source = File.GetMethodSource (index);

			R.MethodBase mbase = entry.MethodBase;

			StringBuilder sb = new StringBuilder (mbase.DeclaringType.FullName);
			sb.Append (".");
			sb.Append (entry.Name);
			sb.Append ("(");
			bool first = true;
			foreach (R.ParameterInfo param in mbase.GetParameters ()) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (param.ParameterType.FullName);
			}
			sb.Append (")");

			string name = sb.ToString ();
			SourceMethod method = new SourceMethod (
				this, file, source.Index, name, source.StartRow,
				source.EndRow, true);

			method_index_hash.Add (index, method);
			return method;
		}

		public SourceMethod GetMethod (int index)
		{
			return GetSourceMethod (index);
		}

		public SourceMethod GetMethodByToken (int token)
		{
			if (File == null)
				return null;

			ensure_sources ();
			C.MethodEntry entry = File.GetMethodByToken (token);
			if (entry == null)
				return null;
			return GetSourceMethod (entry.Index);
		}

		internal ArrayList SymbolRanges {
			get {
				return ranges;
			}
		}

		public override ISymbolTable SymbolTable {
			get {
				return symtab;
			}
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			foreach (MethodRangeEntry range in ranges) {
				if ((address < range.StartAddress) || (address > range.EndAddress))
					continue;

				long offset = address - range.StartAddress;
				if (exact_match && (offset != 0))
					continue;

				IMethod method = range.GetMethod ();
				return new Symbol (
					method.Name, method.StartAddress, (int) offset);
			}

			return null;
		}

		internal override IDisposable RegisterLoadHandler (Process process,
								   SourceMethod source,
								   MethodLoadedHandler handler,
								   object user_data)
		{
			MonoMethod method = GetMonoMethod ((int) source.Handle);
			return method.RegisterLoadHandler (process, handler, user_data);
		}

		internal override SimpleStackFrame UnwindStack (SimpleStackFrame frame,
								ITargetMemoryAccess memory)
		{
			return null;
		}

		//
		// ISerializable
		//

		void ISerializable.GetObjectData (SerializationInfo info,
						  StreamingContext context)
		{
			info.SetType (typeof (MonoSymbolFileProxy));
			info.AddValue ("image", ImageFile);
			info.AddValue ("step_into", StepInto);
			info.AddValue ("load_symbols", LoadSymbols);
		}

		[Serializable]
		protected sealed class MonoSymbolFileProxy : IObjectReference, ISerializable
		{
			string image;
			bool step_into;
			bool load_symbols;

			public object GetRealObject (StreamingContext context)
			{
				Process process = (Process) context.Context;
				DebuggerBackend backend = process.DebuggerBackend;
				MonoCSharpLanguageBackend csharp = backend.CSharpLanguage;
				MonoSymbolFile file = csharp.FindImage (process, image);
				if (file == null)
					throw new SerializationException ();

				file.StepInto = step_into;
				file.LoadSymbols = load_symbols;
				return file;
			}

			void ISerializable.GetObjectData (SerializationInfo info,
							  StreamingContext context)
			{
				throw new InvalidOperationException ();
			}

			private MonoSymbolFileProxy (SerializationInfo info,
						     StreamingContext context)
			{
				image = info.GetString ("image");
				step_into = info.GetBoolean ("step_into");
				load_symbols = info.GetBoolean ("load_symbols");
			}
		}

		protected class MonoMethod : MethodBase
		{
			MonoSymbolFile reader;
			SourceMethod info;
			C.MethodEntry method;
			R.MethodBase rmethod;
			MonoClass decl_type;
			MonoType[] param_types;
			MonoType[] local_types;
			IVariable this_var;
			IVariable[] parameters;
			IVariable[] locals;
			bool has_variables;
			bool is_loaded;
			MethodAddress address;
			Hashtable load_handlers;

			public MonoMethod (MonoSymbolFile reader, SourceMethod info, C.MethodEntry method)
				: base (info.Name, reader.ImageFile, reader)
			{
				this.reader = reader;
				this.info = info;
				this.method = method;
				this.rmethod = method.MethodBase;
			}

			public MonoMethod (MonoSymbolFile reader, SourceMethod info, C.MethodEntry method,
					   ITargetMemoryReader dynamic_reader)
				: this (reader, info, method)
			{
				Load (dynamic_reader.BinaryReader, reader.GlobalAddressDomain);
			}

			public MethodAddress MethodAddress {
				get {
					if (!IsLoaded)
						throw new InvalidOperationException ();

					return address;
				}
			}

			public void Load (TargetBinaryReader dynamic_reader, AddressDomain domain)
			{
				if (is_loaded)
					throw new InternalError ();

				is_loaded = true;

				address = new MethodAddress (method, dynamic_reader, domain);

				SetAddresses (address.StartAddress, address.EndAddress);
				SetMethodBounds (address.MethodStartAddress, address.MethodEndAddress);

				if (!address.WrapperAddress.IsNull)
					SetWrapperAddress (address.WrapperAddress);

				MethodSource source = new CSharpMethod (
					reader, this, info, method, address.LineNumbers);

				if (source != null)
					SetSource (source);
			}

			void get_variables ()
			{
				if (has_variables || !is_loaded)
					return;

				R.ParameterInfo[] param_info = rmethod.GetParameters ();
				param_types = new MonoType [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					param_types [i] = reader.Table.GetType (
						param_info [i].ParameterType, address.ParamTypeInfoOffsets [i]);

				parameters = new IVariable [param_info.Length];
				for (int i = 0; i < param_info.Length; i++)
					parameters [i] = new MonoVariable (
						reader.backend, param_info [i].Name, param_types [i],
						false, param_types [i].IsByRef, this,
						address.ParamVariableInfo [i], 0, 0);

				local_types = new MonoType [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					Type type = method.LocalTypes [i];

					local_types [i] = reader.Table.GetType (
						type, address.LocalTypeInfoOffsets [i]);
				}

				locals = new IVariable [method.NumLocals];
				for (int i = 0; i < method.NumLocals; i++) {
					C.LocalVariableEntry local = method.Locals [i];

					if (method.LocalNamesAmbiguous && (local.BlockIndex > 0)) {
						int index = local.BlockIndex - 1;
						JitLexicalBlockEntry block = address.LexicalBlocks [index];
						locals [i] = new MonoVariable (
							reader.backend, local.Name, local_types [i],
							true, local_types [i].IsByRef, this,
							address.LocalVariableInfo [i],
							block.StartAddress, block.EndAddress);
					} else {
						locals [i] = new MonoVariable (
							reader.backend, local.Name, local_types [i],
							true, local_types [i].IsByRef, this,
							address.LocalVariableInfo [i]);
					}
				}

				decl_type = (MonoClass) reader.Table.GetType (
					rmethod.DeclaringType, address.ClassTypeInfoOffset);

				if (address.HasThis)
					this_var = new MonoVariable (
						reader.backend, "this", decl_type, true,
						true, this, address.ThisVariableInfo);

				has_variables = true;
			}

			public override object MethodHandle {
				get {
					return rmethod;
				}
			}

			public override IVariable[] Parameters {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return parameters;
				}
			}

			public override IVariable[] Locals {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return locals;
				}
			}

			public override ITargetStructType DeclaringType {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return decl_type;
				}
			}

			public override bool HasThis {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return this_var != null;
				}
			}

			public override IVariable This {
				get {
					if (!is_loaded)
						throw new InvalidOperationException ();

					get_variables ();
					return this_var;
				}
			}

			public override SourceMethod GetTrampoline (ITargetMemoryAccess memory,
								    TargetAddress address)
			{
				return reader.LanguageBackend.GetTrampoline (memory, address);
			}

			void breakpoint_hit (Inferior inferior, TargetAddress address,
					     object user_data)
			{
				if (load_handlers == null)
					return;

				// ensure_method ();

				foreach (HandlerData handler in load_handlers.Keys)
					handler.Handler (inferior, info, handler.UserData);

				load_handlers = null;
			}

			public IDisposable RegisterLoadHandler (Process process,
								MethodLoadedHandler handler,
								object user_data)
			{
				string full_name = String.Format (
					"{0}:{1}", rmethod.ReflectedType.FullName,
					rmethod.Name);

				if (load_handlers != null)
					throw new TargetException (
						TargetError.AlreadyHaveBreakpoint,
						"Already have a breakpoint on method {0}.",
						full_name);

				HandlerData data = new HandlerData (this, handler, user_data);

				load_handlers = new Hashtable ();

				reader.Table.CSharpLanguage.InsertBreakpoint (
					process, full_name,
					new BreakpointHandler (breakpoint_hit),
					null);

				load_handlers.Add (data, true);
				return data;
			}

			protected void UnRegisterLoadHandler (HandlerData data)
			{
				if (load_handlers == null)
					return;

				load_handlers.Remove (data);
				if (load_handlers.Count == 0)
					load_handlers = null;
			}

			protected sealed class HandlerData : IDisposable
			{
				public readonly MonoMethod Method;
				public readonly MethodLoadedHandler Handler;
				public readonly object UserData;

				public HandlerData (MonoMethod method,
						    MethodLoadedHandler handler,
						    object user_data)
				{
					this.Method = method;
					this.Handler = handler;
					this.UserData = user_data;
				}

				private bool disposed = false;

				private void Dispose (bool disposing)
				{
					if (!this.disposed) {
						if (disposing) {
							Method.UnRegisterLoadHandler (this);
						}
					}
						
					this.disposed = true;
				}

				public void Dispose ()
				{
					Dispose (true);
					// Take yourself off the Finalization queue
					GC.SuppressFinalize (this);
				}

				~HandlerData ()
				{
					Dispose (false);
				}
			}
		}

		private class MethodRangeEntry : SymbolRangeEntry
		{
			MonoSymbolFile reader;
			int index;
			byte[] contents;

			private MethodRangeEntry (MonoSymbolFile reader, int index,
						  byte[] contents, TargetAddress start_address,
						  TargetAddress end_address)
				: base (start_address, end_address)
			{
				this.reader = reader;
				this.index = index;
				this.contents = contents;
			}

			public static ArrayList ReadRanges (MonoSymbolFile reader,
							    ITargetMemoryAccess target,
							    ITargetMemoryReader memory, int count)
			{
				ArrayList list = new ArrayList ();

				for (int i = 0; i < count; i++) {
					TargetAddress start = memory.ReadGlobalAddress ();
					TargetAddress end = memory.ReadGlobalAddress ();
					int index = memory.ReadInteger ();
					TargetAddress dynamic_address = memory.ReadAddress ();
					int dynamic_size = memory.ReadInteger ();

					byte[] contents = target.ReadBuffer (dynamic_address, dynamic_size);

					MethodRangeEntry entry = new MethodRangeEntry (
						reader, index, contents, start, end);

					Report.Debug (DebugFlags.JitSymtab,
						      "RANGE ENTRY: {0} {1} {2} {3} {4} {5} {6}",
						      reader, start, end, index, dynamic_address,
						      dynamic_size, entry);

					list.Add (entry);
					reader.range_hash.Add (index, entry);
				}

				return list;
			}

			internal MonoMethod GetMethod ()
			{
				return reader.GetMonoMethod (index, contents);
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return reader.GetMonoMethod (index, contents);
			}

			public override string ToString ()
			{
				return String.Format ("RangeEntry [{0:x}:{1:x}:{2:x}]",
						      StartAddress, EndAddress, index);
			}
		}

		private class MonoCSharpSymbolTable : SymbolTable
		{
			MonoSymbolFile reader;

			public MonoCSharpSymbolTable (MonoSymbolFile reader)
			{
				this.reader = reader;
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

			public override bool HasRanges {
				get {
					return true;
				}
			}

			public override ISymbolRange[] SymbolRanges {
				get {
					ArrayList ranges = reader.SymbolRanges;
					ISymbolRange[] retval = new ISymbolRange [ranges.Count];
					ranges.CopyTo (retval, 0);
					return retval;
				}
			}

			public override void UpdateSymbolTable ()
			{
				base.UpdateSymbolTable ();
			}
		}
	}

	internal class MonoCSharpLanguageBackend : ILanguageBackend
	{
		DebuggerBackend backend;
		MonoDebuggerInfo info;
		TargetAddress trampoline_address;
		bool initialized;
		DebuggerMutex mutex;
		protected MonoSymbolTable table;
		Heap heap;

		public MonoCSharpLanguageBackend (DebuggerBackend backend)
		{
			this.backend = backend;

			mutex = new DebuggerMutex ("csharp_mutex");
		}

		public string Name {
			get {
				return "Mono";
			}
		}

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get {
				return info;
			}
		}

		internal MonoSymbolFile FindImage (Process process, string name)
		{
			if (table == null)
				return null;

			R.Assembly ass = R.Assembly.LoadFrom (name);
			MonoSymbolFile file = table.GetImage (ass);
			if (file != null)
				return file;

			int index = LookupAssembly (process, name);
			if (index < 0)
				return null;

			return table.GetImage (ass);
		}

		public Heap DataHeap {
			get { return heap; }
		}

		void read_mono_debugger_info (ITargetMemoryAccess memory, Bfd bfd)
		{
			TargetAddress symbol_info = bfd ["MONO_DEBUGGER__debugger_info"];
			if (symbol_info.IsNull)
				throw new SymbolTableException (
					"Can't get address of `MONO_DEBUGGER__debugger_info'.");

			ITargetMemoryReader header = memory.ReadMemory (symbol_info, 16);
			long magic = header.ReadLongInteger ();
			if (magic != MonoSymbolTable.DynamicMagic)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version < MonoSymbolTable.MinDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at least {1}.", version,
					MonoSymbolTable.MinDynamicVersion);
			if (version > MonoSymbolTable.MaxDynamicVersion)
				throw new SymbolTableException (
					"`MONO_DEBUGGER__debugger_info' has version {0}, " +
					"but expected at most {1}.", version,
					MonoSymbolTable.MaxDynamicVersion);

			int size = (int) header.ReadInteger ();

			ITargetMemoryReader table = memory.ReadMemory (symbol_info, size);
			info = new MonoDebuggerInfo (table);

			trampoline_address = memory.ReadGlobalAddress (info.GenericTrampolineCode);
			heap = new Heap ((ITargetInfo) memory, info.Heap, info.HeapSize);
		}

		void do_update_symbol_table (ITargetMemoryAccess memory, bool force_update)
		{
			Report.Debug (DebugFlags.JitSymtab, "Starting to update symbol table");
			backend.ModuleManager.Lock ();
			try {
				TargetAddress address = memory.ReadAddress (info.SymbolTable);
				if (address.IsNull) {
					Console.WriteLine ("Ooops, no symtab loaded.");
					return;
				}

				if (table == null)
					table = new MonoSymbolTable (backend, this, memory, address);

				table.Update (memory);
			} catch (ThreadAbortException) {
				table = null;
				return;
			} catch (Exception e) {
				Console.WriteLine ("Can't update symbol table: {0}", e);
				table = null;
				return;
			} finally {
				backend.ModuleManager.UnLock ();
			}
			Report.Debug (DebugFlags.JitSymtab, "Done updating symbol table");
		}

		Hashtable breakpoints = new Hashtable ();

		internal int InsertBreakpoint (Process process, string method_name,
					       BreakpointHandler handler, object user_data)
		{
			long retval = process.CallMethod (info.InsertBreakpoint, 0, method_name);

			int index = (int) retval;

			if (index <= 0)
				return -1;

			breakpoints.Add (index, new MyBreakpointHandle (index, handler, user_data));
			return index;
		}

		private struct MyBreakpointHandle
		{
			public readonly int Index;
			public readonly BreakpointHandler Handler;
			public readonly object UserData;

			public MyBreakpointHandle (int index, BreakpointHandler handler, object user_data)
			{
				this.Index = index;
				this.Handler = handler;
				this.UserData = user_data;
			}
		}

		public TargetAddress GenericTrampolineCode {
			get {
				return trampoline_address;
			}
		}

		public TargetAddress RuntimeInvokeFunc {
			get {
				return info.RuntimeInvoke;
			}
		}

		TargetAddress ILanguageBackend.GetTrampolineAddress (ITargetMemoryAccess memory,
								     TargetAddress address,
								     out bool is_start)
		{
			is_start = false;

			if (trampoline_address.IsNull)
				return TargetAddress.Null;

			return memory.Architecture.GetTrampoline (
				memory, address, trampoline_address);
		}

		TargetAddress ILanguageBackend.CompileMethodFunc {
			get {
				return info.CompileMethod;
			}
		}

		public SourceMethod GetTrampoline (ITargetMemoryAccess memory,
						   TargetAddress address)
		{
			if (trampoline_address.IsNull)
				return null;

			TargetAddress trampoline = memory.Architecture.GetTrampoline (
				memory, address, trampoline_address);

			if (trampoline.IsNull)
				return null;

			int token = memory.ReadInteger (trampoline + 4);
			TargetAddress klass = memory.ReadAddress (trampoline + 8);
			TargetAddress image = memory.ReadAddress (klass);
			MonoSymbolFile reader = table.GetImage (image);

			// Console.WriteLine ("TRAMPOLINE: {0} {1:x} {2} {3} {4}", trampoline, token, klass, image, reader);

			if ((reader == null) || ((token & 0xff000000) != 0x06000000))
				throw new InternalError ();

			return reader.GetMethodByToken (token);
		}

		internal int LookupType (StackFrame frame, string name)
		{
			int offset;
			mutex.Lock ();
			offset = (int) frame.Process.CallMethod (info.LookupType, name).Address;
			mutex.Unlock ();
			return offset;
		}

		internal int LookupAssembly (Process process, string name)
		{
			int retval;
			mutex.Lock ();
			retval = (int) process.CallMethod (info.LookupAssembly, name).Address;
			mutex.Unlock ();
			return retval;
		}

		public void Notification (Inferior inferior, NotificationType type,
					  TargetAddress data, long arg)
		{
			switch (type) {
			case NotificationType.InitializeManagedCode:
				read_mono_debugger_info (inferior, inferior.Bfd);
				do_update_symbol_table (inferior, true);
				break;

			case NotificationType.ReloadSymtabs:
				do_update_symbol_table (inferior, true);
				break;

			case NotificationType.JitBreakpoint:
				if (!breakpoints.Contains ((int) arg))
					break;

				do_update_symbol_table (inferior, false);

				MyBreakpointHandle handle = (MyBreakpointHandle) breakpoints [(int) arg];
				handle.Handler (inferior, data, handle.UserData);
				breakpoints.Remove (arg);
				break;

			case NotificationType.MethodCompiled:
				do_update_symbol_table (inferior, false);
				break;

			default:
				Console.WriteLine ("Received unknown notification {0:x}",
						   (int) type);
				break;
			}
		}
	}
}
