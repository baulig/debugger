using System;
using System.IO;
using System.Collections;
using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class WindowsOperatingSystem : OperatingSystemBackend
	{
		public WindowsOperatingSystem(Process process)
			: base (process)
		{ }

		internal override bool CheckForPendingMonoInit (Inferior inferior)
		{
			throw new NotImplementedException();
		}

		public override bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start)
		{
			throw new NotImplementedException();
		}

		public TargetAddress GetSectionAddress (string name)
		{
			throw new NotImplementedException();
		}

		protected override void DoUpdateSharedLibraries (Inferior inferior, ExecutableReader main_reader)
		{ }
	}
}
