using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

using Mono.Debugger;
using Mono.Debugger.Server;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class LinuxOperatingSystem : OperatingSystemBackend
	{
		public LinuxOperatingSystem (Process process)
			: base (process)
		{ }

		protected override void CheckLoadedLibrary (Inferior inferior, ExecutableReader reader)
		{
			check_nptl_setxid (inferior, reader);

			if (!Process.MonoRuntimeFound)
				check_for_mono_runtime (inferior, reader);
		}

		TargetAddress pending_mono_init = TargetAddress.Null;

		void check_for_mono_runtime (Inferior inferior, ExecutableReader reader)
		{
			TargetAddress info = reader.GetSectionAddress (".mdb_debug_info");
			if (info.IsNull)
				return;

			TargetAddress data = inferior.ReadAddress (info);
			if (data.IsNull) {
				//
				// See CheckForPendingMonoInit() below - this should only happen when
				// the Mono runtime is embedded - for instance Moonlight inside Firefox.
				//
				// Note that we have to do a symbol lookup for it because we do not know
				// whether the mono runtime is recent enough to have this variable.
				//
				data = reader.LookupSymbol ("MONO_DEBUGGER__using_debugger");
				if (data.IsNull) {
					Report.Error ("Failed to initialize the Mono runtime!");
					return;
				}

				inferior.WriteInteger (data, 1);
				pending_mono_init = info;
				return;
			}

			Process.InitializeMono (inferior, data);
		}

		//
		// There seems to be a bug in some versions of glibc which causes _dl_debug_state() being
		// called with RT_CONSISTENT before relocations are done.
		//
		// If that happens, the debugger cannot read the `MONO_DEBUGGER__debugger_info' structure
		// at the time the `libmono.so' library is loaded.
		//
		// As a workaround, the `mdb_debug_info' now also contains a global variable called
		// `MONO_DEBUGGER__using_debugger' which may we set to 1 by the debugger to tell us that
		// we're running inside the debugger.
		//

		internal override bool CheckForPendingMonoInit (Inferior inferior)
		{
			lock (this) {
				if (pending_mono_init.IsNull)
					return false;

				TargetAddress data = inferior.ReadAddress (pending_mono_init);
				if (data.IsNull)
					return false;

				pending_mono_init = TargetAddress.Null;
				Process.InitializeMono (inferior, data);
				return true;
			}
		}

		public override bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start)
		{
			lock (this) {
#if FIXME
				foreach (ExecutableReader reader in reader_hash.Values) {
					Bfd bfd = reader as Bfd;
					if ((bfd != null) &&
					    bfd.GetTrampoline (memory, address, out trampoline, out is_start))
						return true;
				}
#endif

				is_start = false;
				trampoline = TargetAddress.Null;
				return false;
			}
		}

		public TargetAddress GetSectionAddress (string name)
		{
			lock (this) {
				foreach (ExecutableReader reader in reader_hash.Values) {
					TargetAddress address = reader.GetSectionAddress (name);
					if (!address.IsNull)
						return address;
				}

				return TargetAddress.Null;
			}
		}

#region Dynamic Linking

		protected override void DoUpdateSharedLibraries (Inferior inferior, ExecutableReader main_reader)
		{ }

#endregion

#region __nptl_setxid hack

		AddressBreakpoint setxid_breakpoint;

		void check_nptl_setxid (Inferior inferior, ExecutableReader reader)
		{
			if (setxid_breakpoint != null)
				return;

			TargetAddress vtable = reader.LookupSymbol ("__libc_pthread_functions");
			if (vtable.IsNull)
				return;

			/*
			 * Big big hack to allow debugging gnome-vfs:
			 * We intercept any calls to __nptl_setxid() and make it
			 * return 0.  This is safe to do since we do not allow
			 * debugging setuid programs or running as root, so setxid()
			 * will always be a no-op anyways.
			 */

			TargetAddress nptl_setxid = inferior.ReadAddress (vtable + 51 * inferior.TargetAddressSize);

			if (!nptl_setxid.IsNull) {
				setxid_breakpoint = new SetXidBreakpoint (this, nptl_setxid);
				setxid_breakpoint.Insert (inferior);
			}
		}

		protected class SetXidBreakpoint : AddressBreakpoint
		{
			protected readonly LinuxOperatingSystem OS;

			public SetXidBreakpoint (LinuxOperatingSystem os, TargetAddress address)
				: base ("setxid", ThreadGroup.System, address)
			{
				this.OS = os;
			}

			public override bool CheckBreakpointHit (Thread target, TargetAddress address)
			{
				return true;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				inferior.Architecture.Hack_ReturnNull (inferior);
				remain_stopped = false;
				return true;
			}
		}

#endregion
	}
}
