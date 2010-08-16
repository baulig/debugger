using System;
using System.IO;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Server
{
	internal class RemoteExecutableReader : ExecutableReader
	{
		OperatingSystemBackend os;
		TargetMemoryInfo memory_info;
		RemoteDebuggerServer server;
		string target_name;
		Module module;
		string file;
		int iid;

		DwarfReader dwarf;
		StabsReader stabs;
		RemoteSymbolFile symfile;

		ArrayList simple_symbols;
		RemoteSymbolTable simple_symtab;

		bool dwarf_supported;
		bool stabs_supported;

		TargetAddress start_address = TargetAddress.Null;
		TargetAddress end_address = TargetAddress.Null;
		TargetAddress base_address = TargetAddress.Null;

		public RemoteExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory_info,
					       RemoteDebuggerServer server, string file)
		{
			this.os = os;
			this.memory_info = memory_info;
			this.server = server;
			this.file = file;

			iid = server.Connection.CreateExeReader (file);
			target_name = server.Connection.BfdGetTargetName (iid);

			if (DwarfReader.IsSupported (this))
				dwarf_supported = true;
			else if (StabsReader.IsSupported (this))
				stabs_supported = true;

			symfile = new RemoteSymbolFile (this);

			module = os.Process.Session.GetModule (file);
			if (module == null) {
				module = os.Process.Session.CreateModule (file, symfile);
			} else {
				module.LoadModule (symfile);
			}

			Console.WriteLine ("TEST: {0} {1}", module, module.Language != null);

			os.Process.SymbolTableManager.AddSymbolFile (symfile);

		}

		public override TargetMemoryInfo TargetMemoryInfo {
			get { return memory_info; }
		}

		public override Module Module {
			get { return module; }
		}

		public override string FileName {
			get { return file; }
		}

		public override bool IsLoaded {
			get { return true; }
		}

		public override string TargetName {
			get { return target_name; }
		}

		public OperatingSystemBackend OperatingSystem {
			get { return os; }
		}

		protected bool HasDebuggingInfo {
			get { return dwarf_supported || stabs_supported; }
		}

		TargetAddress create_address (long addr)
		{
			return addr != 0 ? new TargetAddress (memory_info.AddressDomain, addr) : TargetAddress.Null;
		}

		public override bool IsContinuous {
			get { return !end_address.IsNull; }
		}

		public override TargetAddress StartAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return start_address;
			}
		}

		public override TargetAddress EndAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return end_address;
			}
		}

		public override TargetAddress BaseAddress {
			get { return base_address; }
		}

		public override TargetAddress LookupSymbol (string name)
		{
			Console.WriteLine ("LOOKUP SYMBOL: {0}", name);
			var addr = server.Connection.BfdLookupSymbol (iid, name);
			Console.WriteLine ("LOOKUP SYMBOL #1: {0:x}", addr);
			return create_address (addr);
		}

		public override TargetAddress LookupLocalSymbol (string name)
		{
			Console.WriteLine ("LOOKUP LOCAL SYMBOL: {0}", name);
			var addr = server.Connection.BfdLookupSymbol (iid, name);
			Console.WriteLine ("LOOKUP LOCAL SYMBOL #1: {0:x}", addr);
			return create_address (addr);
		}

		public override bool HasSection (string name)
		{
			Console.WriteLine ("HAS SECTION: {0}", name);
			return server.Connection.BfdHasSection (iid, name);
		}

		public override TargetAddress GetSectionAddress (string name)
		{
			Console.WriteLine ("GET SECTION ADDRESS: {0}", name);
			var addr = server.Connection.BfdGetSectionAddress (iid, name);
			return create_address (addr);
		}

		public override byte[] GetSectionContents (string name)
		{
			Console.WriteLine ("GET SECTION READER: {0}", name);
			return server.Connection.BfdGetSectionContents (iid, name);
		}

		public override TargetAddress EntryPoint {
			get {
				var addr = server.Connection.BfdGetStartAddress (iid);
				Console.WriteLine ("ENTRY POINT: {0:x}", addr);
				return create_address (addr);
			}
		}

		internal override TargetAddress ReadDynamicInfo (Inferior inferior)
		{
			var addr = server.ReadDynamicInfo (inferior, iid);
			return create_address (addr);
		}

		public override void ReadDebuggingInfo ()
		{
			read_dwarf ();
			read_stabs ();
		}

		void read_dwarf ()
		{
			if (!dwarf_supported || (dwarf != null))
				return;

			try {
				dwarf = new DwarfReader (os, this, module);
				dwarf.ReadTypes ();
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				dwarf_supported = false;
				return;
			}
		}

		void read_stabs ()
		{
			if (!stabs_supported || (stabs != null))
				return;

			try {
				stabs = new StabsReader (os, this, module);
				// stabs.ReadTypes ();
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read STABS debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				stabs_supported = false;
				return;
			}
		}

		protected bool SymbolsLoaded
		{
			get { return (dwarf != null); }
		}

		protected DwarfReader DwarfReader {
			get {
				if (dwarf == null)
					throw new InvalidOperationException ();

				return dwarf;
			}
		}

		protected Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (simple_symtab != null)
				return simple_symtab.SimpleLookup (address, exact_match);

			return null;
		}

		protected ArrayList GetSimpleSymbols ()
		{
			return new ArrayList ();
		}

		protected class RemoteSymbolFile : SymbolFile
		{
			public readonly RemoteExecutableReader ExecutableReader;

			public RemoteSymbolFile (RemoteExecutableReader reader)
			{
				this.ExecutableReader = reader;
			}

			public override bool IsNative {
				get { return true; }
			}

			public override string FullName {
				get { return ExecutableReader.FileName; }
			}

			public override string CodeBase {
				get { return ExecutableReader.FileName; }
			}

			public override Language Language {
				get { return ExecutableReader.OperatingSystem.Process.NativeLanguage; }
			}

			public override Module Module {
				get { return ExecutableReader.Module; }
			}

			public override bool HasDebuggingInfo {
				get { return ExecutableReader.HasDebuggingInfo; }
			}

			public override bool SymbolsLoaded {
				get { return ExecutableReader.SymbolsLoaded; }
			}

			public override SourceFile[] Sources {
				get { return ExecutableReader.DwarfReader.Sources; }
			}

			public override MethodSource[] GetMethods (SourceFile file)
			{
				return ExecutableReader.DwarfReader.GetMethods (file);
			}

			public override MethodSource FindMethod (string name)
			{
				return ExecutableReader.DwarfReader.FindMethod (name);
			}

			public override ISymbolTable SymbolTable {
				get { return ExecutableReader.DwarfReader.SymbolTable; }
			}

			public override Symbol SimpleLookup (TargetAddress address, bool exact_match)
			{
				return ExecutableReader.SimpleLookup (address, exact_match);
			}

			internal override void OnModuleChanged ()
			{ }

			internal override StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory)
			{
				return null;
			}
		}

		//
		// The BFD symbol table.
		//

		private class RemoteSymbolTable
		{
			RemoteExecutableReader reader;
			Symbol[] list;

			public RemoteSymbolTable (RemoteExecutableReader reader)
			{
				this.reader = reader;
			}

			public Symbol SimpleLookup (TargetAddress address, bool exact_match)
			{
				if (reader.IsContinuous &&
				    ((address < reader.StartAddress) || (address >= reader.EndAddress)))
					return null;

				if (list == null) {
					ArrayList the_list = reader.GetSimpleSymbols ();
					the_list.Sort ();

					list = new Symbol [the_list.Count];
					the_list.CopyTo (list);
				}

				for (int i = list.Length - 1; i >= 0; i--) {
					Symbol entry = list [i];

					if (address < entry.Address)
						continue;

					long offset = address - entry.Address;
					if (offset == 0) {
						while (i > 0) {
							Symbol n_entry = list [--i];

							if (n_entry.Address == entry.Address) 
								entry = n_entry;
							else
								break;
						}

						return new Symbol (entry.Name, address, 0);
					} else if (exact_match) {
						return null;
					} else {
						return new Symbol (entry.Name, address - offset, (int) offset);
					}
				}

				return null;
			}
		}
	}
}
