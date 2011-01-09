using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Architectures
{
	internal class Opcodes_I386 : X86_Opcodes
	{
		internal Opcodes_I386 (Architecture arch, TargetMemoryInfo target_info)
			: base (arch, target_info)
		{ }

		public override bool Is64BitMode {
			get { return false; }
		}
	}
}
