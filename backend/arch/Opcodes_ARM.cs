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
	}
}
