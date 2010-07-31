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

		public RemoteDebuggerServer ()
		{
			connection = new Connection ();
			connection.Connect ();
		}

		public override InferiorHandle CreateInferior (BreakpointManager breakpoint_manager)
		{
			throw new NotImplementedException ();
		}

		public override TargetError InitializeProcess (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError InitializeThread (InferiorHandle inferior, int child_pid, bool wait)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Spawn (InferiorHandle inferior, string working_dir, string[] argv, string[] envp,
						   bool redirect_fds, out int child_pid, out string error,
						   ChildOutputHandler output_handler)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Attach (InferiorHandle inferior, int child_pid)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetFrame (InferiorHandle inferior, out ServerStackFrame frame)
		{
			throw new NotImplementedException ();
		}

		public override TargetError CurrentInsnIsBpt (InferiorHandle inferior, out int is_breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Step (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Continue (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Resume (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Detach (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError Finalize (InferiorHandle inferior)
		{
			throw new NotImplementedException ();
		}

		public override TargetError ReadMemory (InferiorHandle inferior, long address, int size, out byte[] buffer)
		{
			throw new NotImplementedException ();
		}

		public override TargetError WriteMemory (InferiorHandle inferior, long start, byte[] buffer)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetTargetInfo (out int target_int_size, out int target_long_size,
							   out int target_address_size, out int is_bigendian)
		{
			throw new NotImplementedException ();
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

		public override TargetError InsertBreakpoint (InferiorHandle inferior, long address, out int breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError InsertHardwareBreakpoint (InferiorHandle inferior, HardwareBreakpointType type,
								      out int index, long address, out int breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError RemoveBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError EnableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError DisableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetRegisters (InferiorHandle inferior, out long[] registers)
		{
			throw new NotImplementedException ();
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

		public override TargetError SetSignal (InferiorHandle inferior, int signal, int send_it)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetPendingSignal (InferiorHandle inferior, out int signal)
		{
			throw new NotImplementedException ();
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

		public override TargetError GetSignalInfo (InferiorHandle inferior, out SignalInfo info)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetThreads (InferiorHandle inferior, out int[] threads)
		{
			throw new NotImplementedException ();
		}

		public override TargetError GetApplication (InferiorHandle inferior, out string exe, out string cwd,
							    out string[] cmdline_args)
		{
			throw new NotImplementedException ();
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
			throw new NotImplementedException ();
		}

		public override ServerType GetServerType ()
		{
			throw new NotImplementedException ();
		}

		public override ServerCapabilities GetCapabilities ()
		{
			throw new NotImplementedException ();
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
	}
}

