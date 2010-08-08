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
	internal abstract class DebuggerServer : DebuggerMarshalByRefObject, IDisposable
	{
		internal abstract class InferiorHandle
		{ }

		public abstract ThreadManager ThreadManager {
			get;
		}

		public abstract ServerType Type {
			get;
		}

		public abstract ServerCapabilities Capabilities {
			get;
		}

		public abstract BreakpointManager CreateBreakpointManager ();

		public abstract InferiorHandle CreateInferior (SingleSteppingEngine sse, Inferior inferior,
							       BreakpointManager breakpoint_manager);

		public abstract void InitializeProcess (InferiorHandle inferior);

		public abstract TargetError InitializeThread (InferiorHandle inferior, int child_pid, bool wait);

		public abstract ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
								      string filename, TargetAddress base_address, bool is_loaded);

		internal struct ServerStackFrame
		{
			public long Address;
			public long StackPointer;
			public long FrameAddress;
		}

		internal enum ChildEventType {
			NONE = 0,
			UNKNOWN_ERROR = 1,
			CHILD_EXITED,
			CHILD_STOPPED,
			CHILD_SIGNALED,
			CHILD_CALLBACK,
			CHILD_CALLBACK_COMPLETED,
			CHILD_HIT_BREAKPOINT,
			CHILD_MEMORY_CHANGED,
			CHILD_CREATED_THREAD,
			CHILD_FORKED,
			CHILD_EXECD,
			CHILD_CALLED_EXIT,
			CHILD_NOTIFICATION,
			CHILD_INTERRUPTED,
			RUNTIME_INVOKE_DONE,
			INTERNAL_ERROR,

			UNHANDLED_EXCEPTION	= 4001,
			THROW_EXCEPTION,
			HANDLE_EXCEPTION
		}

		internal delegate void ChildEventHandler (ChildEventType message, int arg);

		internal sealed class ChildEvent
		{
			public readonly ChildEventType Type;
			public readonly long Argument;

			public readonly long Data1;
			public readonly long Data2;

			public readonly byte[] CallbackData;

			public ChildEvent (ChildEventType type, long arg, long data1, long data2)
			{
				this.Type = type;
				this.Argument = arg;
				this.Data1 = data1;
				this.Data2 = data2;
			}

			public ChildEvent (ChildEventType type, long arg, long data1, long data2,
					   byte[] callback_data)
				: this (type, arg, data1, data2)
			{
				this.CallbackData = callback_data;
			}

			public override string ToString ()
			{
				return String.Format ("ChildEvent ({0}:{1}:{2:x}:{3:x})",
						      Type, Argument, Data1, Data2);
			}
		}

		internal enum HardwareBreakpointType {
			NONE = 0,
			EXECUTE,
			READ,
			WRITE
		}

		internal enum ServerType
		{
			UNKNOWN = 0,
			LINUX_PTRACE = 1,
			DARWIN = 2,
			WINDOWS = 3
		}

		internal enum ServerCapabilities
		{
			NONE = 0,
			THREAD_EVENTS = 1,
			CAN_DETACH_ANY = 2,
			HAS_SIGNALS = 4
		}

		[StructLayout(LayoutKind.Sequential)]
		internal struct SignalInfo
		{
			public int SIGKILL;
			public int SIGSTOP;
			public int SIGINT;
			public int SIGCHLD;

			public int SIGFPE;
			public int SIGQUIT;
			public int SIGABRT;
			public int SIGSEGV;
			public int SIGILL;
			public int SIGBUS;
			public int SIGWINCH;

			public int Kernel_SIGRTMIN;
			public int MonoThreadAbortSignal;

			public override string ToString ()
			{
				return String.Format ("SignalInfo ({0}:{1}:{2}:{3}:{4} - {5})",
						      SIGKILL, SIGSTOP, SIGINT, SIGCHLD, Kernel_SIGRTMIN,
						      MonoThreadAbortSignal);
			}
		}

		internal delegate void ChildOutputHandler (bool is_stderr, string output);

		public abstract int Spawn (InferiorHandle inferior, string working_dir, string[] argv, string[] envp,
					   bool redirect_fds, ChildOutputHandler output_handler);

		public abstract TargetError Attach (InferiorHandle inferior, int child_pid);

		public abstract ServerStackFrame GetFrame (InferiorHandle inferior);

		public abstract TargetError CurrentInsnIsBpt (InferiorHandle inferior, out int is_breakpoint);

		public abstract void Step (InferiorHandle inferior);

		public abstract void Continue (InferiorHandle inferior);

		public abstract void Resume (InferiorHandle inferior);

		public abstract TargetError Detach (InferiorHandle inferior);

		public abstract TargetError Finalize (InferiorHandle inferior);

		public abstract byte[] ReadMemory (InferiorHandle inferior, long address, int size);

		public abstract void WriteMemory (InferiorHandle inferior, long start, byte[] buffer);

		public abstract TargetInfo GetTargetInfo ();

		public abstract TargetError CallMethod (InferiorHandle inferior, long method_address, long arg1, long arg2,
							long callback_arg);

		public abstract TargetError CallMethod (InferiorHandle inferior, long method_address, long arg1, long arg2, long arg3,
							string string_arg, long callback_arg);

		public abstract TargetError CallMethod (InferiorHandle inferior, long method_address, byte[] data, long callback_arg);

		public abstract TargetError CallMethod (InferiorHandle inferior, long method_address, long arg1, long arg2,
							byte[] data, long callback_arg);

		public abstract TargetError MarkRuntimeInvokeFrame (InferiorHandle inferior);

		public abstract TargetError AbortInvoke (InferiorHandle inferior, long rti_id);

		public abstract TargetError RuntimeInvoke (InferiorHandle inferior, long invoke_method, long method_address,
							   int num_params, byte[] blob, int[] blob_offsets, long[] addresses,
							   long callback_arg, bool debug);

		public abstract TargetError ExecuteInstruction (InferiorHandle inferior, byte[] instruction, bool update_ip);

		public abstract int InsertBreakpoint (InferiorHandle inferior, long address);

		public abstract TargetError InsertHardwareBreakpoint (InferiorHandle inferior, HardwareBreakpointType type,
								      out int index, long address, out int breakpoint);

		public abstract void RemoveBreakpoint (InferiorHandle inferior, int breakpoint);

		public abstract void EnableBreakpoint (InferiorHandle inferior, int breakpoint);

		public abstract void DisableBreakpoint (InferiorHandle inferior, int breakpoint);

		public abstract long[] GetRegisters (InferiorHandle inferior);

		public abstract TargetError SetRegisters (InferiorHandle inferior, long[] registers);

		public abstract TargetError Stop (InferiorHandle inferior);

		public abstract TargetError StopAndWait (InferiorHandle inferior, out int status);

		public abstract void SetSignal (InferiorHandle inferior, int signal, bool send_it);

		public abstract int GetPendingSignal (InferiorHandle inferior);

		public abstract TargetError Kill (InferiorHandle inferior);

		public abstract ChildEventType DispatchEvent (InferiorHandle inferior, int status, out long arg,
							      out long data1, out long data2, out byte[] opt_data);

		public abstract SignalInfo GetSignalInfo (InferiorHandle inferior);

		public abstract TargetError GetThreads (InferiorHandle inferior, out int[] threads);

		public abstract string GetApplication (InferiorHandle inferior, out string cwd,
						       out string[] cmdline_args);

		public abstract TargetError DetachAfterFork (InferiorHandle inferior);

		public abstract TargetError PushRegisters (InferiorHandle inferior, out long new_rsp);

		public abstract TargetError PopRegisters (InferiorHandle inferior);

		public abstract TargetError GetCallbackFrame (InferiorHandle inferior, long stack_pointer, bool exact_match,
							      out CallbackFrame info);

		internal class CallbackFrame
		{
			public readonly long ID;
			public readonly long CallAddress;
			public readonly long StackPointer;
			public readonly bool IsRuntimeInvokeFrame;
			public readonly bool IsExactMatch;
			public readonly long[] Registers;

			public CallbackFrame (IntPtr data, int count_regs)
			{
				ID = Marshal.ReadInt64 (data);
				CallAddress = Marshal.ReadInt64 (data, 8);
				StackPointer = Marshal.ReadInt64 (data, 16);

				int flags = Marshal.ReadInt32 (data, 24);
				IsRuntimeInvokeFrame = (flags & 1) == 1;
				IsExactMatch = (flags & 2) == 2;

				Registers = new long [count_regs];
				for (int i = 0; i < count_regs; i++)
					Registers [i] = Marshal.ReadInt64 (data, 32 + 8 * i);
			}

			public override string ToString ()
			{
				return String.Format ("Inferior.CallbackFrame ({0}:{1:x}:{2:x}:{3})", ID,
						      CallAddress, StackPointer, IsRuntimeInvokeFrame);
			}
		}

		public abstract TargetError RestartNotification (InferiorHandle inferior);

		internal abstract class MonoRuntimeHandle
		{ }

		public abstract MonoRuntimeHandle InitializeMonoRuntime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size);

		public abstract void SetRuntimeInfo (InferiorHandle inferior, MonoRuntimeHandle runtime);

		public abstract void InitializeCodeBuffer (MonoRuntimeHandle runtime, long executable_code_buffer,
							   int executable_code_buffer_size);

		public abstract void FinalizeMonoRuntime (MonoRuntimeHandle runtime);

		//
		// IDisposable
		//

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("DebuggerServer");
		}

		private bool disposed = false;

		protected virtual void DoDispose ()
		{ }

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				this.disposed = true;

				// Release unmanaged resources
				lock (this) {
					DoDispose ();
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DebuggerServer ()
		{
			Dispose (false);
		}
	}
}

