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

			context.Registers [(int) ARM_Register.SP].State = UnwindContext.RegisterState.Preserved;
			context.Registers [(int) ARM_Register.FP].State = UnwindContext.RegisterState.Preserved;
			context.Registers [(int) ARM_Register.IP].State = UnwindContext.RegisterState.Preserved;

			while (!reader.IsEof) {
				var opcode = reader.ReadUInt32 ();
				Report.Debug (DebugFlags.StackUnwind, "  scanning: {0} {1:x}", address, opcode);
				var insn = new Instruction_ARM (this, memory, address, context.Frame.Registers, opcode);
				address += 4;

				if (!insn.ScanPrologue (context))
					return false;
			}

			return true;
		}

	}
}
