using System;
using System.Text;
using System.Collections;

using Mono.Debugger.Languages;
using Mono.Debugger.Backend;
using Mono.Debugger.Architectures;

namespace Mono.Debugger
{
	public enum WrapperType
	{
		None = 0,
		DelegateInvoke,
		DelegateBeginInvoke,
		DelegateEndInvoke,
		RuntimeInvoke,
		NativeToManaged,
		ManagedToNative,
		ManagedToManaged,
		RemotingInvoke,
		RemotingInvokeWithCheck,
		XDomainInvoke,
		XDomainDispatch,
		Ldfld,
		Stfld,
		LdfldRemote,
		StfldRemote,
		Synchronized,
		DynamicMethod,
		IsInst,
		CanCast,
		ProxyIsInst,
		StelemRef,
		UnBox,
		LdFldA,
		WriteBarrier,
		Unknown,
		ComInteropInvoke,
		ComInterop,
		Alloc
	}

	public struct LineEntry : IComparable {
		public readonly TargetAddress Address;
		public readonly int File;
		public readonly int Line;
		public readonly bool IsHidden;
		public readonly SourceRange? SourceRange;

		public LineEntry (TargetAddress address, int file, int line)
			: this (address, file, line, false)
		{ }

		public LineEntry (TargetAddress address, int file, int line, bool is_hidden)
			: this (address, file, line, is_hidden, null)
		{ }

		public LineEntry (TargetAddress address, int file, int line, bool is_hidden,
				  SourceRange? source_range)
		{
			this.Address = address;;
			this.File = file;
			this.Line = line;
			this.IsHidden = is_hidden;
			this.SourceRange = source_range;
		}

		public int CompareTo (object obj)
		{
			LineEntry entry = (LineEntry) obj;

			if (entry.Address < Address)
				return 1;
			else if (entry.Address > Address)
				return -1;
			else
				return 0;
		}

		public override string ToString ()
		{
			return String.Format ("LineEntry ({0}:{1}:{2})", File, Line, Address);
		}
	}

	public abstract class Method : DebuggerMarshalByRefObject, ISymbolLookup, IComparable
	{
		TargetAddress start, end;
		TargetAddress method_start, method_end;
		WrapperType wrapper_type = WrapperType.None;
		LineNumberTable line_numbers;
		Module module;
		bool is_loaded, has_bounds;
		string image_file;
		string name;

		protected Method (string name, string image_file, Module module,
				  TargetAddress start, TargetAddress end)
			: this (name, image_file, module)
		{
			this.start = start;
			this.end = end;
			this.method_start = start;
			this.method_end = end;
			this.is_loaded = true;
		}

		protected Method (string name, string image_file, Module module)
		{
			this.name = name;
			this.image_file = image_file;
			this.module = module;
		}

		protected Method (Method method)
			: this (method.Name, method.ImageFile, method.Module,
				method.StartAddress, method.EndAddress)
		{ }

		protected void SetAddresses (TargetAddress start, TargetAddress end)
		{
			this.start = start;
			this.end = end;
			this.is_loaded = true;
			this.has_bounds = false;
		}

		protected void SetMethodBounds (TargetAddress method_start, TargetAddress method_end)
		{
			this.method_start = method_start;
			this.method_end = method_end;
			this.has_bounds = true;
		}

		protected void SetLineNumbers (LineNumberTable line_numbers)
		{
			this.line_numbers = line_numbers;
		}

		protected void SetWrapperType (WrapperType wrapper_type)
		{
			this.wrapper_type = wrapper_type;
		}

		internal StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory)
		{
			if (!IsLoaded)
				return null;

			Report.Debug (DebugFlags.StackUnwind, "Unwind method: {0}:{1} - {2}",
				      frame.TargetAddress, name, HasMethodBounds);

			try {
				StackFrame new_frame = Module.UnwindStack (frame, memory);
				if (new_frame != null) {
					Report.Debug (DebugFlags.StackUnwind, "Unwind method #1: {0}:{1} -> {2}",
						      frame.TargetAddress, name, new_frame);
					return new_frame;
				}
			} catch (Exception ex) {
				Report.Debug (DebugFlags.StackUnwind, "Unwind method ex: {0}:{1} -> {2}",
					      frame.TargetAddress, name, ex);
			}

			int prologue_size;
			if (HasMethodBounds)
				prologue_size = (int) (MethodStartAddress - StartAddress);
			else
				prologue_size = (int) (EndAddress - StartAddress);
			int offset = (int) (frame.TargetAddress - StartAddress);

			byte[] prologue = memory.ReadBuffer (StartAddress, prologue_size);
			Report.Debug (DebugFlags.StackUnwind, "Unwind method: {0}:{1} - {2} {3} - {4}",
				      frame.TargetAddress, name, StartAddress, StartAddress + prologue_size, offset);

			var context = new UnwindContext (frame, StartAddress, prologue);
			return frame.Thread.Architecture.UnwindStack (context, memory);
		}

		//
		// Method
		//

		public string Name {
			get {
				return name;
			}
			protected set {
				name = value;
			}
		}

		public string ImageFile {
			get {
				return image_file;
			}
		}

		public Module Module {
			get {
				return module;
			}
		}

		public abstract int Domain {
			get;
		}

		public abstract bool IsWrapper {
			get;
		}

		internal bool IsInvokeWrapper {
			get {
				return (WrapperType == WrapperType.DelegateInvoke ||
				        WrapperType == WrapperType.RemotingInvoke ||
				        WrapperType == WrapperType.RemotingInvokeWithCheck);
			}
		}

		public abstract bool IsCompilerGenerated {
			get;
		}

		public abstract bool HasSource {
			get;
		}

		public abstract MethodSource MethodSource {
			get;
		}

		public abstract object MethodHandle {
			get;
		}

		public bool IsLoaded {
			get {
				return is_loaded;
			}
		}

		public bool HasMethodBounds {
			get {
				return has_bounds;
			}
		}

		public TargetAddress StartAddress {
			get {
				if (!is_loaded)
					throw new InvalidOperationException ();

				return start;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!is_loaded)
					throw new InvalidOperationException ();

				return end;
			}
		}

		public TargetAddress MethodStartAddress {
			get {
				if (!has_bounds)
					throw new InvalidOperationException ();

				return method_start;
			}
		}

		public TargetAddress MethodEndAddress {
			get {
				if (!has_bounds)
					throw new InvalidOperationException ();

				return method_end;
			}
		}

		public WrapperType WrapperType {
			get {
				return wrapper_type;
			}
		}

		public bool HasLineNumbers {
			get {
				return line_numbers != null;
			}
		}

		public LineNumberTable LineNumberTable {
			get {
				if (!HasLineNumbers)
					throw new InvalidOperationException ();

				return line_numbers;
			}
		}

		public static bool IsInSameMethod (Method method, TargetAddress address)
                {
			if (address.IsNull || !method.IsLoaded)
				return false;

                        if ((address < method.StartAddress) || (address >= method.EndAddress))
                                return false;

                        return true;
                }

		public abstract TargetClassType GetDeclaringType (Thread target);

		public abstract bool HasThis {
			get;
		}

		public abstract TargetVariable GetThis (Thread target);

		public abstract TargetVariable[] GetParameters (Thread target);

		public abstract TargetVariable[] GetLocalVariables (Thread target);

		public abstract string[] GetNamespaces ();

		internal abstract MethodSource GetTrampoline (TargetMemoryAccess memory,
							      TargetAddress address);

		internal virtual bool IsIterator {
			get { return false; }
		}

		internal virtual Block LookupBlock (TargetMemoryAccess memory,
						    TargetAddress address)
		{
			return null;
		}

		//
		// ISourceLookup
		//

		Method ISymbolLookup.Lookup (TargetAddress address)
		{
			if (!is_loaded)
				return null;

			if ((address < start) || (address >= end))
				return null;

			return this;
		}

		public int CompareTo (object obj)
		{
			Method method = (Method) obj;

			TargetAddress address;
			try {
				address = method.StartAddress;
			} catch {
				return is_loaded ? -1 : 0;
			}

			if (!is_loaded)
				return 1;

			if (address < start)
				return 1;
			else if (address > start)
				return -1;
			else
				return 0;
		}

		public override string ToString ()
		{
			return String.Format ("{0}({1},{2:x},{3:x})", GetType (), name, start, end);
		}
	}
}
