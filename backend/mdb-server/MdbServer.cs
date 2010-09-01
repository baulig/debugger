using System;
using System.Collections.Generic;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal class MdbServer : ServerObject, IDebuggerServer
	{
		ThreadManager manager;
		Process process;

		SingleSteppingEngine main_sse;

		MdbProcess main_process;

		public MdbServer (Connection connection)
			: base (connection, -1, ServerObjectKind.Server)
		{ }

		bool initialized;
		TargetInfo target_info;
		ServerType server_type;
		ArchType arch_type;
		ServerCapabilities capabilities;
		MdbBreakpointManager bpm;

		void initialize ()
		{
			if (initialized)
				return;

			var info_reader = Connection.SendReceive (CommandSet.SERVER, (int) CmdServer.GET_TARGET_INFO, null);
			target_info = new TargetInfo (info_reader.ReadInt (), info_reader.ReadInt (), info_reader.ReadInt (), info_reader.ReadByte () != 0);

			server_type = (ServerType) Connection.SendReceive (CommandSet.SERVER, (int) CmdServer.GET_SERVER_TYPE, null).ReadInt ();
			arch_type = (ArchType) Connection.SendReceive (CommandSet.SERVER, (int) CmdServer.GET_ARCH_TYPE, null).ReadInt ();
			capabilities = (ServerCapabilities) Connection.SendReceive (CommandSet.SERVER, (int) CmdServer.GET_CAPABILITIES, null).ReadInt ();

			var bpm_iid = Connection.SendReceive (CommandSet.SERVER, (int) CmdServer.GET_BPM, null).ReadInt ();
			bpm = new MdbBreakpointManager (Connection, bpm_iid);

			initialized = true;
		}

		enum CmdServer {
			GET_TARGET_INFO = 1,
			GET_SERVER_TYPE = 2,
			GET_ARCH_TYPE = 3,
			GET_CAPABILITIES = 4,
			GET_BPM = 5,
			CREATE_PROCESS = 6
		}

		public TargetInfo TargetInfo {
			get {
				lock (this) {
					initialize ();
					return target_info;
				}
			}
		}

		public ServerType ServerType {
			get {
				lock (this) {
					initialize ();
					return server_type;
				}
			}
		}

		public ArchType ArchType {
			get {
				lock (this) {
					initialize ();
					return arch_type;
				}
			}
		}

		public ServerCapabilities Capabilities {
			get {
				lock (this) {
					initialize ();
					return capabilities;
				}
			}
		}

		public MdbBreakpointManager BreakpointManager {
			get {
				lock (this) {
					initialize ();
					return bpm;
				}
			}
		}

		IBreakpointManager IDebuggerServer.BreakpointManager {
			get { return BreakpointManager; }
		}

		public MdbProcess CreateProcess ()
		{
			var proc_iid = Connection.SendReceive (CommandSet.SERVER, (int) CmdServer.CREATE_PROCESS, null).ReadInt ();
			return new MdbProcess (Connection, proc_iid);
		}

		IProcess IDebuggerServer.CreateProcess ()
		{
			return CreateProcess ();
		}
	}
}
