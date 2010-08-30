using System;
using System.Collections;
using System.Collections.Generic;

using Mono.Debugger;
using Mono.Debugger.Server;
using Mono.Debugger.Languages;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class ExecutableReader : DebuggerMarshalByRefObject, IDisposable
	{
		DebuggerServer server;

		IExecutableReader reader;

		DebuggingFileReader debug_info;
		ExeReaderSymbolFile symfile;

		ArrayList simple_symbols;
		SymbolTable simple_symtab;

		bool dwarf_supported;
		bool stabs_supported;

		TargetAddress start_address = TargetAddress.Null;
		TargetAddress end_address = TargetAddress.Null;
		TargetAddress base_address = TargetAddress.Null;

		public ExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory_info,
					 DebuggerServer server, IExecutableReader reader, string file)
		{
			this.OperatingSystem = os;
			this.TargetMemoryInfo = memory_info;
			this.FileName = file;
			this.server = server;
			this.reader = reader;

			TargetName = reader.GetTargetName ();

			if (DwarfReader.IsSupported (this))
				dwarf_supported = true;
			else if (StabsReader.IsSupported (this))
				stabs_supported = true;

			symfile = new ExeReaderSymbolFile (this);

			Module = os.Process.Session.GetModule (file);
			if (Module == null) {
				Module = os.Process.Session.CreateModule (file, symfile);
			} else {
				Module.LoadModule (symfile);
			}

			os.Process.SymbolTableManager.AddSymbolFile (symfile);
		}

		public OperatingSystemBackend OperatingSystem {
			get; private set;
		}

		public TargetMemoryInfo TargetMemoryInfo {
			get; private set;
		}

		public Module Module {
			get; private set;
		}

		public string FileName {
			get; private set;
		}

		public string TargetName {
			get; private set;
		}

		public bool IsLoaded {
			get { return true; }
		}

		protected bool HasDebuggingInfo {
			get { return dwarf_supported || stabs_supported; }
		}

		TargetAddress create_address (long addr)
		{
			return addr != 0 ? new TargetAddress (TargetMemoryInfo.AddressDomain, addr) : TargetAddress.Null;
		}

		public bool IsContinuous {
			get { return !end_address.IsNull; }
		}

		public TargetAddress StartAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return start_address;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return end_address;
			}
		}

		public TargetAddress BaseAddress {
			get { return base_address; }
		}

		public TargetAddress LookupSymbol (string name)
		{
			var addr = reader.LookupSymbol (name);
			return create_address (addr);
		}

		public TargetAddress LookupLocalSymbol (string name)
		{
			var addr = reader.LookupSymbol (name);
			return create_address (addr);
		}

		public bool HasSection (string name)
		{
			return reader.HasSection (name);
		}

		public TargetAddress GetSectionAddress (string name)
		{
			var addr = reader.GetSectionAddress (name);
			return create_address (addr);
		}

		public byte[] GetSectionContents (string name)
		{
			return reader.GetSectionContents (name);
		}

		public TargetAddress EntryPoint {
			get {
				var addr = reader.StartAddress;
				return create_address (addr);
			}
		}

		public void ReadDebuggingInfo ()
		{
			read_dwarf ();
			read_stabs ();
		}

		void read_dwarf ()
		{
			if (!dwarf_supported || (debug_info != null))
				return;

			try {
				var dwarf = new DwarfReader (OperatingSystem, this, Module);
				dwarf.ReadTypes ();
				debug_info = dwarf;
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read DWARF debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				dwarf_supported = false;
				return;
			}
		}

		void read_stabs ()
		{
			if (!stabs_supported || (debug_info != null))
				return;

			try {
				debug_info = new StabsReader (OperatingSystem, this, Module);
			} catch (Exception ex) {
				Console.WriteLine ("Cannot read STABS debugging info from " +
						   "symbol file `{0}': {1}", FileName, ex);
				stabs_supported = false;
				return;
			}
		}

		protected bool SymbolsLoaded
		{
			get { return (debug_info != null); }
		}

		protected DebuggingFileReader DebugInfo {
			get {
				if (debug_info == null)
					throw new InvalidOperationException ();

				return debug_info;
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

		protected class ExeReaderSymbolFile : SymbolFile
		{
			public readonly ExecutableReader ExecutableReader;

			public ExeReaderSymbolFile (ExecutableReader reader)
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
				get { return ExecutableReader.DebugInfo.Sources; }
			}

			public override MethodSource[] GetMethods (SourceFile file)
			{
				return ExecutableReader.DebugInfo.GetMethods (file);
			}

			public override MethodSource FindMethod (string name)
			{
				return ExecutableReader.DebugInfo.FindMethod (name);
			}

			public override ISymbolTable SymbolTable {
				get { return ExecutableReader.DebugInfo.SymbolTable; }
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

		private class ExeReaderSymbolTable
		{
			ExecutableReader reader;
			Symbol[] list;

			public ExeReaderSymbolTable (ExecutableReader reader)
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


		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void DoDispose ()
		{ }

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing)
				DoDispose ();
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ExecutableReader ()
		{
			Dispose (false);
		}
	}
}
