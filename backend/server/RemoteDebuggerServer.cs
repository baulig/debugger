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

		class RemoteInferior : InferiorHandle
		{
			public readonly int IID;

			public RemoteInferior (int iid)
			{
				this.IID = iid;
			}
		}

		public override InferiorHandle CreateInferior (BreakpointManager breakpoint_manager)
		{
			return new RemoteInferior (connection.CreateInferior ());
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

		public override TargetError InsertBreakpoint (InferiorHandle inferior, long address, out int breakpoint)
		{
			throw new NotImplementedException ();
		}

		public override TargetError InsertHardwareBreakpoint (InferiorHandle inferior, HardwareBreakpointType type,
								      out int index, long address, out int breakpoint)
		{
			index = -1;
			breakpoint = -1;
			return TargetError.NotImplemented;
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
			throw new NotImplementedException ();
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
	}
}

