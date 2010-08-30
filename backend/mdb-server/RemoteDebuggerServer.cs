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
		MdbServer server;
		RemoteThreadManager manager;
		ServerCapabilities capabilities;
		ServerType server_type;
		ArchTypeEnum arch_type;

		public RemoteDebuggerServer (Debugger debugger, IPEndPoint endpoint)
		{
			manager = new RemoteThreadManager (debugger, this);

			connection = new Connection ();
			server = connection.Connect (endpoint);

			server_type = server.GetServerType ();
			capabilities = server.GetCapabilities ();
			arch_type = server.GetArchType ();
		}

		public override ServerType Type {
			get { return server_type; }
		}

		public override ServerCapabilities Capabilities {
			get { return capabilities; }
		}

		public override ArchTypeEnum ArchType {
			get { return arch_type; }
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

		internal MdbServer Server {
			get { return server; }
		}

		public override ThreadManager ThreadManager {
			get { return manager; }
		}

		public override IBreakpointManager CreateBreakpointManager ()
		{
			return server.CreateBreakpointManager ();
		}

		public override IInferior CreateInferior (SingleSteppingEngine sse, Inferior inferior,
							  IBreakpointManager breakpoint_manager)
		{
			return server.CreateInferior ((MdbBreakpointManager) breakpoint_manager);
		}

		public override ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
								      string filename, TargetAddress base_address, bool is_loaded)
		{
			var mdb_reader = server.CreateExeReader (filename);
			var reader = new ExecutableReader (os, memory, this, mdb_reader, filename);
			reader.ReadDebuggingInfo ();
			return reader;
		}

		public override TargetInfo GetTargetInfo ()
		{
			check_disposed ();
			return server.GetTargetInfo ();
		}

		public override MonoRuntimeHandle InitializeMonoRuntime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size)
		{
			throw new NotImplementedException ();
		}

		public override void InitializeCodeBuffer (MonoRuntimeHandle runtime, long executable_code_buffer,
							   int executable_code_buffer_size)
		{
			throw new NotImplementedException ();
		}

		public override void FinalizeMonoRuntime (MonoRuntimeHandle runtime)
		{
			throw new NotImplementedException ();
		}

		public Disassembler GetDisassembler ()
		{
			return new RemoteDisassembler (this);
		}

                protected string DisassembleInsn (Inferior inferior, long address, out int insn_size)
                {
                        var handle = (MdbInferior) inferior.InferiorHandle;
                        return handle.DisassembleInstruction (address, out insn_size);
                }

		class RemoteDisassembler : Disassembler
		{
			public readonly RemoteDebuggerServer Server;

			public RemoteDisassembler (RemoteDebuggerServer server)
			{
				this.Server = server;
			}

			public override int GetInstructionSize (TargetMemoryAccess memory, TargetAddress address)
			{
				int insn_size;
				Server.DisassembleInsn ((Inferior) memory, address.Address, out insn_size);
				return insn_size;
			}

			public override AssemblerMethod DisassembleMethod (TargetMemoryAccess memory, Method method)
			{
				throw new NotImplementedException ();
			}

			public override AssemblerLine DisassembleInstruction (TargetMemoryAccess memory,
									      Method method, TargetAddress address)
			{
				int insn_size;
				var insn = Server.DisassembleInsn ((Inferior) memory, address.Address, out insn_size);
				return new AssemblerLine (null, address, (byte) insn_size, insn);
			}
		}
	}
}
