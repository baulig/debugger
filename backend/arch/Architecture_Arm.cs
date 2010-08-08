using System;
using System.Collections;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	// <summary>
	//   Architecture-dependent stuff for the x86_64.
	// </summary>
	internal class Architecture_Arm : Architecture
	{
		internal Architecture_Arm (Process process, TargetInfo info)
			: base (process, info)
		{ }

		internal override bool IsRetInstruction (TargetMemoryAccess memory,
							 TargetAddress address)
		{
			return false;
		}

		internal override bool IsSyscallInstruction (TargetMemoryAccess memory,
							     TargetAddress address)
		{
			return false;
		}

		internal enum ARM_Register
		{
			R0 = 0,
			R1,
			R2,
			R3,
			R4,
			R5,
			R6,
			R7,
			R8,
			R9,
			R10,

			FP,
			IP,
			SP,
			LR,
			PC,
			CPSR,
			ORIG_R0,

			COUNT
		}

		public override int[] AllRegisterIndices {
			get {
				return new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
			}
		}

		public override int[] RegisterIndices {
			get {
				return new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
			}
		}

		// FIXME: Map mono/arch/amd64/amd64-codegen.h registers to
		//        debugger/arch/IArchitecture_X86_64.cs registers.
		internal override int[] RegisterMap {
			get {
				return new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
			}
		}

		internal override int[] DwarfFrameRegisterMap {
			get { throw new NotImplementedException (); }
		}

		public override string[] RegisterNames {
			get {
				return new string[] {
					"r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10",
					"fp", "ip", "sp", "lr", "pc", "cpsr", "orig_r0"
				};
			}
		}

		public override int[] RegisterSizes {
			get {
				return new int[] { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };
			}
		}

		internal override int CountRegisters {
			get { return (int) ARM_Register.COUNT; }
		}

		string format (Register register)
		{
			if (!register.Valid)
				return "XXXXXXXX";

			int bits = 8;
			string saddr = register.Value.ToString ("x");
			for (int i = saddr.Length; i < bits; i++)
				saddr = "0" + saddr;
			return saddr;
		}

		public override string PrintRegister (Register register)
		{
			return format (register);
		}

		public override string PrintRegisters (StackFrame frame)
		{
			Registers registers = frame.Registers;
			return String.Format (
				"R0={0}  R1={1}  R2={2}  R3={3}  R4={4}  R5={5}\n" +
				"R6={6}  R7={7}  R8={8}  R9={9}  R10={10}\n" +
				"FP={11}  IP={12}  SP={13}  LR={14}  PC={15}\n" +
				"CPSR={16}  ORIG_R0={17}\n",
				format (registers [(int) ARM_Register.R0]),
				format (registers [(int) ARM_Register.R1]),
				format (registers [(int) ARM_Register.R2]),
				format (registers [(int) ARM_Register.R3]),
				format (registers [(int) ARM_Register.R4]),
				format (registers [(int) ARM_Register.R5]),
				format (registers [(int) ARM_Register.R6]),
				format (registers [(int) ARM_Register.R7]),
				format (registers [(int) ARM_Register.R8]),
				format (registers [(int) ARM_Register.R9]),
				format (registers [(int) ARM_Register.R10]),
				format (registers [(int) ARM_Register.FP]),
				format (registers [(int) ARM_Register.IP]),
				format (registers [(int) ARM_Register.SP]),
				format (registers [(int) ARM_Register.LR]),
				format (registers [(int) ARM_Register.PC]),
				format (registers [(int) ARM_Register.CPSR]),
				format (registers [(int) ARM_Register.ORIG_R0]));
		}

		internal override int MaxPrologueSize {
			get { return -1; }
		}

		internal override Registers CopyRegisters (Registers old_regs)
		{
			Registers regs = new Registers (old_regs);

			return regs;
		}

		internal override StackFrame UnwindStack (StackFrame frame, TargetMemoryAccess memory,
							  byte[] code, int offset)
		{
			return null;
		}

		internal override StackFrame TrySpecialUnwind (StackFrame frame,
							       TargetMemoryAccess memory)
		{
			return null;
		}

		internal override void Hack_ReturnNull (Inferior inferior)
		{
		}

		internal override StackFrame CreateFrame (Thread thread, FrameType type, TargetMemoryAccess memory, Registers regs)
		{
			TargetAddress address = new TargetAddress (
				memory.AddressDomain, regs [0].GetValue ());
			TargetAddress stack_pointer = new TargetAddress (
				memory.AddressDomain, regs [1].GetValue ());
			TargetAddress frame_pointer = new TargetAddress (
				memory.AddressDomain, regs [2].GetValue ());

			return CreateFrame (thread, type, memory, address, stack_pointer, frame_pointer, regs);
		}

		internal override StackFrame GetLMF (ThreadServant thread, TargetMemoryAccess memory, ref TargetAddress lmf_address)
		{
			return null;
		}
	}
}
