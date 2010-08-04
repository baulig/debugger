using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Server
{
	internal class RemoteBreakpointManager : BreakpointManager
	{
		RemoteDebuggerServer server;
		int iid;

		internal int IID {
			get { return iid; }
		}

		internal RemoteBreakpointManager (RemoteDebuggerServer server)
		{
			this.server = server;

			iid = server.Connection.CreateBreakpointManager ();
		}

		protected RemoteBreakpointManager (RemoteBreakpointManager old)
			: base (old)
		{
			this.server = old.server;
		}

		public override BreakpointManager Clone ()
		{
			return new RemoteBreakpointManager (this);
		}

		public override BreakpointHandle LookupBreakpoint (TargetAddress address,
								   out int index, out bool is_enabled)
		{
			index = server.Connection.LookupBreakpointByAddr (iid, address.Address, out is_enabled);
			if (!bpt_by_index.ContainsKey (index))
				return null;
			return bpt_by_index [index].Handle;
		}

		public override bool IsBreakpointEnabled (int breakpoint)
		{
			bool enabled;
			if (!server.Connection.LookupBreakpointById (iid, breakpoint, out enabled))
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
