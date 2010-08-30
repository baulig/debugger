using System;
using System.IO;
using System.Net;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;
using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class RemoteDebuggerServer : DebuggerServer
	{
		Connection connection;

		protected RemoteDebuggerServer (Debugger debugger, Connection connection, IDebuggerServer server)
			: base (debugger, server)
		{
			this.connection = connection;
		}

		public static DebuggerServer Create (Debugger debugger, IPEndPoint endpoint)
		{
			var connection = new Connection ();
			var server = connection.Connect (endpoint);

			return new RemoteDebuggerServer (debugger, connection, server);
		}

#if FIXME
		void handle_event (Connection.EventInfo e)
		{
			SingleSteppingEngine sse = null;
			if (e.ReqId > 0)
				sse = sse_hash [e.ReqId];

			Console.WriteLine ("EVENT: {0} {1} {2}", e.EventKind, e.ReqId, sse);

			switch (e.EventKind) {
			case Connection.EventKind.TARGET_EVENT:
				Console.WriteLine ("TARGET EVENT: {0} {1}", e.ChildEvent, sse);
				try {
					sse.ProcessEvent (e.ChildEvent);
				} catch (Exception ex) {
					Console.WriteLine ("ON TARGET EVENT EX: {0}", ex);
				}
				break;
			default:
				throw new InternalError ();
			}
		}
#endif

		internal Connection Connection {
			get { return connection; }
		}
	}
}
