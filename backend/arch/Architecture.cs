using System;
using Mono.Debugger.Server;
using Mono.Debugger.Backend;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	// <summary>
	//   Architecture-dependent interface.
	// </summary>
	internal abstract class Architecture : DebuggerMarshalByRefObject, IDisposable
	{
		protected readonly Process process;
		protected readonly TargetInfo TargetInfo;

		Disassembler disassembler;
		X86_Opcodes opcodes;

		protected Architecture (Process process, TargetInfo info)
		{
			this.process = process;
			this.TargetInfo = info;

			var server = process.ThreadManager.DebuggerServer as RemoteDebuggerServer;
			if (server != null)
				disassembler = server.GetDisassembler ();
			else if (!Inferior.IsRunningOnWindows)
				disassembler = new BfdDisassembler (process, info.TargetAddressSize == 8);

			switch (process.ThreadManager.DebuggerServer.ArchType) {
			case DebuggerServer.ArchTypeEnum.I386:
				opcodes = new Opcodes_X86_64 (process);
				break;
			case DebuggerServer.ArchTypeEnum.X86_64:
				opcodes = new Opcodes_I386 (process);
				break;
			case DebuggerServer.ArchTypeEnum.ARM:
				// opcodes = new Opcodes_ARM (process);
				break;
			default:
				throw new InternalError ();
			}
		}

		internal Disassembler Disassembler {
			get { return disassembler; }
		}

		public Opcodes Opcodes {
			get { return opcodes; }
		}

		public int TargetAddressSize {
			get { return TargetInfo.TargetAddressSize; }
		}

		// <summary>
		//   The names of all registers.
		// </summary>
		public abstract string[] RegisterNames {
			get;
		}

		// <summary>
		//   Indices of the "important" registers, sorted in a way that's suitable
		//   to display them to the user.
		// </summary>
		public abstract int[] RegisterIndices {
			get;
		}

		// <summary>
		//   Indices of all registers.
		// </summary>
		public abstract int[] AllRegisterIndices {
			get;
		}

		// <summary>
		// Size (in bytes) of each register.
		// </summary>
		public abstract int[] RegisterSizes {
			get;
		}

		// <summary>
		// A map between the register the register numbers in
		// the jit code generator and the register indices
		// used in the above arrays.
		// </summary>
		internal abstract int[] RegisterMap {
			get;
		}

		internal abstract int[] DwarfFrameRegisterMap {
			get;
		}

		internal abstract int CountRegisters {
			get;
		}

		public abstract string PrintRegister (Register register);

		public abstract string PrintRegisters (StackFrame frame);

		// <summary>
		//   Returns whether the instruction at target address @address is a `ret'
		//   instruction.
		// </summary>
		internal abstract bool IsRetInstruction (TargetMemoryAccess memory,
							 TargetAddress address);

		// <summary>
		//   Returns whether the instruction at target address @address is a `syscall'
		//   instruction.
		// </summary>
		internal abstract bool IsSyscallInstruction (TargetMemoryAccess memory,
							     TargetAddress address);

		internal Instruction ReadInstruction (TargetMemoryAccess memory, TargetAddress address)
		{
			return opcodes.ReadInstruction (memory, address);
		}

		internal abstract int MaxPrologueSize {
			get;
		}

		internal abstract Registers CopyRegisters (Registers regs);

		internal abstract StackFrame GetLMF (ThreadServant thread, TargetMemoryAccess target,
						     ref TargetAddress lmf_address);

		internal abstract StackFrame UnwindStack (StackFrame last_frame,
							  TargetMemoryAccess memory,
							  byte[] code, int offset);

		internal abstract StackFrame TrySpecialUnwind (StackFrame last_frame,
							       TargetMemoryAccess memory);

		internal abstract StackFrame CreateFrame (Thread thread, FrameType type,
							  TargetMemoryAccess target, Registers regs);

		internal StackFrame CreateFrame (Thread thread, FrameType type, TargetMemoryAccess target,
						 TargetAddress address, TargetAddress stack,
						 TargetAddress frame_pointer, Registers regs)
		{
			if ((address.IsNull) || (address.Address == 0))
				return null;

			Method method = process.SymbolTableManager.Lookup (address);
			if (method != null)
				return new StackFrame (
					thread, type, address, stack, frame_pointer, regs, method);

			Symbol name = process.SymbolTableManager.SimpleLookup (address, false);
			return new StackFrame (
				thread, type, address, stack, frame_pointer, regs,
				process.NativeLanguage, name);
		}

		//
		// This is a horrible hack - don't use !
		//
		//
		internal abstract void Hack_ReturnNull (Inferior inferior);

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Architecture");
		}

		protected virtual void DoDispose ()
		{
			if (disassembler != null) {
				disassembler.Dispose ();
				disassembler = null;
			}
		}

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

		~Architecture ()
		{
			Dispose (false);
		}

	}
}
