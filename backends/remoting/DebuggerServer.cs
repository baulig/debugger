using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Lifetime;

using Mono.Debugger;
using Mono.Debugger.Remoting;

namespace Mono.Debugger.Remoting
{
	public class DebuggerServer : DebuggerBackend
	{
		static DebuggerChannel channel;
		static DebuggerBackend global_server;

		protected static void Run (string url)
		{
			RemotingConfiguration.RegisterActivatedServiceType (
				typeof (DebuggerServer));

			channel = new DebuggerChannel (url);
			ChannelServices.RegisterChannel (channel);

			channel.Connection.Run ();
			ChannelServices.UnregisterChannel (channel);
		}

		public DebuggerServer ()
		{
			DebuggerExitedEvent += new TargetExitedHandler (backend_exited);

			if (global_server != null)
				throw new InternalError ();

			global_server = this;
		}

		void backend_exited ()
		{
			RemotingServices.Disconnect (this);
		}

		public DebuggerBackend DebuggerBackend {
			get { return this; }
		}

		new internal static ThreadManager ThreadManager {
			get { return global_server.ThreadManager; }
		}
	}
}

