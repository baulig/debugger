using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_ARM : Opcodes
	{
		Process process;
		TargetMemoryInfo target_info;

		internal Opcodes_ARM (Process process)
		{
			this.process = process;

			target_info = process.ThreadManager.GetTargetMemoryInfo (AddressDomain.Global);
		}

		internal TargetMemoryInfo TargetMemoryInfo {
			get { return target_info; }
		}

		internal Disassembler Disassembler {
			get { return process.Architecture.Disassembler; }
		}

		internal Process Process {
			get { return process; }
		}

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
