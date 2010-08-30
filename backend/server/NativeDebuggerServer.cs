#if FIXME
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
	internal class NativeDebuggerServer : DebuggerServer
	{
		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_initialize_process (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_initialize_thread (IntPtr handle, int child_pid, bool wait);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_io_thread_main (IntPtr io_data, ChildOutputHandler output_handler);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_spawn (IntPtr handle, string working_directory, string[] argv, string[] envp, bool redirect_fds, out int child_pid, out IntPtr io_data, out IntPtr error);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_attach (IntPtr handle, int child_pid);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_frame (IntPtr handle, out ServerStackFrame frame);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_current_insn_is_bpt (IntPtr handle, out int is_breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_step (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_continue (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_resume (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_detach (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_finalize (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_read_memory (IntPtr handle, long start, int size, IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_write_memory (IntPtr handle, long start, int size, IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_target_info (out int target_int_size, out int target_long_size, out int target_address_size, out int is_bigendian);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method (IntPtr handle, long method_address, long method_argument1, long method_argument2, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_1 (IntPtr handle, long method_address, long method_argument, long data_argument, long data_argument2, string string_argument, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_2 (IntPtr handle, long method_address, int data_size, IntPtr data, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_3 (IntPtr handle, long method_address, long method_argument, long address_argument, int blob_size, IntPtr blob_data, long callback_argument);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_mark_rti_frame (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_abort_invoke (IntPtr handle, long rti_id);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_call_method_invoke (IntPtr handle, long invoke_method, long method_address, int num_params, int blob_size, IntPtr param_data, IntPtr offset_data, IntPtr blob_data, long callback_argument, bool debug);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_execute_instruction (IntPtr handle, IntPtr instruction, int insn_size, bool update_ip);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_insert_breakpoint (IntPtr handle, long address, out int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_insert_hw_breakpoint (IntPtr handle, HardwareBreakpointType type, out int index, long address, out int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_remove_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_enable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_disable_breakpoint (IntPtr handle, int breakpoint);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_count_registers (IntPtr handle, out int count);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_registers (IntPtr handle, IntPtr values);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_set_registers (IntPtr handle, IntPtr values);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_stop (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_stop_and_wait (IntPtr handle, out int status);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_set_signal (IntPtr handle, int signal, int send_it);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_pending_signal (IntPtr handle, out int signal);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_kill (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_create_inferior (IntPtr breakpoint_manager);

		[DllImport("monodebuggerserver")]
		static extern ChildEventType mono_debugger_server_dispatch_event (IntPtr handle, int status, out long arg, out long data1, out long data2, out int opt_data_size, out IntPtr opt_data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_signal_info (IntPtr handle, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_threads (IntPtr handle, out int count, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_application (IntPtr handle, out IntPtr exe_file, out IntPtr cwd, out int nargs, out IntPtr data);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_detach_after_fork (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_push_registers (IntPtr handle, out long new_rsp);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_pop_registers (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_callback_frame (IntPtr handle, long stack_pointer, bool exact_match, IntPtr info);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_restart_notification (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_set_runtime_info (IntPtr handle, IntPtr mono_runtime_info);

		[DllImport("monodebuggerserver")]
		static extern ServerType mono_debugger_server_get_server_type ();

		[DllImport("monodebuggerserver")]
		static extern ServerCapabilities mono_debugger_server_get_capabilities ();

		[DllImport("monodebuggerserver")]
		static extern ArchTypeEnum mono_debugger_server_get_arch_type ();

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_server_initialize_mono_runtime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_initialize_code_buffer (
			IntPtr runtime, long executable_code_buffer,
			int executable_code_buffer_size);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_finalize_mono_runtime (IntPtr handle);

		[DllImport("libglib-2.0-0.dll")]
		protected extern static void g_free (IntPtr data);

		protected class NativeInferior : InferiorHandle
		{
			public BreakpointManager BreakpointManager;
			public IntPtr Handle;

			public NativeInferior (BreakpointManager bpm, IntPtr handle)
			{
				this.BreakpointManager = bpm;
				this.Handle = handle;
			}
		}

		NativeThreadManager manager;
		ServerCapabilities capabilities;
		ServerType server_type;
		ArchTypeEnum arch_type;

		public NativeDebuggerServer (Debugger debugger)
		{
			manager = new NativeThreadManager (debugger, this);
			server_type = mono_debugger_server_get_server_type ();
			capabilities = mono_debugger_server_get_capabilities ();
			arch_type = mono_debugger_server_get_arch_type ();
		}

		public override ThreadManager ThreadManager {
			get { return manager; }
		}

		public override ServerType Type {
			get { return server_type; }
		}

		public override ServerCapabilities Capabilities {
			get { return capabilities; }
		}

		public override BreakpointManager CreateBreakpointManager ()
		{
			return new NativeBreakpointManager ();
		}

		public override ArchTypeEnum ArchType {
			get { return arch_type; }
		}

		public override InferiorHandle CreateInferior (SingleSteppingEngine sse, Inferior inferior, BreakpointManager bpm)
		{
			var handle = mono_debugger_server_create_inferior (((NativeBreakpointManager) bpm).Manager);
			if (handle == IntPtr.Zero)
				throw new InternalError ("mono_debugger_server_initialize() failed.");

			return new NativeInferior (bpm, handle);
		}

		public override void InitializeProcess (InferiorHandle inferior)
		{
			check_error (mono_debugger_server_initialize_process (((NativeInferior) inferior).Handle));
		}

		public override TargetError InitializeThread (InferiorHandle inferior, int child_pid, bool wait)
		{
			return mono_debugger_server_initialize_thread (((NativeInferior) inferior).Handle, child_pid, wait);
		}

		public override ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
								      string filename, TargetAddress base_address, bool is_loaded)
		{
			return new Bfd (os, memory, filename, base_address, is_loaded);
		}

		protected static void check_error (TargetError error)
		{
			if (error == TargetError.None)
				return;

			throw new TargetException (error);
		}

		public override int Spawn (InferiorHandle inferior, string working_dir, string[] argv, string[] envp,
					   bool redirect_fds, ChildOutputHandler output_handler)
		{
			IntPtr error_ptr, io_data;

			check_disposed ();

			int child_pid;
			TargetError result = mono_debugger_server_spawn (
				((NativeInferior) inferior).Handle, working_dir, argv, envp, redirect_fds,
				out child_pid, out io_data, out error_ptr);

			if (result != TargetError.None) {
				string error = Marshal.PtrToStringAuto (error_ptr);
				g_free (error_ptr);
				throw new TargetException (result, error);
			}

			if (redirect_fds) {
				ST.Thread io_thread = new ST.Thread (delegate () {
					mono_debugger_server_io_thread_main (io_data, output_handler);
				});
				io_thread.IsBackground = true;
				io_thread.Start ();
			}

			return child_pid;
		}

		public override TargetError Attach (InferiorHandle inferior, int child_pid)
		{
			check_disposed ();
			return mono_debugger_server_attach (((NativeInferior) inferior).Handle, child_pid);
		}

		public override ServerStackFrame GetFrame (InferiorHandle inferior)
		{
			check_disposed ();
			ServerStackFrame frame;
			check_error (mono_debugger_server_get_frame (((NativeInferior) inferior).Handle, out frame));
			return frame;
		}

		public override TargetError CurrentInsnIsBpt (InferiorHandle inferior, out int is_breakpoint)
		{
			check_disposed ();
			return mono_debugger_server_current_insn_is_bpt (((NativeInferior) inferior).Handle, out is_breakpoint);
		}

		public override void Step (InferiorHandle inferior)
		{
			check_disposed ();
			check_error (mono_debugger_server_step (((NativeInferior) inferior).Handle));
		}

		public override void Continue (InferiorHandle inferior)
		{
			check_disposed ();
			check_error (mono_debugger_server_continue (((NativeInferior) inferior).Handle));
		}

		public override void Resume (InferiorHandle inferior)
		{
			check_disposed ();
			check_error (mono_debugger_server_resume (((NativeInferior) inferior).Handle));
		}

		public override TargetError Detach (InferiorHandle inferior)
		{
			check_disposed ();
			return mono_debugger_server_detach (((NativeInferior) inferior).Handle);
		}

		public override TargetError Finalize (InferiorHandle inferior)
		{
			check_disposed ();
			var handle = (NativeInferior) inferior;
			var result = mono_debugger_server_finalize (handle.Handle);
			handle.Handle = IntPtr.Zero;
			return result;
		}

		public override byte[] ReadMemory (InferiorHandle inferior, long address, int size)
		{
			check_disposed ();
			if (size == 0)
				return new byte [0];
			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (size);
				check_error (mono_debugger_server_read_memory (
					((NativeInferior) inferior).Handle, address, size, data));
				var buffer = new byte [size];
				Marshal.Copy (data, buffer, 0, size);
				return buffer;
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public override void WriteMemory (InferiorHandle inferior, long address, byte[] buffer)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				int size = buffer.Length;
				data = Marshal.AllocHGlobal (size);
				Marshal.Copy (buffer, 0, data, size);
				check_error (mono_debugger_server_write_memory (
					((NativeInferior) inferior).Handle, address, size, data));
			} finally {
				if (data != IntPtr.Zero)
					Marshal.FreeHGlobal (data);
			}
		}

		public override TargetInfo GetTargetInfo ()
		{
			check_disposed ();
			int target_int_size, target_long_size, target_addr_size, is_bigendian;
			check_error (mono_debugger_server_get_target_info (
				out target_int_size, out target_long_size,
				out target_addr_size, out is_bigendian));

			return new TargetInfo (target_int_size, target_long_size,
					       target_addr_size, is_bigendian != 0);
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address,
							long arg1, long arg2, long callback_arg)
		{
			check_disposed ();
			return mono_debugger_server_call_method (
				((NativeInferior) inferior).Handle, method_address, arg1, arg2, callback_arg);
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address,
							long arg1, long arg2, long arg3, string string_arg,
							long callback_arg)
		{
			check_disposed ();
			return mono_debugger_server_call_method_1 (
				((NativeInferior) inferior).Handle, method_address, arg1, arg2, arg3, string_arg, callback_arg);
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address,
							byte[] data, long callback_arg)
		{
			check_disposed ();

			IntPtr data_ptr = IntPtr.Zero;
			int data_size = data != null ? data.Length : 0;

			try {
				if (data != null) {
					data_ptr = Marshal.AllocHGlobal (data_size);
					Marshal.Copy (data, 0, data_ptr, data_size);
				}

				return mono_debugger_server_call_method_2 (
					((NativeInferior) inferior).Handle, method_address,
					data_size, data_ptr, callback_arg);
			} finally {
				if (data_ptr != IntPtr.Zero)
					Marshal.FreeHGlobal (data_ptr);
			}
		}

		public override TargetError CallMethod (InferiorHandle inferior, long method_address,
							long arg1, long arg2, byte[] blob, long callback_arg)
		{
			check_disposed ();

			IntPtr blob_data = IntPtr.Zero;
			try {
				if (blob != null) {
					blob_data = Marshal.AllocHGlobal (blob.Length);
					Marshal.Copy (blob, 0, blob_data, blob.Length);
				}

				return mono_debugger_server_call_method_3 (
					((NativeInferior) inferior).Handle, method_address, arg1,
					arg2, blob != null ? blob.Length : 0, blob_data, callback_arg);
			} finally {
				if (blob_data != IntPtr.Zero)
					Marshal.FreeHGlobal (blob_data);
			}
		}

		public override TargetError RuntimeInvoke (InferiorHandle inferior, long invoke_method, long method_argument,
							   int num_params, byte[] blob, int[] blob_offsets, long[] addresses,
							   long callback_arg, bool debug)
		{
			check_disposed ();

			IntPtr blob_data = IntPtr.Zero, param_data = IntPtr.Zero;
			IntPtr offset_data = IntPtr.Zero;
			try {
				int blob_size = blob != null ? blob.Length : 0;
				if (blob_size > 0) {
					blob_data = Marshal.AllocHGlobal (blob_size);
					Marshal.Copy (blob, 0, blob_data, blob_size);
				}

				param_data = Marshal.AllocHGlobal (num_params * 8);
				Marshal.Copy (addresses, 0, param_data, num_params);

				offset_data = Marshal.AllocHGlobal (num_params * 4);
				Marshal.Copy (blob_offsets, 0, offset_data, num_params);

				return mono_debugger_server_call_method_invoke (
					((NativeInferior) inferior).Handle, invoke_method, method_argument,
					num_params, blob_size, param_data, offset_data, blob_data,
					callback_arg, debug);
			} finally {
				if (blob_data != IntPtr.Zero)
					Marshal.FreeHGlobal (blob_data);
				Marshal.FreeHGlobal (param_data);
				Marshal.FreeHGlobal (offset_data);
			}
		}

		public override TargetError ExecuteInstruction (InferiorHandle inferior, byte[] instruction, bool update_ip)
		{
			check_disposed ();

			IntPtr data = IntPtr.Zero;
			try {
				data = Marshal.AllocHGlobal (instruction.Length);
				Marshal.Copy (instruction, 0, data, instruction.Length);

				return mono_debugger_server_execute_instruction (
					((NativeInferior) inferior).Handle, data, instruction.Length, update_ip);
			} finally {
				Marshal.FreeHGlobal (data);
			}
		}

		public override TargetError MarkRuntimeInvokeFrame (InferiorHandle inferior)
		{
			check_disposed ();
			return mono_debugger_server_mark_rti_frame (((NativeInferior) inferior).Handle);
		}

		public override TargetError AbortInvoke (InferiorHandle inferior, long rti_id)
		{
			check_disposed ();
			return mono_debugger_server_abort_invoke (((NativeInferior) inferior).Handle, rti_id);
		}

		public override int InsertBreakpoint (InferiorHandle inferior, long address)
		{
			check_disposed ();
			int breakpoint;
			check_error (mono_debugger_server_insert_breakpoint (
				((NativeInferior) inferior).Handle, address, out breakpoint));
			return breakpoint;
		}

		public override TargetError InsertHardwareBreakpoint (InferiorHandle inferior, HardwareBreakpointType type,
								      out int index, long address, out int breakpoint)
		{
			check_disposed ();
			return mono_debugger_server_insert_hw_breakpoint (
				((NativeInferior) inferior).Handle, type, out index, address, out breakpoint);
		}

		public override void RemoveBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			check_disposed ();
			check_error (mono_debugger_server_remove_breakpoint (((NativeInferior) inferior).Handle, breakpoint));
		}

		public override void EnableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			check_disposed ();
			check_error (mono_debugger_server_enable_breakpoint (((NativeInferior) inferior).Handle, breakpoint));
		}

		public override void DisableBreakpoint (InferiorHandle inferior, int breakpoint)
		{
			check_disposed ();
			check_error (mono_debugger_server_disable_breakpoint (((NativeInferior) inferior).Handle, breakpoint));
		}

		public override long[] GetRegisters (InferiorHandle inferior)
		{
			check_disposed ();

			int count;
			check_error (mono_debugger_server_count_registers (((NativeInferior) inferior).Handle, out count));

			IntPtr buffer = IntPtr.Zero;
			try {
				int buffer_size = count * 8;
				buffer = Marshal.AllocHGlobal (buffer_size);
				check_error (mono_debugger_server_get_registers (((NativeInferior) inferior).Handle, buffer));
				var registers = new long [count];
				Marshal.Copy (buffer, registers, 0, count);
				return registers;
			} finally {
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		public override TargetError SetRegisters (InferiorHandle inferior, long[] registers)
		{
			check_disposed ();

			int count;
			var result = mono_debugger_server_count_registers (((NativeInferior) inferior).Handle, out count);
			if (result != TargetError.None)
				return result;

			if (count != registers.Length)
				throw new ArgumentException ();

			IntPtr buffer = IntPtr.Zero;
			try {
				int buffer_size = count * 8;
				buffer = Marshal.AllocHGlobal (buffer_size);
				Marshal.Copy (registers, 0, buffer, count);
				return mono_debugger_server_set_registers (((NativeInferior) inferior).Handle, buffer);
			} finally {
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		public override TargetError Stop (InferiorHandle inferior)
		{
			check_disposed ();
			return mono_debugger_server_stop (((NativeInferior) inferior).Handle);
		}

		public override TargetError StopAndWait (InferiorHandle inferior, out int status)
		{
			check_disposed ();
			return mono_debugger_server_stop_and_wait (((NativeInferior) inferior).Handle, out status);
		}

		public override void SetSignal (InferiorHandle inferior, int signal, bool send_it)
		{
			check_disposed ();
			check_error (mono_debugger_server_set_signal (((NativeInferior) inferior).Handle, signal, send_it ? 1 : 0));
		}

		public override int GetPendingSignal (InferiorHandle inferior)
		{
			int signal;
			check_disposed ();
			check_error (mono_debugger_server_get_pending_signal (((NativeInferior) inferior).Handle, out signal));
			return signal;
		}

		public override TargetError Kill (InferiorHandle inferior)
		{
			check_disposed ();
			return mono_debugger_server_kill (((NativeInferior) inferior).Handle);
		}

		public override ChildEventType DispatchEvent (InferiorHandle inferior, int status, out long arg,
							      out long data1, out long data2, out byte[] opt_data)
		{
			ChildEventType message;

			int opt_data_size;
			IntPtr opt_data_ptr;

			message = mono_debugger_server_dispatch_event (
				((NativeInferior) inferior).Handle, status, out arg, out data1, out data2,
				out opt_data_size, out opt_data_ptr);

			if (opt_data_size > 0) {
				opt_data = new byte [opt_data_size];
				Marshal.Copy (opt_data_ptr, opt_data, 0, opt_data_size);
				g_free (opt_data_ptr);
			} else {
				opt_data = null;
			}

			return message;
		}

		public override SignalInfo GetSignalInfo (InferiorHandle inferior)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				check_error (mono_debugger_server_get_signal_info (((NativeInferior) inferior).Handle, out data));
				return (SignalInfo) Marshal.PtrToStructure (data, typeof (SignalInfo));
			} finally {
				g_free (data);
			}
		}

		public override TargetError GetThreads (InferiorHandle inferior, out int[] threads)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			try {
				int count;
				var result = mono_debugger_server_get_threads (((NativeInferior) inferior).Handle, out count, out data);

				if (result == TargetError.None) {
					threads = new int [count];
					Marshal.Copy (data, threads, 0, count);
				} else {
					threads = null;
				}

				return result;
			} finally {
				g_free (data);
			}
		}

		public override string GetApplication (InferiorHandle inferior,
						       out string cwd, out string[] cmdline_args)
		{
			check_disposed ();
			IntPtr data = IntPtr.Zero;
			IntPtr p_exe = IntPtr.Zero;
			IntPtr p_cwd = IntPtr.Zero;
			try {
				int count;
				string exe_file;
				check_error (mono_debugger_server_get_application (
					((NativeInferior) inferior).Handle, out p_exe, out p_cwd, out count, out data));

				cmdline_args = new string [count];
				exe_file = Marshal.PtrToStringAnsi (p_exe);
				cwd = Marshal.PtrToStringAnsi (p_cwd);

				for (int i = 0; i < count; i++) {
					IntPtr ptr = Marshal.ReadIntPtr (data, i * IntPtr.Size);
					cmdline_args [i] = Marshal.PtrToStringAnsi (ptr);
				}

				return exe_file;
			} finally {
				g_free (data);
				g_free (p_exe);
				g_free (p_cwd);
			}
		}

		public override TargetError DetachAfterFork (InferiorHandle inferior)
		{
			check_disposed ();
			return mono_debugger_server_detach_after_fork (((NativeInferior) inferior).Handle);
		}

		public override TargetError PushRegisters (InferiorHandle inferior, out long new_rsp)
		{
			check_disposed ();
			return mono_debugger_server_push_registers (((NativeInferior) inferior).Handle, out new_rsp);
		}

		public override TargetError PopRegisters (InferiorHandle inferior)
		{
			check_disposed ();
			return mono_debugger_server_pop_registers (((NativeInferior) inferior).Handle);
		}

		public override TargetError GetCallbackFrame (InferiorHandle inferior, long stack_pointer,
							      bool exact_match, out CallbackFrame info)
		{
			info = null;
			check_disposed ();

			int count;
			var result = mono_debugger_server_count_registers (((NativeInferior) inferior).Handle, out count);
			if (result != TargetError.None)
				return result;

			IntPtr buffer = IntPtr.Zero;
			try {
				int buffer_size = 32 + count * 8;
				buffer = Marshal.AllocHGlobal (buffer_size);
				result = mono_debugger_server_get_callback_frame (
					((NativeInferior) inferior).Handle, stack_pointer, exact_match, buffer);

				if (result == TargetError.None)
					info = new CallbackFrame (buffer, count);

				return result;
			} finally {
				if (buffer != IntPtr.Zero)
					Marshal.FreeHGlobal (buffer);
			}
		}

		public override TargetError RestartNotification (InferiorHandle inferior)
		{
			return mono_debugger_server_restart_notification (((NativeInferior) inferior).Handle);
		}

		internal class NativeMonoRuntime : MonoRuntimeHandle
		{
			public IntPtr Handle;

			public NativeMonoRuntime (IntPtr handle)
			{
				this.Handle = handle;
			}
		}

		public override MonoRuntimeHandle InitializeMonoRuntime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size)
		{
			IntPtr handle = mono_debugger_server_initialize_mono_runtime (
				address_size, notification_address,
				executable_code_buffer, executable_code_buffer_size,
				breakpoint_info, breakpoint_info_index,
				breakpoint_table_size);

			return new NativeMonoRuntime (handle);
		}

		public override void SetRuntimeInfo (InferiorHandle inferior, MonoRuntimeHandle runtime)
		{
			mono_debugger_server_set_runtime_info (((NativeInferior) inferior).Handle,
							       ((NativeMonoRuntime) runtime).Handle);
		}

		public override void InitializeCodeBuffer (MonoRuntimeHandle runtime, long executable_code_buffer,
							   int executable_code_buffer_size)
		{
			mono_debugger_server_initialize_code_buffer (((NativeMonoRuntime) runtime).Handle,
								     executable_code_buffer, executable_code_buffer_size);
		}

		public override void FinalizeMonoRuntime (MonoRuntimeHandle runtime)
		{
			var handle = (NativeMonoRuntime) runtime;
			mono_debugger_server_finalize_mono_runtime (handle.Handle);
			handle.Handle = IntPtr.Zero;
		}

		internal override void InitializeAtEntryPoint (Inferior inferior)
		{ }

		protected override void DoDispose ()
		{
		}
	}
}
#endif
