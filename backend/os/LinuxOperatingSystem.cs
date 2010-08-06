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
				foreach (ExecutableReader reader in reader_hash.Values) {
					Bfd bfd = reader as Bfd;
					if ((bfd != null) &&
					    bfd.GetTrampoline (memory, address, out trampoline, out is_start))
						return true;
				}

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

		bool has_dynlink_info;
		TargetAddress first_link_map = TargetAddress.Null;
		TargetAddress dynlink_breakpoint_addr = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;

		AddressBreakpoint dynlink_breakpoint;

		protected override void DoUpdateSharedLibraries (Inferior inferior, ExecutableReader main_reader)
		{
			if (has_dynlink_info) {
				if (!first_link_map.IsNull)
					do_update_shlib_info (inferior);
				return;
			}

			TargetAddress debug_base = main_reader.ReadDynamicInfo (inferior);
			if (debug_base.IsNull)
				return;

			int size = 2 * inferior.TargetLongIntegerSize + 3 * inferior.TargetAddressSize;

			TargetReader reader = new TargetReader (inferior.ReadMemory (debug_base, size));
			if (reader.ReadLongInteger () != 1)
				return;

			first_link_map = reader.ReadAddress ();
			dynlink_breakpoint_addr = reader.ReadAddress ();

			rdebug_state_addr = debug_base + reader.Offset;

			if (reader.ReadLongInteger () != 0)
				return;

			has_dynlink_info = true;

			Instruction insn = inferior.Architecture.ReadInstruction (inferior, dynlink_breakpoint_addr);
			if ((insn == null) || !insn.CanInterpretInstruction)
				throw new InternalError ("Unknown dynlink breakpoint: {0}", dynlink_breakpoint_addr);

			dynlink_breakpoint = new DynlinkBreakpoint (this, insn);
			dynlink_breakpoint.Insert (inferior);

			do_update_shlib_info (inferior);

			CheckLoadedLibrary (inferior, main_reader);
		}

		bool dynlink_handler (Inferior inferior)
		{
			if (inferior.ReadInteger (rdebug_state_addr) != 0)
				return false;

			do_update_shlib_info (inferior);
			return false;
		}

		void do_update_shlib_info (Inferior inferior)
		{
			bool first = true;
			TargetAddress map = first_link_map;
			while (!map.IsNull) {
				int the_size = 4 * inferior.TargetAddressSize;
				TargetReader map_reader = new TargetReader (inferior.ReadMemory (map, the_size));

				TargetAddress l_addr = map_reader.ReadAddress ();
				TargetAddress l_name = map_reader.ReadAddress ();
				map_reader.ReadAddress ();

				string name;
				try {
					name = inferior.ReadString (l_name);
					// glibc 2.3.x uses the empty string for the virtual
					// "linux-gate.so.1".
					if ((name != null) && (name == ""))
						name = null;
				} catch {
					name = null;
				}

				map = map_reader.ReadAddress ();

				if (first) {
					first = false;
					continue;
				}

				if (name == null)
					continue;

				if (reader_hash.ContainsKey (name))
					continue;

				bool step_into = Process.ProcessStart.LoadNativeSymbolTable;
				AddExecutableFile (inferior, name, l_addr, step_into, true);
			}
		}

		protected class DynlinkBreakpoint : AddressBreakpoint
		{
			protected readonly LinuxOperatingSystem OS;
			public readonly Instruction Instruction;

			public DynlinkBreakpoint (LinuxOperatingSystem os, Instruction instruction)
				: base ("dynlink", ThreadGroup.System, instruction.Address)
			{
				this.OS = os;
				this.Instruction = instruction;
			}

			public override bool CheckBreakpointHit (Thread target, TargetAddress address)
			{
				return true;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				OS.dynlink_handler (inferior);
				if (!Instruction.InterpretInstruction (inferior))
					throw new InternalError ();
				remain_stopped = false;
				return true;
			}
		}

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
