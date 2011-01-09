using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_ARM : Opcodes
	{
		internal Opcodes_ARM (Architecture arch, TargetMemoryInfo target_info)
			: base (arch, target_info)
		{ }

		internal override Instruction ReadInstruction (TargetMemoryAccess memory,
							       TargetAddress address)
		{
			return new Instruction_ARM (this, memory, address);
		}

		internal override byte[] GenerateNopInstruction ()
		{
			throw new NotImplementedException ();
		}

		internal bool ScanPrologue (UnwindContext context, TargetMemoryAccess memory)
		{
			var address = context.StartAddress;
			var reader = new TargetBinaryReader (context.PrologueCode, memory.TargetMemoryInfo);

			Report.Debug (DebugFlags.StackUnwind, "Scanning prologue: {0}", reader.HexDump ());

			for (int i = 0; i <= 15; i++)
				context.Registers [i].State = UnwindContext.RegisterState.Preserved;

			context.Dump ();

			while (!reader.IsEof) {
				var opcode = reader.ReadUInt32 ();
				Report.Debug (DebugFlags.StackUnwind, "  scanning: {0} {1:x}", address, opcode);
				var insn = new Instruction_ARM (this, memory, address, context.Frame.Registers, opcode);

				bool ok = insn.ScanPrologue (context);

				Report.Debug (DebugFlags.StackUnwind, "  scanning: {0} {1:x} -> {2}", address, opcode,
					      ok ? "OK" : "ERROR");

				address += 4;

				context.Dump ();

				if (!ok)
					return false;
			}

			Report.Debug (DebugFlags.StackUnwind, "Done scanning prologue: IP = {0}, FP = {1}, SP = {2}",
				      context.PrintRegisterValue ((int) ARM_Register.IP),
				      context.PrintRegisterValue ((int) ARM_Register.FP),
				      context.PrintRegisterValue ((int) ARM_Register.SP));

			var fp_regval = context.Registers [(int) ARM_Register.FP];
			if ((fp_regval.State != UnwindContext.RegisterState.Register) || (fp_regval.BaseRegister != (int) ARM_Register.SP)) {
				Report.Debug (DebugFlags.StackUnwind, "  Invalid frame pointer!");
				return false;
			}

			var current_fp = context.OriginalRegisters [(int) ARM_Register.FP];
			var current_lr = context.OriginalRegisters [(int) ARM_Register.LR];

			var old_sp = current_fp - fp_regval.Offset;
			var old_lr = current_lr;
			var old_fp = current_fp;

			Report.Debug (DebugFlags.StackUnwind, "  FP = {0}, LR = {1}, SP on entry = {2}",
				      current_fp, current_lr, old_sp);

			var preserved_lr = context.PreservedRegisters [(int) ARM_Register.LR];
			if (preserved_lr.State == UnwindContext.RegisterState.Memory) {
				old_lr = memory.ReadAddress (old_sp + preserved_lr.Offset);
				Report.Debug (DebugFlags.StackUnwind, "  Preserved LR: {0}", old_lr);
			}

			var preserved_fp = context.PreservedRegisters [(int) ARM_Register.FP];
			if (preserved_fp.State == UnwindContext.RegisterState.Memory) {
				old_fp = memory.ReadAddress (old_sp + preserved_fp.Offset);
				Report.Debug (DebugFlags.StackUnwind, "  Preserved FP: {0}", old_fp);
			}

			return true;
		}

	}
}
