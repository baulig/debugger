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
		IExecutableReader reader;

		DebuggingFileReader debug_info;
		ExeReaderSymbolFile symfile;

		ArrayList simple_symbols;
		SymbolTable simple_symtab;

		bool dwarf_supported;
		bool stabs_supported;
		bool has_frame_reader;

		DwarfFrameReader frame_reader, eh_frame_reader;

		TargetAddress start_address = TargetAddress.Null;
		TargetAddress end_address = TargetAddress.Null;
		TargetAddress base_address = TargetAddress.Null;
		TargetAddress entry_point = TargetAddress.Null;

		public ExecutableReader (Process process, TargetInfo target_info, IExecutableReader reader)
		{
			this.Process = process;
			this.TargetInfo = target_info;
			this.reader = reader;

			if (DwarfReader.IsSupported (this))
				dwarf_supported = true;
			else if (StabsReader.IsSupported (this))
				stabs_supported = true;

			symfile = new ExeReaderSymbolFile (this);

			start_address = create_address (reader.StartAddress);
			end_address = create_address (reader.EndAddress);
			base_address = create_address (reader.BaseAddress);
			entry_point = create_address (reader.EntryPoint);

			Module = process.Session.GetModule (FileName);
			if (Module == null) {
				Module = process.Session.CreateModule (FileName, symfile);
			} else {
				Module.LoadModule (symfile);
			}

			process.SymbolTableManager.AddSymbolFile (symfile);
		}

		public Process Process {
			get; private set;
		}

		public OperatingSystemBackend OperatingSystem
		{
			get { return Process.OperatingSystem; }
		}

		internal Architecture Architecture {
			get { return Process.Architecture; }
		}

		public TargetInfo TargetInfo {
			get; private set;
		}

		public Module Module {
			get; private set;
		}

		public string FileName {
			get { return reader.FileName; }
		}

		public string TargetName {
			get { return reader.TargetName; }
		}

		public bool IsLoaded {
			get { return true; }
		}

		protected bool HasDebuggingInfo {
			get { return dwarf_supported || stabs_supported; }
		}

		TargetAddress create_address (long addr)
		{
			return addr != 0 ? new TargetAddress (AddressDomain.Global, addr) : TargetAddress.Null;
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

		public TargetAddress EntryPoint {
			get { return entry_point; }
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

		void create_frame_reader ()
		{
			if (has_frame_reader)
				return;

			has_frame_reader = true;

			if (reader.HasSection (".debug_frame")) {
				var contents = reader.GetSectionContents (".debug_frame");
				var addr = reader.GetSectionAddress (".debug_frame");
				var blob = new TargetBlob (contents, TargetInfo);
				frame_reader = new DwarfFrameReader (
					OperatingSystem, this, blob, addr, false);
			}

			if (reader.HasSection (".eh_frame")) {
				var contents = reader.GetSectionContents (".eh_frame");
				var addr = reader.GetSectionAddress (".eh_frame");
				var blob = new TargetBlob (contents, TargetInfo);
				frame_reader = new DwarfFrameReader (
					OperatingSystem, this, blob, addr, true);
			}
		}

		public void ReadDebuggingInfo ()
		{
			read_dwarf ();
			read_stabs ();
			create_frame_reader ();
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

		protected StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory)
		{
			if ((frame.TargetAddress < StartAddress) || (frame.TargetAddress > EndAddress))
				return null;

			StackFrame new_frame;
			try {
				new_frame = Architecture.TrySpecialUnwind (frame, memory);
				if (new_frame != null)
					return new_frame;
			} catch {
			}

			try {
				if (frame_reader != null) {
					new_frame = frame_reader.UnwindStack (frame, memory, Architecture);
					if (new_frame != null)
						return new_frame;
				}

				if (eh_frame_reader != null) {
					new_frame = eh_frame_reader.UnwindStack (frame, memory, Architecture);
					if (new_frame != null)
						return new_frame;
				}
			} catch {
				return null;
			}

			return null;
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
				return ExecutableReader.UnwindStack (frame, memory);
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
