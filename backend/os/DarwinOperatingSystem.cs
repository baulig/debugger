using System;
using System.IO;
using System.Collections;

using Mono.Debugger;
using Mono.Debugger.Server;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class DarwinOperatingSystem : OperatingSystemBackend
	{
		public DarwinOperatingSystem (Process process)
			: base (process)
		{ }

		protected override void CheckLoadedLibrary (Inferior inferior, ExecutableReader reader)
		{
			if (!Process.MonoRuntimeFound)
				check_for_mono_runtime (inferior, reader);
		}

		TargetAddress pending_mono_init = TargetAddress.Null;

		void check_for_mono_runtime (Inferior inferior, ExecutableReader reader)
		{
			TargetAddress info = reader.LookupSymbol ("MONO_DEBUGGER__debugger_info_ptr");
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

#if FIXME
				// Add a breakpoint in mini_debugger_init, to make sure that InitializeMono()
				// gets called in time to set the breakpoint at debugger_initialize, needed to 
				// initialize the notifications.
				TargetAddress mini_debugger_init = reader.LookupSymbol ("mini_debugger_init");
				if (!mini_debugger_init.IsNull)
				{
					Instruction insn = inferior.Architecture.ReadInstruction (inferior, mini_debugger_init);
					if ((insn == null) || !insn.CanInterpretInstruction)
						throw new InternalError ("Unknown dynlink breakpoint: {0}", mini_debugger_init);

					DynlinkBreakpoint init_breakpoint = new DynlinkBreakpoint (this, insn);
					init_breakpoint.Insert (inferior);
				}
#endif
				return;
			}
			
			Process.InitializeMono (inferior, data);
		}

		internal override bool CheckForPendingMonoInit (Inferior inferior)
		{
			if (pending_mono_init.IsNull)
				return false;

			TargetAddress data = inferior.ReadAddress (pending_mono_init);
			if (data.IsNull)
				return false;

			pending_mono_init = TargetAddress.Null;
			Process.InitializeMono (inferior, data);
			return true;
		}

		public override bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start)
		{
#if FIXME
			foreach (ExecutableReader reader in reader_hash.Values) {
				Bfd bfd = reader as Bfd;
				if (bfd == null)
					continue;
					
				if (bfd.GetTrampoline (memory, address, out trampoline, out is_start))
					return true;
			}
#endif

			is_start = false;
			trampoline = TargetAddress.Null;
			return false;
		}

		public TargetAddress GetSectionAddress (string name)
		{
			foreach (ExecutableReader reader in reader_hash.Values) {
				if (reader == null)
					continue;

				TargetAddress address = reader.GetSectionAddress (name);
				if (!address.IsNull)
					return address;
			}

			return TargetAddress.Null;
		}

#region Dynamic Linking

#if FIXME

		bool has_dynlink_info;
		TargetAddress dyld_all_image_infos = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;

		AddressBreakpoint dynlink_breakpoint;

		protected override void DoUpdateSharedLibraries (Inferior inferior, ExecutableReader main_reader)
		{
			if (has_dynlink_info) {
				if (!dyld_all_image_infos.IsNull)
					do_update_shlib_info (inferior);
				return;
			}

			TargetMemoryInfo info = Process.ThreadManager.GetTargetMemoryInfo (AddressDomain.Global);
			Bfd dyld_image = new Bfd (this, info, "/usr/lib/dyld", TargetAddress.Null, true);

			dyld_all_image_infos = dyld_image.LookupSymbol("dyld_all_image_infos");
			if (dyld_all_image_infos.IsNull)
				return;
			

			int size = 2 * inferior.TargetLongIntegerSize + 2 * inferior.TargetAddressSize;
			TargetReader reader = new TargetReader (inferior.ReadMemory (dyld_all_image_infos, size));

			reader.ReadLongInteger (); // version
			reader.ReadLongInteger (); // infoArrayCount
			reader.ReadAddress (); // infoArray
			TargetAddress dyld_image_notifier = reader.ReadAddress ();

			has_dynlink_info = true;

			Instruction insn = inferior.Architecture.ReadInstruction (inferior, dyld_image_notifier);
			if ((insn == null) || !insn.CanInterpretInstruction)
				throw new InternalError ("Unknown dynlink breakpoint: {0}", dyld_image_notifier);

			dynlink_breakpoint = new DynlinkBreakpoint (this, insn);
			dynlink_breakpoint.Insert (inferior);

			do_update_shlib_info (inferior);

			CheckLoadedLibrary (inferior, main_reader);
		}

		void do_update_shlib_info (Inferior inferior)
		{
//			if (Process.MonoRuntimeFound)
//				return;
			if (!dyld_all_image_infos.IsNull) {
				int size = 2 * inferior.TargetLongIntegerSize + 2 * inferior.TargetAddressSize;
				TargetReader reader = new TargetReader (inferior.ReadMemory (dyld_all_image_infos, size));

				reader.ReadLongInteger (); // version
				int infoArrayCount = (int)reader.ReadLongInteger ();
				TargetAddress infoArray = reader.ReadAddress ();

				size = infoArrayCount * (inferior.TargetLongIntegerSize + 2 * inferior.TargetAddressSize);
				reader = new TargetReader (inferior.ReadMemory (infoArray, size));
				Console.Write("Loading symbols for shared libraries:");
				for (int i=0; i<infoArrayCount; i++)
				{
					TargetAddress imageLoadAddress = reader.ReadAddress();
					TargetAddress imageFilePath = reader.ReadAddress();
					reader.ReadLongInteger(); //imageFileModDate
					string name = inferior.ReadString (imageFilePath);

					if (name == null)
						continue;
	
					if (reader_hash.ContainsKey (name))
						continue;
					
					try {
						Console.Write(".");
						AddExecutableFile (inferior, name, imageLoadAddress/*TargetAddress.Null*/, false, true);
					}
					catch (SymbolTableException e)
					{
						Console.WriteLine("Unable to load binary for "+name);
						reader_hash.Add (name, null);
					}
				}	
				Console.WriteLine("");
			}
		}

		protected class DynlinkBreakpoint : AddressBreakpoint
		{
			protected readonly DarwinOperatingSystem OS;
			public readonly Instruction Instruction;

			public DynlinkBreakpoint (DarwinOperatingSystem os, Instruction instruction)
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
				OS.do_update_shlib_info (inferior);
				if (!Instruction.InterpretInstruction (inferior))
					throw new InternalError ();
				remain_stopped = false;
				return true;
			}
		}

#else
		protected override void DoUpdateSharedLibraries (Inferior inferior, ExecutableReader main_reader)
		{ }
#endif

#endregion
	}
}
