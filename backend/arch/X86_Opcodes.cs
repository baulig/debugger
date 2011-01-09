using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal abstract class X86_Opcodes : Opcodes
	{
		protected X86_Opcodes (Architecture arch, TargetMemoryInfo target_info)
			: base (arch, target_info)
		{ }

		public abstract bool Is64BitMode {
			get;
		}

		internal override Instruction ReadInstruction (TargetMemoryAccess memory,
							       TargetAddress address)
		{
			return X86_Instruction.DecodeInstruction (this, memory, address);
		}

		internal override byte[] GenerateNopInstruction ()
		{
			return new byte[] { 0x90 };
		}
	}
}
