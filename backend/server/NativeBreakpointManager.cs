#if FIXME
using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Server
{
	internal class NativeBreakpointManager : BreakpointManager
	{
		IntPtr _manager;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_new ();

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_clone (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_free (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup (IntPtr manager, long address);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup_by_id (IntPtr manager, int id);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_lock ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_unlock ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_breakpoint_info_get_id (IntPtr info);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_breakpoint_info_get_is_enabled (IntPtr info);

		public NativeBreakpointManager ()
		{
			_manager = mono_debugger_breakpoint_manager_new ();
		}

		protected NativeBreakpointManager (NativeBreakpointManager old)
			: base (old)
		{
			Lock ();

			_manager = mono_debugger_breakpoint_manager_clone (old.Manager);

			Unlock ();
		}

		public override BreakpointManager Clone ()
		{
			return new NativeBreakpointManager (this);
		}

		protected override void Lock ()
		{
			mono_debugger_breakpoint_manager_lock ();
		}

		protected override void Unlock ()
		{
			mono_debugger_breakpoint_manager_unlock ();
		}

		internal IntPtr Manager {
			get { return _manager; }
		}

		public override BreakpointHandle LookupBreakpoint (TargetAddress address,
								   out int index, out bool is_enabled)
		{
			Lock ();
			try {
				IntPtr info = mono_debugger_breakpoint_manager_lookup (
					_manager, address.Address);
				if (info == IntPtr.Zero) {
					index = 0;
					is_enabled = false;
					return null;
				}

				index = mono_debugger_breakpoint_info_get_id (info);
				is_enabled = mono_debugger_breakpoint_info_get_is_enabled (info);
				if (!bpt_by_index.ContainsKey (index))
					return null;
				return bpt_by_index [index].Handle;
			} finally {
				Unlock ();
			}
		}

		public override bool IsBreakpointEnabled (int breakpoint)
		{
			Lock ();
			try {
				IntPtr info = mono_debugger_breakpoint_manager_lookup_by_id (
					_manager, breakpoint);
				if (info == IntPtr.Zero)
					return false;

				return mono_debugger_breakpoint_info_get_is_enabled (info);
			} finally {
				Unlock ();
			}
		}

		//
		// IDisposable
		//

		protected override void DoDispose ()
		{
			mono_debugger_breakpoint_manager_free (_manager);
			_manager = IntPtr.Zero;
		}
	}
}
#endif
