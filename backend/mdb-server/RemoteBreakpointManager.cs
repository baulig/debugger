using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal class RemoteBreakpointManager : BreakpointManager
	{
		RemoteDebuggerServer server;
		MdbBreakpointManager bpm;

		public MdbBreakpointManager MdbBreakpointManager {
			get { return bpm; }
		}

		internal RemoteBreakpointManager (RemoteDebuggerServer server)
		{
			this.server = server;
			this.bpm = server.Server.CreateBreakpointManager ();
		}

		protected RemoteBreakpointManager (RemoteBreakpointManager old)
			: base (old)
		{
			this.server = old.server;
			this.bpm = old.bpm;
		}

		public override BreakpointManager Clone ()
		{
			return new RemoteBreakpointManager (this);
		}

		public override BreakpointHandle LookupBreakpoint (TargetAddress address,
								   out int index, out bool is_enabled)
		{
			index = bpm.LookupBreakpointByAddr (address.Address, out is_enabled);
			if (!bpt_by_index.ContainsKey (index))
				return null;
			return bpt_by_index [index].Handle;
		}

		public override bool IsBreakpointEnabled (int breakpoint)
		{
			bool enabled;
			if (!bpm.LookupBreakpointById (breakpoint, out enabled))
				return false;
			return enabled;
		}

		//
		// IDisposable
		//

		protected override void DoDispose ()
		{ }
	}
}
