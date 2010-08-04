using System;
using System.IO;
using System.Linq;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Languages;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Server
{
	internal class RemoteThreadManager : ThreadManager
	{
		new RemoteDebuggerServer server;

		internal RemoteThreadManager (Debugger debugger, RemoteDebuggerServer server)
			: base (debugger, server)
		{
			this.server = server;
		}

		public override bool HasTarget {
			get { return server != null; }
		}

		internal override bool GetPendingSigstopForNewThread (int pid)
		{
			return false;
		}

		internal override bool InBackgroundThread {
			get { return true; }
		}

		internal override object SendCommand (SingleSteppingEngine sse, TargetAccessDelegate target,
						      object user_data)
		{
			return target (sse.Thread, user_data);
		}

		public override Process StartApplication (ProcessStart start, out CommandResult result)
		{
			Process process = new Process (this, start);
			processes.Add (process);

			result = process.StartApplication ();

			return process;
		}

		internal override void AddPendingEvent (SingleSteppingEngine engine, DebuggerServer.ChildEvent cevent)
		{
			throw new InvalidOperationException ();
		}

		internal void OnTargetEvent (int pid, DebuggerServer.ChildEvent cevent)
		{
			Console.WriteLine ("ON TARGET EVENT: {0} {1}", pid, cevent);
			SingleSteppingEngine engine = (SingleSteppingEngine) thread_hash [pid];
			try {
				engine.ProcessEvent (cevent);
			} catch (Exception ex) {
				Console.WriteLine ("ON TARGET EVENT EX: {0}", ex);
			}
		}
	}
}
