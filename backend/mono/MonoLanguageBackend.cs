using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using C = Mono.CompilerServices.SymbolWriter;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend.Mono
{
	internal delegate void BreakpointHandler (Inferior inferior, TargetAddress address,
						  object user_data);

	// <summary>
	//   This class is the managed representation of the
	//   MonoDefaults struct (at least the types we're interested
	//   in) as defined in mono/metadata/class-internals.h.
	// </summary>
	internal class MonoBuiltinTypeInfo
	{
		public readonly MonoSymbolFile Corlib;
		public readonly MonoObjectType ObjectType;
		public readonly MonoFundamentalType ByteType;
		public readonly MonoVoidType VoidType;
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
		public readonly MonoFundamentalType DecimalType;
		public readonly MonoStringType StringType;
		public readonly MonoClassType ExceptionType;
		public readonly MonoClassType DelegateType;
		public readonly MonoClassType ArrayType;

		public MonoBuiltinTypeInfo (MonoSymbolFile corlib, TargetMemoryAccess memory)
		{
			this.Corlib = corlib;

			MonoLanguageBackend mono = corlib.MonoLanguage;

			ObjectType = MonoObjectType.Create (corlib, memory);
			VoidType = MonoVoidType.Create (corlib, memory);

			StringType = MonoStringType.Create (corlib, memory);

			BooleanType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Boolean);
			CharType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Char);
			ByteType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Byte);
			SByteType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.SByte);
			Int16Type = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Int16);
			UInt16Type = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.UInt16);
			Int32Type = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Int32);
			UInt32Type = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.UInt32);
			Int64Type = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Int64);
			UInt64Type = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.UInt64);
			SingleType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Single);
			DoubleType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Double);

			IntType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.IntPtr);
			UIntType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.UIntPtr);

			DecimalType = MonoFundamentalType.Create (
				corlib, memory, FundamentalKind.Decimal);
			Cecil.TypeDefinition decimal_type = corlib.ModuleDefinition.Types ["System.Decimal"];
			corlib.AddType (decimal_type, DecimalType);

			TargetAddress klass = corlib.MonoLanguage.MetadataHelper.GetArrayClass (memory);
			Cecil.TypeDefinition array_type = corlib.ModuleDefinition.Types ["System.Array"];
			ArrayType = mono.CreateCoreType (corlib, array_type, memory, klass);
			mono.AddCoreType (array_type, ArrayType, ArrayType, klass);

			klass = corlib.MonoLanguage.MetadataHelper.GetDelegateClass (memory);
			Cecil.TypeDefinition delegate_type = corlib.ModuleDefinition.Types ["System.Delegate"];
			DelegateType = new MonoClassType (corlib, delegate_type);
			mono.AddCoreType (delegate_type, DelegateType, DelegateType, klass);

			klass = corlib.MonoLanguage.MetadataHelper.GetExceptionClass (memory);
			Cecil.TypeDefinition exception_type = corlib.ModuleDefinition.Types ["System.Exception"];
			ExceptionType = mono.CreateCoreType (corlib, exception_type, memory, klass);
			mono.AddCoreType (exception_type, ExceptionType, ExceptionType, klass);
		}
	}

	internal abstract class MonoDataTable
	{
		public readonly TargetAddress TableAddress;
		TargetAddress first_chunk;
		TargetAddress current_chunk;
		int last_offset;

		protected MonoDataTable (TargetAddress address, TargetAddress first_chunk)
		{
			this.TableAddress = address;
			this.first_chunk = first_chunk;
			this.current_chunk = first_chunk;
		}

		public void Read (TargetMemoryAccess memory)
		{
			int address_size = memory.TargetMemoryInfo.TargetAddressSize;
			int header_size = 16 + address_size;

			if (first_chunk.IsNull) {
				first_chunk = memory.ReadAddress (TableAddress + 8);
				current_chunk = first_chunk;
			}

			if (current_chunk.IsNull)
				return;

		again:
			TargetReader reader = new TargetReader (
				memory.ReadMemory (current_chunk, header_size));

			reader.ReadInteger (); /* size */
			int allocated_size = reader.ReadInteger ();
			int current_offset = reader.ReadInteger ();
			reader.ReadInteger (); /* dummy */
			TargetAddress next = reader.ReadAddress ();

			read_data_items (memory, current_chunk + header_size,
					 last_offset, current_offset);

			last_offset = current_offset;

			if (!next.IsNull && (current_offset == allocated_size)) {
				current_chunk = next;
				last_offset = 0;
				goto again;
			}
		}

		void read_data_items (TargetMemoryAccess memory, TargetAddress address,
				      int start, int end)
		{
			TargetReader reader = new TargetReader (
				memory.ReadMemory (address + start, end - start));

			Report.Debug (DebugFlags.JitSymtab,
				      "READ DATA ITEMS: {0} {1} {2} - {3} {4}", address,
				      start, end, reader.BinaryReader.Position, reader.Size);

			while (reader.BinaryReader.Position + 4 < reader.Size) {
				int item_size = reader.BinaryReader.ReadInt32 ();
				if (item_size == 0)
					break;
				DataItemType item_type = (DataItemType)
					reader.BinaryReader.ReadInt32 ();

				long pos = reader.BinaryReader.Position;

				ReadDataItem (memory, item_type, reader);

				reader.BinaryReader.Position = pos + item_size;
			}
		}

		protected enum DataItemType {
			Unknown		= 0,
			Class,
			Method,
			DelegateInvoke
		}

		protected abstract void ReadDataItem (TargetMemoryAccess memory, DataItemType type,
						      TargetReader reader);

		protected virtual string MyToString ()
		{
			return "";
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}{4})", GetType (), TableAddress,
					      first_chunk, current_chunk, MyToString ());
		}
	}

	internal class MonoLanguageBackend : Language
	{
		Hashtable symfile_by_index;
		int last_num_symbol_files;
		Hashtable symfile_by_image_addr;
		Hashtable symfile_hash;
		Hashtable assembly_hash;
		Hashtable assembly_by_name;
		Hashtable class_hash;
		Dictionary<TargetAddress,MonoClassInfo> class_info_by_addr;
		MonoSymbolFile corlib;
		MonoBuiltinTypeInfo builtin_types;
		MonoFunctionType main_method;

		Hashtable data_tables;
		GlobalDataTable global_data_table;

		MetadataHelper runtime;

		Process process;
		MonoDebuggerInfo info;
		TargetAddress[] trampolines;
		bool initialized;

		public MonoLanguageBackend (Process process, MonoDebuggerInfo info)
		{
			this.process = process;
			this.info = info;
			data_tables = new Hashtable ();
		}

		public override string Name {
			get { return "Mono"; }
		}

		public override bool IsManaged {
			get { return true; }
		}

		internal MonoDebuggerInfo MonoDebuggerInfo {
			get { return info; }
		}

		internal MetadataHelper MetadataHelper {
			get { return runtime; }
		}

		internal MonoBuiltinTypeInfo BuiltinTypes {
			get { return builtin_types; }
		}

		internal override Process Process {
			get { return process; }
		}

		public override TargetInfo TargetInfo {
			get { return corlib.TargetMemoryInfo; }
		}

		internal TargetAddress[] Trampolines {
			get { return trampolines; }
		}

		internal bool IsTrampolineAddress (TargetAddress address)
		{
			foreach (TargetAddress trampoline in trampolines) {
				if (address == trampoline)
					return true;
			}

			return false;
		}

		internal bool IsDelegateTrampoline (TargetAddress address)
		{
			if (global_data_table == null)
				return false;

			return global_data_table.IsDelegateInvoke (address);
		}

		internal bool TryFindImage (Thread thread, string filename)
		{
			Cecil.AssemblyDefinition ass = Cecil.AssemblyFactory.GetAssembly (filename);
			if (ass == null)
				return false;

			MonoSymbolFile file = (MonoSymbolFile) assembly_hash [ass];
			if (file != null)
				return true;

			return true;
		}

		public TargetType LookupMonoType (Cecil.TypeReference type)
		{
			MonoSymbolFile file;

			Cecil.TypeDefinition typedef = type as Cecil.TypeDefinition;
			if (typedef != null) {
				file = (MonoSymbolFile) assembly_hash [type.Module.Assembly];
				if (file == null) {
					Console.WriteLine ("Type `{0}' from unknown assembly `{1}'",
							   type, type.Module.Assembly);
					return null;
				}

				return file.LookupMonoType (typedef);
			}

			Cecil.ArrayType array = type as Cecil.ArrayType;
			if (array != null) {
				TargetType element_type = LookupMonoType (array.ElementType);
				if (element_type == null)
					return null;

				return new MonoArrayType (element_type, array.Rank);
			}

			Cecil.ReferenceType reftype = type as Cecil.ReferenceType;
			if (reftype != null) {
				TargetType element_type = LookupMonoType (reftype.ElementType);
				if (element_type == null)
					return null;

				return new MonoPointerType (element_type);
			}

			Cecil.GenericParameter gen_param = type as Cecil.GenericParameter;
			if (gen_param != null)
				return new MonoGenericParameterType (this, gen_param.Name);

			Cecil.GenericInstanceType gen_inst = type as Cecil.GenericInstanceType;
			if (gen_inst != null) {
				TargetType[] args = new TargetType [gen_inst.GenericArguments.Count];
				for (int i = 0; i < args.Length; i++) {
					args [i] = LookupMonoType (gen_inst.GenericArguments [i]);
					if (args [i] == null)
						return null;
				}

				MonoClassType container = LookupMonoType (gen_inst.ElementType)
					as MonoClassType;
				if (container == null)
					return null;

				return new MonoGenericInstanceType (container, args, TargetAddress.Null);
			}

			int rank = 0;

			string full_name = type.FullName;
			int pos = full_name.IndexOf ('[');
			if (pos > 0) {
				string dim = full_name.Substring (pos);
				full_name = full_name.Substring (0, pos);

				if ((dim.Length < 2) || (dim [dim.Length - 1] != ']'))
					throw new ArgumentException ();
				for (int i = 1; i < dim.Length - 1; i++)
					if (dim [i] != ',')
						throw new ArgumentException ();

				rank = dim.Length - 1;
			}

			TargetType mono_type = LookupType (full_name);
			if (mono_type == null)
				return null;

			if (rank > 0)
				return new MonoArrayType (mono_type, rank);
			else
				return mono_type;
		}

		internal void AddCoreType (Cecil.TypeDefinition typedef, TargetType type,
					   TargetClassType klass, TargetAddress klass_address)
		{
			corlib.AddType (typedef, type);
			if (!class_hash.Contains (klass_address))
				class_hash.Add (klass_address, type);
		}

		public TargetType LookupMonoClass (TargetMemoryAccess target, TargetAddress klass_address)
		{
			return ReadClassInfo (target, klass_address).RealType;
		}

		Cecil.AssemblyDefinition resolve_cecil_asm_name (Cecil.AssemblyNameReference name)
		{
			foreach (MonoSymbolFile symfile in symfile_hash.Values) {
				if (name.FullName == symfile.Assembly.Name.FullName)
					return symfile.Assembly;
			}

			return null;
		}

		Cecil.TypeDefinition resolve_cecil_type_ref (Cecil.TypeReference type)
		{
			type = type.GetOriginalType ();

			if (type is Cecil.TypeDefinition)
				return (Cecil.TypeDefinition) type;

			Cecil.AssemblyNameReference reference = type.Scope as Cecil.AssemblyNameReference;
			if (reference != null) {
				Cecil.AssemblyDefinition assembly = resolve_cecil_asm_name (reference);
				if (assembly == null)
					return null;

				return assembly.MainModule.Types [type.FullName];
			}

			Cecil.ModuleDefinition module = type.Scope as Cecil.ModuleDefinition;
			if (module != null)
				return module.Types [type.FullName];

			throw new NotImplementedException ();
		}

		public override bool IsExceptionType (TargetClassType ctype)
		{
			MonoClassType mono_type = ctype as MonoClassType;
			if (mono_type == null)
				return false;

			Cecil.TypeDefinition exc_type = builtin_types.ExceptionType.Type;
			Cecil.TypeDefinition type = mono_type.Type;

			while (type != null) {
				if (exc_type == type)
					return true;

				type = resolve_cecil_type_ref (type.BaseType);
			}

			return false;
		}

		public MonoSymbolFile GetImage (TargetAddress address)
		{
			return (MonoSymbolFile) symfile_by_image_addr [address];
		}

		internal MonoSymbolFile GetSymbolFile (int index)
		{
			return (MonoSymbolFile) symfile_by_index [index];
		}

		void read_mono_debugger_info (TargetMemoryAccess memory)
		{
			Console.WriteLine ("READ DEBUGGER INFO: {0}", info.MonoMetadataInfo);

			runtime = MetadataHelper.Create (memory, info);

			trampolines = new TargetAddress [info.MonoTrampolineNum];

			TargetAddress address = info.MonoTrampolineCode;
			for (int i = 0; i < trampolines.Length; i++) {
				trampolines [i] = memory.ReadAddress (address);
				address += memory.TargetMemoryInfo.TargetAddressSize;
			}

			symfile_by_index = new Hashtable ();
			symfile_by_image_addr = new Hashtable ();
			symfile_hash = new Hashtable ();
			assembly_hash = new Hashtable ();
			assembly_by_name = new Hashtable ();
			class_hash = new Hashtable ();
			class_info_by_addr = new Dictionary<TargetAddress,MonoClassInfo> ();
		}

		void reached_main (TargetMemoryAccess target, TargetAddress method)
		{
			main_method = ReadMonoMethod (target, method);
		}

		internal MonoFunctionType ReadMonoMethod (TargetMemoryAccess memory,
							  TargetAddress address)
		{
			int token = MetadataHelper.MonoMethodGetToken (memory, address);
			TargetAddress klass = MetadataHelper.MonoMethodGetClass (memory, address);
			TargetAddress image = MetadataHelper.MonoClassGetMonoImage (memory, klass);

			MonoSymbolFile file = GetImage (image);
			if (file == null)
				return null;

			return file.GetFunctionByToken (token);
		}

		public TargetType ReadMonoClass (TargetMemoryAccess target, TargetAddress klass)
		{
			TargetAddress byval_type = MetadataHelper.MonoClassGetByValType (target, klass);
			return ReadType (target, byval_type);
		}

		public TargetType ReadType (TargetMemoryAccess memory, TargetAddress address)
		{
			TargetAddress data = MetadataHelper.MonoTypeGetData (memory, address);
			MonoTypeEnum type = MetadataHelper.MonoTypeGetType (memory, address);
			bool byref = MetadataHelper.MonoTypeGetIsByRef (memory, address);

			TargetType target_type = ReadType (memory, type, data);
			if (target_type == null)
				return null;

			if (byref)
				target_type = new MonoPointerType (target_type);

			return target_type;
		}

		public TargetClassType ReadStructType (TargetMemoryAccess memory, TargetAddress address)
		{
			TargetAddress data = MetadataHelper.MonoTypeGetData (memory, address);
			MonoTypeEnum type = MetadataHelper.MonoTypeGetType (memory, address);

			return (TargetClassType) ReadType (memory, type, data);
		}

		TargetType ReadType (TargetMemoryAccess memory, MonoTypeEnum type, TargetAddress data)
		{
			switch (type) {
			case MonoTypeEnum.MONO_TYPE_BOOLEAN:
				return BuiltinTypes.BooleanType;
			case MonoTypeEnum.MONO_TYPE_CHAR:
				return BuiltinTypes.CharType;
			case MonoTypeEnum.MONO_TYPE_I1:
				return BuiltinTypes.SByteType;
			case MonoTypeEnum.MONO_TYPE_U1:
				return BuiltinTypes.ByteType;
			case MonoTypeEnum.MONO_TYPE_I2:
				return BuiltinTypes.Int16Type;
			case MonoTypeEnum.MONO_TYPE_U2:
				return BuiltinTypes.UInt16Type;
			case MonoTypeEnum.MONO_TYPE_I4:
				return BuiltinTypes.Int32Type;
			case MonoTypeEnum.MONO_TYPE_U4:
				return BuiltinTypes.UInt32Type;
			case MonoTypeEnum.MONO_TYPE_I8:
				return BuiltinTypes.Int64Type;
			case MonoTypeEnum.MONO_TYPE_U8:
				return BuiltinTypes.UInt64Type;
			case MonoTypeEnum.MONO_TYPE_R4:
				return BuiltinTypes.SingleType;
			case MonoTypeEnum.MONO_TYPE_R8:
				return BuiltinTypes.DoubleType;
			case MonoTypeEnum.MONO_TYPE_STRING:
				return BuiltinTypes.StringType;
			case MonoTypeEnum.MONO_TYPE_OBJECT:
				return BuiltinTypes.ObjectType;
			case MonoTypeEnum.MONO_TYPE_I:
				return BuiltinTypes.IntType;
			case MonoTypeEnum.MONO_TYPE_U:
				return BuiltinTypes.UIntType;
			case MonoTypeEnum.MONO_TYPE_VOID:
				return BuiltinTypes.VoidType;

			case MonoTypeEnum.MONO_TYPE_PTR: {
				TargetType target_type = ReadType (memory, data);
				return new MonoPointerType (target_type);
			}

			case MonoTypeEnum.MONO_TYPE_VALUETYPE:
			case MonoTypeEnum.MONO_TYPE_CLASS:
				return LookupMonoClass (memory, data);

			case MonoTypeEnum.MONO_TYPE_SZARRAY: {
				TargetType etype = ReadMonoClass (memory, data);
				return new MonoArrayType (etype, 1);
			}

			case MonoTypeEnum.MONO_TYPE_ARRAY: {
				TargetAddress klass = MetadataHelper.MonoArrayTypeGetClass (memory, data);
				int rank = MetadataHelper.MonoArrayTypeGetRank (memory, data);

				MetadataHelper.MonoArrayTypeGetBounds (memory, data);

				TargetType etype = ReadMonoClass (memory, klass);
				return new MonoArrayType (etype, rank);
			}

			case MonoTypeEnum.MONO_TYPE_GENERICINST:
				return ReadGenericClass (memory, data, true);

			case MonoTypeEnum.MONO_TYPE_VAR:
			case MonoTypeEnum.MONO_TYPE_MVAR: {
				MetadataHelper.GenericParamInfo info = MetadataHelper.GetGenericParameter (
					memory, data);

				return new MonoGenericParameterType (this, info.Name);
			}

			default:
				Report.Error ("UNKNOWN TYPE: {0}", type);
				return null;
			}
		}

		public TargetType ReadGenericClass (TargetMemoryAccess memory, TargetAddress address,
						    bool handle_nullable)
		{
			MetadataHelper.GenericClassInfo info = MetadataHelper.GetGenericClass (memory, address);
			if (info == null)
				return null;

			TargetType[] args = new TargetType [info.TypeArguments.Length];

			for (int i = 0; i < info.TypeArguments.Length; i++) {
				args [i] = ReadType (memory, info.TypeArguments [i]);
				if (args [i] == null)
					return null;
			}

			MonoClassType container = LookupMonoClass (memory, info.ContainerClass)
				as MonoClassType;
			if (container == null)
				return null;

			if (handle_nullable && (container.Type.FullName == "System.Nullable`1"))
				return new MonoNullableType (args [0]);

			return new MonoGenericInstanceType (container, args, info.KlassPtr);
		}

		internal MonoFunctionType MainMethod {
			get { return main_method; }
		}

#region symbol table management
		internal void Update (TargetMemoryAccess target)
		{
			Report.Debug (DebugFlags.JitSymtab, "Update requested");
			if (initialized) {
				DateTime start = DateTime.Now;
				++data_table_count;
				foreach (MonoDataTable table in data_tables.Values)
					table.Read (target);
				foreach (MonoSymbolFile symfile in symfile_by_index.Values)
					symfile.TypeTable.Read (target);
				global_data_table.Read (target);
				data_table_time += DateTime.Now - start;
			}
		}

		void read_symbol_table (TargetMemoryAccess memory)
		{
			if (initialized)
				throw new InternalError ();

			Report.Debug (DebugFlags.JitSymtab, "Starting to read symbol table");
			try {
				DateTime start = DateTime.Now;
				++full_update_count;
				do_read_symbol_table (memory);
				update_time += DateTime.Now - start;
			} catch (ThreadAbortException) {
				return;
			} catch (SymbolTableException ex) {
				Console.WriteLine ("Can't read symbol table: {0} {1}",
						   memory, ex.Message);
				return;
			} catch (Exception e) {
				Console.WriteLine ("Can't read symbol table: {0} {1} {2}",
						   memory, e, Environment.StackTrace);
				return;
			}
			Report.Debug (DebugFlags.JitSymtab, "Done reading symbol table");
			initialized = true;
		}

		void read_builtin_types (TargetMemoryAccess memory)
		{
			if (initialized)
				builtin_types = new MonoBuiltinTypeInfo (corlib, memory);
		}

		MonoSymbolFile load_symfile (TargetMemoryAccess memory, TargetAddress address)
		{
			MonoSymbolFile symfile = null;

			Console.WriteLine ("LOAD SYMFILE: {0}", address);

			if (symfile_hash.Contains (address))
				return (MonoSymbolFile) symfile_hash [address];

			try {
				symfile = new MonoSymbolFile (this, process, memory, address);
			} catch (C.MonoSymbolFileException ex) {
				Console.WriteLine (ex.Message);
			} catch (SymbolTableException ex) {
				Console.WriteLine (ex.Message);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}

			symfile_hash.Add (address, symfile);

			if (symfile == null)
				return null;

			if (!assembly_by_name.Contains (symfile.Assembly.Name.FullName)) {
				assembly_hash.Add (symfile.Assembly, symfile);
				assembly_by_name.Add (symfile.Assembly.Name.FullName, symfile);
			}

			symfile_by_image_addr.Add (symfile.MonoImage, symfile);
			symfile_by_index.Add (symfile.Index, symfile);

			return symfile;
		}

		void close_symfile (MonoSymbolFile symfile)
		{
			symfile_by_image_addr.Remove (symfile.MonoImage);
			assembly_hash.Remove (symfile.Assembly);
			assembly_by_name.Remove (symfile.Assembly.Name.FullName);
			symfile_by_index.Remove (symfile.Index);
		}

		// This method reads the MonoDebuggerSymbolTable structure
		// (struct definition is in mono-debug-debugger.h)
		void do_read_symbol_table (TargetMemoryAccess memory)
		{
			TargetAddress symtab_address = memory.ReadAddress (info.SymbolTable);
			if (symtab_address.IsNull)
				throw new SymbolTableException ("Symbol table is null.");

			TargetReader header = new TargetReader (
				memory.ReadMemory (symtab_address, info.SymbolTableSize));

			long magic = header.BinaryReader.ReadInt64 ();
			if (magic != MonoDebuggerInfo.DynamicMagic)
				throw new SymbolTableException (
					"Debugger symbol table has unknown magic {0:x}.", magic);

			int version = header.ReadInteger ();
			if (version < MonoDebuggerInfo.MinDynamicVersion)
				throw new SymbolTableException (
					"Debugger symbol table has version {0}, but " +
					"expected at least {1}.", version,
					MonoDebuggerInfo.MinDynamicVersion);
			if (version > MonoDebuggerInfo.MaxDynamicVersion)
				throw new SymbolTableException (
					"Debugger symbol table has version {0}, but " +
					"expected at most {1}.", version,
					MonoDebuggerInfo.MaxDynamicVersion);

			int total_size = header.ReadInteger ();
			if (total_size != info.SymbolTableSize)
				throw new SymbolTableException (
					"Debugger symbol table has size {0}, but " +
					"expected {1}.", total_size, info.SymbolTableSize);

			TargetAddress corlib_address = header.ReadAddress ();
			TargetAddress global_data_table_ptr = header.ReadAddress ();
			TargetAddress data_table_list = header.ReadAddress ();

			TargetAddress symfile_by_index = header.ReadAddress ();

			if (corlib_address.IsNull)
				throw new SymbolTableException ("Corlib address is null.");
			corlib = load_symfile (memory, corlib_address);
			if (corlib == null)
				throw new SymbolTableException ("Cannot read corlib!");

			TargetAddress ptr = symfile_by_index;
			while (!ptr.IsNull) {
				TargetAddress next_ptr = memory.ReadAddress (ptr);
				TargetAddress address = memory.ReadAddress (
					ptr + memory.TargetMemoryInfo.TargetAddressSize);

				ptr = next_ptr;
				load_symfile (memory, address);
			}

			ptr = data_table_list;
			while (!ptr.IsNull) {
				TargetAddress next_ptr = memory.ReadAddress (ptr);
				TargetAddress address = memory.ReadAddress (
					ptr + memory.TargetMemoryInfo.TargetAddressSize);

				ptr = next_ptr;
				add_data_table (memory, address);
			}

			global_data_table = new GlobalDataTable (this, global_data_table_ptr);
		}

		void add_data_table (TargetMemoryAccess memory, TargetAddress ptr)
		{
			int table_size = 8 + 2 * memory.TargetMemoryInfo.TargetAddressSize;

			TargetReader reader = new TargetReader (memory.ReadMemory (ptr, table_size));

			int domain = reader.ReadInteger ();
			reader.Offset += 4;

			DomainDataTable table = (DomainDataTable) data_tables [domain];
			if (table == null) {
				TargetAddress first_chunk = reader.ReadAddress ();
				table = new DomainDataTable (this, domain, ptr, first_chunk);
				data_tables.Add (domain, table);
			}
		}

		void destroy_data_table (int domain, TargetAddress table)
		{
			data_tables.Remove (domain);
		}

		protected class DomainDataTable : MonoDataTable
		{
			public readonly int Domain;
			public readonly MonoLanguageBackend Mono;

			public DomainDataTable (MonoLanguageBackend mono, int domain,
						TargetAddress address, TargetAddress first_chunk)
				: base (address, first_chunk)
			{
				this.Mono = mono;
				this.Domain = domain;
			}

			protected override void ReadDataItem (TargetMemoryAccess memory,
							      DataItemType type, TargetReader reader)
			{
				if (type != DataItemType.Method)
					throw new InternalError (
						"Got unknown data item: {0}", type);

				int size = reader.BinaryReader.PeekInt32 ();
				byte[] contents = reader.BinaryReader.PeekBuffer (size);
				reader.BinaryReader.ReadInt32 ();
				int file_idx = reader.BinaryReader.ReadInt32 ();
				Report.Debug (DebugFlags.JitSymtab, "READ RANGE ITEM: {0} {1}",
					      size, file_idx);
				MonoSymbolFile file = Mono.GetSymbolFile (file_idx);
				if (file != null)
					file.AddRangeEntry (memory, reader, contents);
			}

			protected override string MyToString ()
			{
				return String.Format (":{0}", Domain);
			}
		}

		protected class GlobalDataTable : MonoDataTable
		{
			public readonly MonoLanguageBackend Mono;
			ArrayList delegate_impl_list;

			public GlobalDataTable (MonoLanguageBackend mono, TargetAddress address)
				: base (address, TargetAddress.Null)
			{
				this.Mono = mono;

				delegate_impl_list = new ArrayList ();
			}

			protected override void ReadDataItem (TargetMemoryAccess memory,
							      DataItemType type, TargetReader reader)
			{
				if (type != DataItemType.DelegateInvoke)
					throw new InternalError (
						"Got unknown data item: {0}", type);

				TargetAddress code = reader.ReadAddress ();
				int size = reader.BinaryReader.ReadInt32 ();
				Report.Debug (DebugFlags.JitSymtab, "READ DELEGATE IMPL: {0} {1}",
					      code, size);
				delegate_impl_list.Add (new DelegateInvokeEntry (code, size));
			}

			public bool IsDelegateInvoke (TargetAddress address)
			{
				foreach (DelegateInvokeEntry entry in delegate_impl_list) {
					if ((address >= entry.Code) && (address < entry.Code + entry.Size))
						return true;
				}

				return false;
			}

			struct DelegateInvokeEntry {
				public readonly TargetAddress Code;
				public readonly int Size;

				public DelegateInvokeEntry (TargetAddress code, int size)
				{
					this.Code = code;
					this.Size = size;
				}
			}
		}

		static int manual_update_count;
		static int full_update_count;
		static int update_count;
		static int data_table_count;
		static TimeSpan data_table_time;
		static TimeSpan update_time;
		static int range_entry_count;
		static TimeSpan range_entry_time;
		static TimeSpan range_entry_method_time;

		public static void RangeEntryCreated (TimeSpan time)
		{
			range_entry_count++;
			range_entry_time += time;
		}

		public static void RangeEntryGetMethod (TimeSpan time)
		{
			range_entry_method_time += time;
		}

		internal MonoClassInfo ReadClassInfo (TargetMemoryAccess memory, TargetAddress klass)
		{
			if (class_info_by_addr.ContainsKey (klass))
				return class_info_by_addr [klass];

			MonoClassInfo info = MonoClassInfo.ReadClassInfo (this, memory, klass);
			class_info_by_addr.Add (klass, info);
			return info;
		}

		internal MonoClassType CreateCoreType (MonoSymbolFile file, Cecil.TypeDefinition typedef,
						       TargetMemoryAccess memory, TargetAddress klass)
		{
			MonoClassType type;
			MonoClassInfo info = MonoClassInfo.ReadCoreType (
				file, typedef, memory, klass, out type);
			class_info_by_addr.Add (klass, info);

			return type;
		}
#endregion

#region Class Init Handlers

		static int next_unique_id;

		internal static int GetUniqueID ()
		{
			return ++next_unique_id;
		}

#endregion

#region Method Load Handlers

		Hashtable method_load_handlers = new Hashtable ();

		void method_from_jit_info (TargetAccess target, TargetAddress data,
					   MethodLoadedHandler handler)
		{
			int size = target.ReadInteger (data);
			TargetReader reader = new TargetReader (target.ReadMemory (data, size));

			reader.BinaryReader.ReadInt32 ();
			int count = reader.BinaryReader.ReadInt32 ();

			for (int i = 0; i < count; i++) {
				TargetAddress address = reader.ReadAddress ();
				Method method = read_range_entry (target, address);

				handler (target, method);
			}
		}

		Method read_range_entry (TargetMemoryAccess target, TargetAddress address)
		{
			int size = target.ReadInteger (address);
			TargetReader reader = new TargetReader (target.ReadMemory (address, size));

			byte[] contents = reader.BinaryReader.PeekBuffer (size);

			reader.BinaryReader.ReadInt32 ();
			int file_idx = reader.BinaryReader.ReadInt32 ();
			MonoSymbolFile file = (MonoSymbolFile) symfile_by_index [file_idx];

			return file.ReadRangeEntry (target, reader, contents);
		}

		internal void RegisterMethodLoadHandler (TargetAccess target, TargetAddress info, int index,
							 MethodLoadedHandler handler)
		{
			if (!info.IsNull)
				method_from_jit_info (target, info, handler);
			else
				method_load_handlers.Add (index, handler);
		}

		internal void RemoveMethodLoadHandler (Thread thread, int index)
		{
			if (!thread.CurrentFrame.Language.IsManaged)
				throw new TargetException (TargetError.InvalidContext);

			thread.CallMethod (info.RemoveBreakpoint, index, 0);
			method_load_handlers.Remove (index);
		}

		internal void RemoveMethodLoadHandler (int index)
		{
			method_load_handlers.Remove (index);
		}

		internal int RegisterMethodLoadHandler (Thread thread, MonoFunctionType func,
							FunctionBreakpointHandle handle)
		{
			if (method_load_handlers.Contains (handle.Index))
				return handle.Index;

			if (!thread.CurrentFrame.Language.IsManaged)
				throw new TargetException (TargetError.InvalidContext);

			TargetAddress retval = thread.CallMethod (
				info.InsertSourceBreakpoint, func.SymbolFile.MonoImage,
				func.Token, handle.Index, func.DeclaringType.BaseName);

			MethodLoadedHandler handler = handle.MethodLoaded;

			if (!retval.IsNull) {
				thread.ThreadServant.DoTargetAccess (
					delegate (TargetMemoryAccess target)  {
						method_from_jit_info ((TargetAccess) target,
								      retval, handler);
						return null;
				});
			}

			method_load_handlers.Add (handle.Index, handler);
			return handle.Index;
		}
#endregion

#region Language implementation
		public override string SourceLanguage (StackFrame frame)
		{
			return "";
		}

		public override TargetType LookupType (string name)
		{
			switch (name) {
			case "short":   name = "System.Int16";   break;
			case "ushort":  name = "System.UInt16";  break;
			case "int":     name = "System.Int32";   break;
			case "uint":    name = "System.UInt32";  break;
			case "long":    name = "System.Int64";   break;
			case "ulong":   name = "System.UInt64";  break;
			case "float":   name = "System.Single";  break;
			case "double":  name = "System.Double";  break;
			case "char":    name = "System.Char";    break;
			case "byte":    name = "System.Byte";    break;
			case "sbyte":   name = "System.SByte";   break;
			case "object":  name = "System.Object";  break;
			case "string":  name = "System.String";  break;
			case "bool":    name = "System.Boolean"; break;
			case "void":    name = "System.Void";    break;
			case "decimal": name = "System.Decimal"; break;
			}

			if (name.IndexOf ('[') >= 0)
				return null;

			foreach (MonoSymbolFile symfile in symfile_by_index.Values) {
				try {
					Cecil.TypeDefinitionCollection types = symfile.Assembly.MainModule.Types;
					// FIXME: Work around an API problem in Cecil.
					foreach (Cecil.TypeDefinition type in types) {
						if (type.FullName != name)
							continue;

						return symfile.LookupMonoType (type);
					}
				} catch {
				}
			}

			return null;
		}

		TargetFundamentalType GetFundamentalType (Type type)
		{
			switch (Type.GetTypeCode (type)) {
			case TypeCode.Boolean:
				return builtin_types.BooleanType;
			case TypeCode.Char:
				return builtin_types.CharType;
			case TypeCode.SByte:
				return builtin_types.SByteType;
			case TypeCode.Byte:
				return builtin_types.ByteType;
			case TypeCode.Int16:
				return builtin_types.Int16Type;
			case TypeCode.UInt16:
				return builtin_types.UInt16Type;
			case TypeCode.Int32:
				return builtin_types.Int32Type;
			case TypeCode.UInt32:
				return builtin_types.UInt32Type;
			case TypeCode.Int64:
				return builtin_types.Int64Type;
			case TypeCode.UInt64:
				return builtin_types.UInt64Type;
			case TypeCode.Single:
				return builtin_types.SingleType;
			case TypeCode.Double:
				return builtin_types.DoubleType;
			case TypeCode.String:
				return builtin_types.StringType;
			case TypeCode.Object:
				if (type == typeof (IntPtr))
					return builtin_types.IntType;
				else if (type == typeof (UIntPtr))
					return builtin_types.UIntType;
				return null;
			case TypeCode.Decimal:
				return builtin_types.DecimalType;

			default:
				return null;
			}
		}

		public override bool CanCreateInstance (Type type)
		{
			return GetFundamentalType (type) != null;
		}

		public override TargetFundamentalObject CreateInstance (Thread thread, object obj)
		{
			TargetFundamentalType type = GetFundamentalType (obj.GetType ());
			if (type == null)
				return null;

			return type.CreateInstance (thread, obj);
		}

		public override TargetPointerObject CreatePointer (StackFrame frame, TargetAddress address)
		{
			return process.NativeLanguage.CreatePointer (frame, address);
		}

		public override TargetObject CreateObject (Thread thread, TargetAddress address)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					return CreateObject (target, address);
			});
		}

		internal TargetObject CreateObject (TargetMemoryAccess target, TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (address);
			MonoObjectObject obj = (MonoObjectObject)builtin_types.ObjectType.GetObject (
				target, location);
			if (obj == null)
				return null;

			TargetObject result;
			try {
				result = obj.GetDereferencedObject (target);
				if (result == null)
					result = obj;
			} catch {
				result = obj;
			}

			return result;
		}

		public override TargetObject CreateNullObject (Thread target, TargetType type)
		{
			return new TargetNullObject (type);
		}

		public override TargetObjectObject CreateBoxedObject (Thread thread, TargetObject value)
		{
			TargetAddress klass;

			if ((value is MonoClassObject)  && !value.Type.IsByRef)
				klass = ((MonoClassObject) value).KlassAddress;
			else if (value is TargetFundamentalObject) {
				MonoClassType ctype = ((MonoFundamentalType) value.Type).MonoClassType;
				MonoClassInfo info = (MonoClassInfo) thread.ThreadServant.DoTargetAccess (
					delegate (TargetMemoryAccess target) {
						return ctype.ResolveClass (target, true);
					});
				klass = info.KlassAddress;
			} else
				throw new InvalidOperationException ();

			if (!thread.CurrentFrame.Language.IsManaged)
				throw new TargetException (TargetError.InvalidContext);

			TargetAddress boxed = thread.CallMethod (MonoDebuggerInfo.GetBoxedObjectMethod, klass, value);
			if (boxed.IsNull)
				return null;

			return new MonoObjectObject (builtin_types.ObjectType, new AbsoluteTargetLocation (boxed));
		}

		public override TargetPointerType CreatePointerType (TargetType type)
		{
			return null;
		}

		public override TargetFundamentalType IntegerType {
			get { return builtin_types.Int32Type; }
		}

		public override TargetFundamentalType LongIntegerType {
			get { return builtin_types.Int64Type; }
		}

		public override TargetFundamentalType StringType {
			get { return builtin_types.StringType; }
		}

		public override TargetType PointerType {
			get { return builtin_types.IntType; }
		}

		public override TargetType VoidType {
			get { return builtin_types.VoidType; }
		}

		public override TargetClassType ExceptionType {
			get { return builtin_types.ExceptionType; }
		}

		public override TargetClassType DelegateType {
			get { return builtin_types.DelegateType; }
		}

		public override TargetClassType ObjectType {
			get { return builtin_types.ObjectType.ClassType; }
		}

		public override TargetClassType ArrayType {
			get { return builtin_types.ArrayType; }
		}
#endregion

		public TargetAddress RuntimeInvokeFunc {
			get { return info.RuntimeInvoke; }
		}

		public MethodSource GetTrampoline (TargetMemoryAccess memory,
						   TargetAddress address)
		{
#if FIXME
			int insn_size;
			TargetAddress target;
			CallTargetType type = memory.Architecture.GetCallTarget (
				memory, address, out target, out insn_size);
			if (type != CallTargetType.MonoTrampoline)
				return null;

			int token = memory.ReadInteger (target + 4);
			TargetAddress klass = memory.ReadAddress (target + 8);
			TargetAddress image = memory.ReadAddress (klass);

			foreach (MonoSymbolFile file in symfile_by_index.Values) {
				if (file.MonoImage != image)
					continue;

				return file.GetMethodByToken (token);
			}
#endif

			return null;
		}

		void JitBreakpoint (Inferior inferior, int idx, TargetAddress data)
		{
			Method method = read_range_entry (inferior, data);
			if (method == null)
				return;

			MethodLoadedHandler handler = (MethodLoadedHandler) method_load_handlers [idx];
			if (handler != null)
				handler (inferior, method);
		}

		internal void Initialize (TargetMemoryAccess memory)
		{
			Report.Debug (DebugFlags.JitSymtab, "Initialize mono language");
		}

		internal void InitializeCoreFile (TargetMemoryAccess memory)
		{
			Report.Debug (DebugFlags.JitSymtab, "Initialize mono language");
			read_mono_debugger_info (memory);
			read_symbol_table (memory);
			read_builtin_types (memory);
		}

		internal void InitializeAttach (TargetMemoryAccess memory)
		{
			Report.Debug (DebugFlags.JitSymtab, "Initialize mono language");
			read_mono_debugger_info (memory);
			read_symbol_table (memory);
			read_builtin_types (memory);
		}

		Dictionary<int,MetadataHelper.AppDomainInfo> appdomain_info = new Dictionary<int,MetadataHelper.AppDomainInfo> ();

		void create_appdomain (TargetMemoryAccess memory, TargetAddress address)
		{
			int addr_size = memory.TargetMemoryInfo.TargetAddressSize;
			TargetReader reader = new TargetReader (
				memory.ReadMemory (address, 8 + 3 * addr_size));

			int id = reader.BinaryReader.ReadInt32 ();
			int shadow_path_len = reader.BinaryReader.ReadInt32 ();
			TargetAddress shadow_path_addr = reader.ReadAddress ();

			string shadow_path = null;
			if (!shadow_path_addr.IsNull) {
				byte[] buffer = memory.ReadBuffer (shadow_path_addr, shadow_path_len);
				char[] cbuffer = new char [buffer.Length];
				for (int i = 0; i < buffer.Length; i++)
					cbuffer [i] = (char) buffer [i];
				shadow_path = new String (cbuffer);
			}

			TargetAddress domain = reader.ReadAddress ();
			TargetAddress setup = reader.ReadAddress ();

			MetadataHelper.AppDomainInfo info = MetadataHelper.GetAppDomainInfo (this, memory, setup);
			info.ShadowCopyPath = shadow_path;

			appdomain_info.Add (id, info);
		}

		void unload_appdomain (int id)
		{
			appdomain_info.Remove (id);
		}

		bool is_shadow_copy_path (string path)
		{
			foreach (MetadataHelper.AppDomainInfo info in appdomain_info.Values) {
				if (!info.ShadowCopyFiles || (info.ShadowCopyPath == null))
					continue;

				if (path.StartsWith (info.ShadowCopyPath))
					return true;
			}

			return false;
		}

		internal string GetShadowCopyLocation (string path)
		{
			if (!is_shadow_copy_path (path))
				return null;

			string shadow_ini_path = Path.Combine (Path.GetDirectoryName (path), "__AssemblyInfo__.ini");
			if (!File.Exists (shadow_ini_path))
				return null;

			try {
				using (StreamReader ini_reader = File.OpenText (shadow_ini_path))
					return ini_reader.ReadToEnd ();
			} catch {
				return null;
			}
		}

		public bool Notification (SingleSteppingEngine engine, Inferior inferior,
					  NotificationType type, TargetAddress data, long arg)
		{
			switch (type) {
			case NotificationType.InitializeCorlib:
				Report.Debug (DebugFlags.JitSymtab, "Initialize corlib");
				read_mono_debugger_info (inferior);
				read_symbol_table (inferior);
				read_builtin_types (inferior);
				break;

			case NotificationType.InitializeManagedCode:
				Report.Debug (DebugFlags.JitSymtab, "Initialize managed code");
				reached_main (inferior, data);
				break;

			case NotificationType.LoadModule: {
				MonoSymbolFile symfile = load_symfile (inferior, data);
				Report.Debug (DebugFlags.JitSymtab,
					      "Module load: {0} {1}", data, symfile);
				if (symfile == null)
					break;
				engine.Process.Debugger.OnModuleLoadedEvent (symfile.Module);
				if ((builtin_types != null) && (symfile != null)) {
					if (engine.OnModuleLoaded (symfile.Module))
						return false;
				}
				break;
			}

			case NotificationType.UnloadModule: {
				Report.Debug (DebugFlags.JitSymtab,
					      "Module unload: {0} {1}", data, arg);

				MonoSymbolFile symfile = (MonoSymbolFile) symfile_by_index [(int) arg];
				if (symfile == null)
					break;

				engine.Process.Debugger.OnModuleUnLoadedEvent (symfile.Module);
				close_symfile (symfile);
				break;
			}

			case NotificationType.JitBreakpoint:
				JitBreakpoint (inferior, (int) arg, data);
				break;

			case NotificationType.DomainCreate:
				Report.Debug (DebugFlags.JitSymtab,
					      "Domain create: {0}", data);
				add_data_table (inferior, data);
				break;

			case NotificationType.DomainUnload:
				Report.Debug (DebugFlags.JitSymtab,
					      "Domain unload: {0} {1:x}", data, arg);
				destroy_data_table ((int) arg, data);
				engine.Process.BreakpointManager.DomainUnload (inferior, (int) arg);
				break;

			case NotificationType.ClassInitialized:
				break;

			case NotificationType.CreateAppDomain:
				create_appdomain (inferior, data);
				break;

			case NotificationType.UnloadAppDomain:
				unload_appdomain ((int) arg);
				break;

			default:
				Console.WriteLine ("Received unknown notification {0:x} / {1} {2:x}",
						   (int) type, data, arg);
				break;
			}

			return true;
		}

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			lock (this) {
				if (disposed)
					return;
			  
				disposed = true;
			}

			if (disposing) {
				if (symfile_hash != null) {
					foreach (MonoSymbolFile symfile in symfile_hash.Values)
						symfile.Dispose();

					symfile_hash = null;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~MonoLanguageBackend ()
		{
			Dispose (false);
		}

	}
}
