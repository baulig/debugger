using System;
using System.IO;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Server
{
	internal class RemoteDebuggerServer : DebuggerServer
	{
		Connection connection;
		RemoteThreadManager manager;

		public RemoteDebuggerServer (Debugger debugger)
		{
			manager = new RemoteThreadManager (debugger, this);

			connection = new Connection (handle_event);

			connection.Connect ();
		}

		void handle_event (Connection.EventInfo e)
		{
			Console.WriteLine ("EVENT: {0} {1}", e.EventKind, e.ReqId);

			switch (e.EventKind) {
			case Connection.EventKind.TARGET_EVENT:
				Console.WriteLine ("TARGET EVENT: {0}", e.ChildEvent);
				manager.OnTargetEvent (e.ReqId, e.ChildEvent);
				break;
			default:
				throw new InternalError ();
			}
		}

		internal Connection Connection {
			get { return connection; }
		}

		class RemoteInferior : InferiorHandle
		{
			public readonly int IID;

			public RemoteInferior (int iid)
			{
				this.IID = iid;
			}
		}

		public override ThreadManager ThreadManager {
			get { return manager; }
		}

		public override BreakpointManager CreateBreakpointManager ()
		{
			return new RemoteBreakpointManager (this);
		}

		public override InferiorHandle CreateInferior (BreakpointManager breakpoint_manager)
		{
			var bpm = (RemoteBreakpointManager) breakpoint_manager;
			return new RemoteInferior (connection.CreateInferior (bpm.IID));
		}

		public override ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
								      string filename, TargetAddress base_address, bool is_loaded)
		{
			var reader = new RemoteExecutableReader (os, memory, this, filename);
			reader.ReadDebuggingInfo ();
			return reader;
		}

		public override void InitializeProcess (InferiorHandle inferior)
		{
			connection.InitializeProcess (((RemoteInferior) inferior).IID);
		}

		public override TargetError InitializeThread (InferiorHandle inferior, int child_pid, bool wait)
		{
			throw new NotImplementedException ();
		}

		public override int Spawn (InferiorHandle inferior, string working_dir, string[] argv, string[] envp,
					   bool redirect_fds, ChildOutputHandler output_handler)
		{
			return connection.Spawn (((RemoteInferior) inferior).IID, working_dir ?? Environment.CurrentDirectory, argv);
		}

		public override TargetError Attach (InferiorHandle inferior, int child_pid)
		{
			throw new NotImplementedException ();
		}

		public override ServerStackFrame GetFrame (InferiorHandle inferior)
		{
			return connection.GetFrame (((RemoteInferior) inferior).IID);
		}

		public override TargetError CurrentInsnIsBpt (InferiorHandle inferior, out int is_breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override void Step (InferiorHandle inferior)
		{
			connection.Step (((RemoteInferior) inferior).IID);
		}

		public override void Continue (InferiorHandle inferior)
		{
			connection.Continue (((RemoteInferior) inferior).IID);
		}

		public override void Resume (InferiorHandle inferior)
		{
			connection.Resume (((RemoteInferior) inferior).IID);
		}

		public override TargetError Detach (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Finalize (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override byte[] ReadMemory (InferiorHandle inferior, long address, int size)
		{
			return connection.ReadMemory (((RemoteInferior) inferior).IID, address, size);
		}

		public override void WriteMemory (InferiorHandle inferior, long start, byte[] buffer)
		{
			connection.WriteMemory (((RemoteInferior) inferior).IID, start, buffer);
		}

		public override TargetInfo GetTargetInfo ()
		{
			check_disposed ();
			return connection.GetTargetInfo ();
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
			return connection.InsertBreakpoint (((RemoteInferior) inferior).IID, address);
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
			connection.RemoveBreakpoint (((RemoteInferior) inferior).IID, breakpoint);
		}

		public override void EnableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			connection.EnableBreakpoint (((RemoteInferior) inferior).IID, breakpoint);
		}

		public override void DisableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			connection.DisableBreakpoint (((RemoteInferior) inferior).IID, breakpoint);
		}

		public override long[] GetRegisters (InferiorHandle inferior)
		{
			return connection.GetRegisters (((RemoteInferior) inferior).IID);
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
			connection.SetSignal (((RemoteInferior) inferior).IID, signal, send_it);
		}

		public override int GetPendingSignal (InferiorHandle inferior)
		{
			return connection.GetPendingSignal (((RemoteInferior) inferior).IID);
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
			return connection.GetSignalInfo (((RemoteInferior) inferior).IID);
		}

		public override TargetError GetThreads (InferiorHandle inferior, out int[] threads)
		{
			throw new NotImplementedException ();
		}

		public override string GetApplication (InferiorHandle inferior, out string cwd,
						       out string[] cmdline_args)
		{
			return connection.GetApplication (((RemoteInferior) inferior).IID, out cwd, out cmdline_args);
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

		public override ServerType GetServerType ()
		{
			check_disposed ();
			return connection.GetServerType ();
		}

		public override ServerCapabilities GetCapabilities ()
		{
			check_disposed ();
			return connection.GetCapabilities ();
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

		internal long ReadDynamicInfo (Inferior inferior, int bfd_iid)
		{
			var handle = (RemoteInferior) inferior.InferiorHandle;
			return connection.BfdGetDynamicInfo (handle.IID, bfd_iid);
		}

		public string DisassembleInsn (Inferior inferior, long address, out int insn_size)
		{
			var handle = (RemoteInferior) inferior.InferiorHandle;
			return connection.DisassembleInsn (handle.IID, address, out insn_size);
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

