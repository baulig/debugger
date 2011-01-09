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

			return true;
		}

	}
}
