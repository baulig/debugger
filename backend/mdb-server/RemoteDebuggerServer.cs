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

		public override BreakpointManager CreateBreakpointManager ()
		{
			return new RemoteBreakpointManager (this);
		}

		public override InferiorHandle CreateInferior (SingleSteppingEngine sse, Inferior inferior,
							       BreakpointManager breakpoint_manager)
		{
			var bpm = (RemoteBreakpointManager) breakpoint_manager;
			return server.CreateInferior (bpm.MdbBreakpointManager);
		}

		public override ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
								      string filename, TargetAddress base_address, bool is_loaded)
		{
			var mdb_reader = server.CreateExeReader (filename);
			var reader = new ExecutableReader (os, memory, this, mdb_reader, filename);
			reader.ReadDebuggingInfo ();
			return reader;
		}

		public override void InitializeProcess (InferiorHandle inferior)
		{
			((MdbInferior) inferior).InitializeProcess ();
		}

		public override TargetError InitializeThread (InferiorHandle inferior, int child_pid, bool wait)
		{
			throw new NotImplementedException ();
		}

		public override int Spawn (InferiorHandle inferior, string working_dir, string[] argv, string[] envp,
					   bool redirect_fds, ChildOutputHandler output_handler)
		{
			return ((MdbInferior) inferior).Spawn (working_dir, argv);
		}

		public override TargetError Attach (InferiorHandle inferior, int child_pid)
		{
			throw new NotImplementedException ();
		}

		public override ServerStackFrame GetFrame (InferiorHandle inferior)
		{
			return ((MdbInferior) inferior).GetFrame ();
		}

		public override TargetError CurrentInsnIsBpt (InferiorHandle inferior, out int is_breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override void Step (InferiorHandle inferior)
		{
			((MdbInferior) inferior).Step ();
		}

		public override void Continue (InferiorHandle inferior)
		{
			((MdbInferior) inferior).Continue ();
		}

		public override void Resume (InferiorHandle inferior)
		{
			((MdbInferior) inferior).Resume ();
		}

		public override TargetError Detach (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Finalize (InferiorHandle inferior)
		{
			((MdbInferior) inferior).Dispose ();
			connection = null;
			return TargetError.None;
		}

		public override byte[] ReadMemory (InferiorHandle inferior, long address, int size)
		{
			return ((MdbInferior) inferior).ReadMemory (address, size);
		}

		public override void WriteMemory (InferiorHandle inferior, long start, byte[] buffer)
		{
			((MdbInferior) inferior).WriteMemory (start, buffer);
		}

		public override TargetInfo GetTargetInfo ()
		{
			check_disposed ();
			return server.GetTargetInfo ();
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address, long arg1, long arg2,
							long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address, long arg1, long arg2, long arg3,
							string string_arg, long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address, byte[] data, long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address, long arg1, long arg2,
							byte[] data, long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public override TargetError MarkRuntimeInvokeFrame (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError AbortInvoke (InferiorHandle inferior, long rti_id)
		{
			throw new NotImplementedException ();
		}

		public override TargetError RuntimeInvoke (InferiorHandle inferior, long invoke_method, long method_address,
							   int num_params, byte[] blob, int[] blob_offsets, long[] addresses,
							   long callback_arg, bool debug)
		{
			throw new NotImplementedException ();
		}

		public override TargetError ExecuteInstruction (InferiorHandle inferior, byte[] instruction, bool update_ip)
		{
			throw new NotImplementedException ();
		}

		public override int InsertBreakpoint (InferiorHandle inferior, long address)
		{
			return ((MdbInferior) inferior).InsertBreakpoint (address);
		}

		public override TargetError InsertHardwareBreakpoint (InferiorHandle inferior, HardwareBreakpointType type,
								      out int index, long address, out int breakpoint)
		{
			index = -1;
			breakpoint = -1;
			return TargetError.NotImplemented;
		}

		public override void RemoveBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			((MdbInferior) inferior).RemoveBreakpoint (breakpoint);
		}

		public override void EnableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			((MdbInferior) inferior).EnableBreakpoint (breakpoint);
		}

		public override void DisableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			((MdbInferior) inferior).DisableBreakpoint (breakpoint);
		}

		public override long[] GetRegisters (InferiorHandle inferior)
		{
			return ((MdbInferior) inferior).GetRegisters ();
		}

		public override TargetError SetRegisters (InferiorHandle inferior, long[] registers)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Stop (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError StopAndWait (InferiorHandle inferior, out int status)
		{
			throw new NotImplementedException ();
		}

		public override void SetSignal (InferiorHandle inferior, int signal, bool send_it)
		{
			((MdbInferior) inferior).SetSignal (signal, send_it);
		}

		public override int GetPendingSignal (InferiorHandle inferior)
		{
			return ((MdbInferior) inferior).GetPendingSignal ();
		}

		public override TargetError Kill (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override ChildEventType DispatchEvent (InferiorHandle inferior, int status, out long arg,
							      out long data1, out long data2, out byte[] opt_data)
		{
			throw new NotImplementedException ();
		}

		public override SignalInfo GetSignalInfo (InferiorHandle inferior)
		{
			return ((MdbInferior) inferior).GetSignalInfo ();
		}

		public override TargetError GetThreads (InferiorHandle inferior, out int[] threads)
		{
			throw new NotImplementedException ();
		}

		public override string GetApplication (InferiorHandle inferior, out string cwd,
						       out string[] cmdline_args)
		{
			return ((MdbInferior) inferior).GetApplication (out cwd, out cmdline_args);
		}

		public override TargetError DetachAfterFork (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError PushRegisters (InferiorHandle inferior, out long new_rsp)
		{
			throw new NotImplementedException ();
		}

		public override TargetError PopRegisters (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetCallbackFrame (InferiorHandle inferior, long stack_pointer, bool exact_match,
							      out CallbackFrame info)
		{
			info = null;
			return TargetError.NoCallbackFrame;
		}

		public override TargetError RestartNotification (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override MonoRuntimeHandle InitializeMonoRuntime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size)
		{
			throw new NotImplementedException ();
		}

		public override void SetRuntimeInfo (InferiorHandle inferior, MonoRuntimeHandle runtime)
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

		internal override void InitializeAtEntryPoint (Inferior inferior)
		{
			var handle = (MdbInferior) inferior.InferiorHandle;
			handle.InitializeAtEntryPoint ();
		}

		public string DisassembleInsn (Inferior inferior, long address, out int insn_size)
		{
			var handle = (MdbInferior) inferior.InferiorHandle;
			return handle.DisassembleInsn (address, out insn_size);
		}

		public Disassembler GetDisassembler ()
		{
			return new RemoteDisassembler (this);
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
