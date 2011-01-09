using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_X86_64 : X86_Opcodes
	{
		internal Opcodes_X86_64 (Architecture arch, TargetMemoryInfo target_info)
			: base (arch, target_info)
		{ }

		public override bool Is64BitMode {
			get { return true; }
		}
	}
}
