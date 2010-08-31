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
		{
			var info_reader = connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_TARGET_INFO, null);
			TargetInfo = new TargetInfo (info_reader.ReadInt (), info_reader.ReadInt (), info_reader.ReadInt (), info_reader.ReadByte () != 0);

			ServerType = (ServerType) connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_SERVER_TYPE, null).ReadInt ();
			ArchType = (ArchType) connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_ARCH_TYPE, null).ReadInt ();
			Capabilities = (ServerCapabilities) connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_CAPABILITIES, null).ReadInt ();

			var bpm_iid = connection.SendReceive (CommandSet.SERVER, (int)CmdServer.GET_BPM, null).ReadInt ();
			BreakpointManager = new MdbBreakpointManager (connection, bpm_iid);
		}

		enum CmdServer {
			GET_TARGET_INFO = 1,
			GET_SERVER_TYPE = 2,
			GET_ARCH_TYPE = 3,
			GET_CAPABILITIES = 4,
			GET_BPM = 5,
			SPAWN = 6
		}

		public TargetInfo TargetInfo {
			get; private set;
		}

		public ServerType ServerType {
			get; private set;
		}

		public ArchType ArchType {
			get; private set;
		}

		public ServerCapabilities Capabilities {
			get; private set;
		}

		public MdbBreakpointManager BreakpointManager {
			get; private set;
		}

		IBreakpointManager IDebuggerServer.BreakpointManager {
			get { return BreakpointManager; }
		}

		Dictionary<int,SingleSteppingEngine> sse_by_inferior = new Dictionary<int,SingleSteppingEngine> ();

		public MdbInferior Spawn (SingleSteppingEngine sse, string cwd, string[] argv, string[] envp,
					  out MdbProcess process)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteString (cwd ?? "");

			int argc = argv.Length;
			if (argv [argc-1] == null)
				argc--;
			writer.WriteInt (argc);
			for (int i = 0; i < argc; i++)
				writer.WriteString (argv [i] ?? "dummy");
			var reader = Connection.SendReceive (CommandSet.SERVER, (int)CmdServer.SPAWN, writer);
			int process_iid = reader.ReadInt ();
			int inferior_iid = reader.ReadInt ();
			int pid = reader.ReadInt ();
			process = main_process = new MdbProcess (Connection, process_iid);

			main_sse = sse;
			manager = sse.ThreadManager;
			this.process = sse.Process;

			var inferior = new MdbInferior (Connection, inferior_iid);
			sse_by_inferior.Add (inferior_iid, sse);

			Console.WriteLine ("SPAWN: {0}", inferior_iid);

			return inferior;
		}

		IInferior IDebuggerServer.Spawn (SingleSteppingEngine sse, string cwd, string[] argv, string[] envp,
						 out IProcess process)
		{
			MdbProcess mdb_process;
			var inferior = Spawn (sse, cwd, argv, envp, out mdb_process);
			process = mdb_process;
			return inferior;
		}

		public MdbInferior Attach (SingleSteppingEngine sse, int pid, out MdbProcess process)
		{
			throw new NotImplementedException ();
		}

		IInferior IDebuggerServer.Attach (SingleSteppingEngine sse, int pid, out IProcess process)
		{
			MdbProcess mdb_process;
			var inferior = Attach (sse, pid, out mdb_process);
			process = mdb_process;
			return inferior;
		}

		void OnDllLoaded (MdbExeReader reader)
		{
			Console.WriteLine ("DLL LOADED: {0}", reader.FileName);

			var exe = new ExecutableReader (process, main_sse.TargetMemoryInfo, reader);
			exe.ReadDebuggingInfo ();
		}

		void OnThreadCreated (MdbInferior inferior)
		{
			Console.WriteLine ("THREAD CREATED: {0}", inferior.ID);

			var sse = process.ThreadCreated (main_process, inferior);
			sse_by_inferior.Add (inferior.ID, sse);
		}

		internal void HandleEvent (ServerEvent e)
		{
			Console.WriteLine ("SERVER EVENT: {0} {1}", e, DebuggerWaitHandle.CurrentThread);

			if (e.Sender.Kind == ServerObjectKind.Inferior) {
				var inferior = (MdbInferior) e.Sender;
				Console.WriteLine ("INFERIOR EVENT: {0}", inferior.ID);

				if (!sse_by_inferior.ContainsKey (inferior.ID)) {
					Console.WriteLine ("UNKNOWN INFERIOR !");
					return;
				}

				var sse = sse_by_inferior[inferior.ID];
				sse.ProcessEvent (e);
			}

			switch (e.Type) {
			case ServerEventType.MainModuleLoaded:
			case ServerEventType.DllLoaded:
				OnDllLoaded ((MdbExeReader) e.ArgumentObject);
				break;

			case ServerEventType.ThreadCreated:
				OnThreadCreated ((MdbInferior) e.ArgumentObject);
				break;
			}
		}
	}
}
