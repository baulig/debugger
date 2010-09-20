using System;
using System.IO;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Server;

namespace Mono.Debugger.Backend
{
	internal class Inferior : TargetAccess, ITargetNotification, IDisposable
	{
		protected ThreadManager thread_manager;

		protected readonly ProcessStart start;

		protected readonly Process process;
		protected readonly DebuggerErrorHandler error_handler;
		protected readonly BreakpointManager breakpoint_manager;
		protected readonly AddressDomain address_domain;
		protected readonly bool native;

		protected readonly DebuggerServer server;
		protected readonly SingleSteppingEngine sse;

		IInferior server_inferior;
		IProcess server_process;

		int child_pid;
		bool initialized;
		bool has_target;
		bool pushed_regs;

		TargetMemoryInfo target_info;
		Architecture arch;

		bool has_signals;
		SignalInfo signal_info;

		public static bool IsRunningOnWindows {
			get {
				return ((int)Environment.OSVersion.Platform < 4);
			}
		}

		public bool HasTarget {
			get {
				return has_target;
			}
		}

		public int PID {
			get {
				check_disposed ();
				return child_pid;
			}
		}

		public DebuggerServer DebuggerServer {
			get {
				return server;
			}
		}

		internal IInferior InferiorHandle {
			get {
				check_disposed ();
				return server_inferior;
			}
		}

		internal IProcess ProcessHandle {
			get {
				check_disposed ();
				return server_process;
			}
		}

		protected Inferior (ThreadManager thread_manager, Process process,
				    SingleSteppingEngine sse, ProcessStart start,
				    BreakpointManager bpm, DebuggerErrorHandler error_handler,
				    AddressDomain address_domain)
		{
			this.thread_manager = thread_manager;
			this.process = process;
			this.start = start;
			this.native = start.IsNative;
			this.error_handler = error_handler;
			this.breakpoint_manager = bpm;
			this.address_domain = address_domain;

			server = thread_manager.DebuggerServer;
		}

		protected Inferior (SingleSteppingEngine sse, IProcess server_process, IInferior server_inferior)
		{
			this.sse = sse;
			this.server_process = server_process;
			this.server_inferior = server_inferior;
			this.thread_manager = sse.ThreadManager;
			this.process = sse.Process;
			this.start = sse.Process.ProcessStart;
			this.native = start.IsNative;
			this.breakpoint_manager = sse.Process.BreakpointManager;
			this.address_domain = sse.ThreadManager.AddressDomain;
			this.server = sse.ThreadManager.DebuggerServer;
		}

		public static Inferior CreateInferior (SingleSteppingEngine sse, IProcess server_process,
						       IInferior server_inferior)
		{
			var inferior = new Inferior (sse, server_process, server_inferior);
			inferior.SetupInferior ();
			return inferior;
		}

		public void CallMethod (TargetAddress method, long data1, long data2,
					long callback_arg)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.Busy);
			try {
				var data = new InvocationData (method.Address, callback_arg, data1, data2);
				server_inferior.CallMethod (data);
			} catch {
				change_target_state (old_state);
				throw;
			}
		}

		public void CallMethod (TargetAddress method, long arg1, long arg2, long arg3,
					string arg4, long callback_arg)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.Running);
			try {
				var data = new InvocationData (method.Address, callback_arg, arg1, arg2, arg3, arg4);
				server_inferior.CallMethod (data);
			} catch {
				change_target_state (old_state);
				throw;
			}
		}

		public void CallMethodWithContext (TargetAddress method, long callback_arg)
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.Running);

			try {
				var data = new InvocationData (method.Address, callback_arg);
				server_inferior.CallMethod (data);
			} catch {
				change_target_state (old_state);
				throw;
			}
		}

		public void CallMethod (TargetAddress method, long method_argument,
					TargetObject obj, long callback_arg)
		{
			check_disposed ();

			byte[] blob = null;
			long address = 0;

			if (obj.Location.HasAddress)
				address = obj.Location.GetAddress (this).Address;
			else
				blob = obj.Location.ReadBuffer (this, obj.Type.Size);

			var data = new InvocationData (method.Address, callback_arg, address, blob);
			server_inferior.CallMethod (data);
		}

		public void RuntimeInvoke (TargetAddress invoke_method,
					   TargetAddress method_argument,
					   TargetObject object_argument,
					   TargetObject[] param_objects,
					   long callback_arg, bool debug)
		{
			check_disposed ();

			int length = param_objects.Length + 1;

			TargetObject[] input_objects = new TargetObject [length];
			input_objects [0] = object_argument;
			param_objects.CopyTo (input_objects, 1);

			int blob_size = 0;
			byte[][] blobs = new byte [length][];
			int[] blob_offsets = new int [length];
			long[] addresses = new long [length];

			for (int i = 0; i < length; i++) {
				TargetObject obj = input_objects [i];

				if (obj == null)
					continue;
				if (obj.Location.HasAddress) {
					blob_offsets [i] = -1;
					addresses [i] = obj.Location.GetAddress (this).Address;
					continue;
				}
				blobs [i] = obj.Location.ReadBuffer (this, obj.Type.Size);
				blob_offsets [i] = blob_size;
				blob_size += blobs [i].Length;
			}

			byte[] blob = new byte [blob_size];
			blob_size = 0;
			for (int i = 0; i < length; i++) {
				if (blobs [i] == null)
					continue;
				blobs [i].CopyTo (blob, blob_size);
				blob_size += blobs [i].Length;
			}

			server_inferior.RuntimeInvoke (
				invoke_method.Address, method_argument.Address,
				length, blob, blob_offsets, addresses, callback_arg, debug);
		}

		public void MarkRuntimeInvokeFrame ()
		{
			server_inferior.MarkRuntimeInvokeFrame ();
		}

		public void AbortInvoke (long rti_id)
		{
			server_inferior.AbortInvoke (rti_id);
		}

		public int InsertBreakpoint (TargetAddress address)
		{
			return server_inferior.InsertBreakpoint (address.Address);
		}

		public int InsertHardwareBreakpoint (TargetAddress address, bool fallback, out int hw_index)
		{
			return server_inferior.InsertHardwareBreakpoint (
				HardwareBreakpointType.None, fallback,
				address.Address, out hw_index);
		}

		public void RemoveBreakpoint (int breakpoint)
		{
			server_inferior.RemoveBreakpoint (breakpoint);
		}

		public int InsertHardwareWatchPoint (TargetAddress address, HardwareBreakpointType type,
						     out int hw_index)
		{
			return server_inferior.InsertHardwareBreakpoint (type, false, address.Address, out hw_index);
		}

		public void EnableBreakpoint (int breakpoint)
		{
			server_inferior.EnableBreakpoint (breakpoint);
		}

		public void DisableBreakpoint (int breakpoint)
		{
			server_inferior.DisableBreakpoint (breakpoint);
		}

		public void RestartNotification ()
		{
#if FIXME
			inferior.RestartNotification (inferior);
#endif
		}

		public ProcessStart ProcessStart {
			get {
				return start;
			}
		}

		public Process Process {
			get {
				return process;
			}
		}

		public void InitializeAfterExec (int pid)
		{
			if (initialized)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			initialized = true;

			server_inferior.InitializeThread (pid, false);
			this.child_pid = pid;

			string exe_file, cwd;
			string[] cmdline_args;
			exe_file = GetApplication (out cwd, out cmdline_args);

			start.SetupApplication (exe_file, cwd, cmdline_args);

			SetupInferior ();

			change_target_state (TargetState.Stopped, 0);
		}

#if FIXME
		protected void Attach ()
		{
			if (has_target || initialized)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			has_target = true;

			server_inferior = server_process.Attach (start.PID);
			this.child_pid = start.PID;

			string exe_file, cwd;
			string[] cmdline_args;
			exe_file = GetApplication (out cwd, out cmdline_args);

			start.SetupApplication (exe_file, cwd, cmdline_args);

			initialized = true;

			SetupInferior ();

			change_target_state (TargetState.Stopped, 0);
		}
#endif

		public void InitializeProcess ()
		{
			server_process.InitializeProcess (server_inferior);
		}

#if FIXME
		public ServerEvent ProcessEvent (int status)
		{
			long arg, data1, data2;
			ServerEventType message;

			int opt_data_size;
			byte[] opt_data;

			message = inferior.DispatchEvent (
				status, out arg, out data1, out data2, out opt_data);

			switch (message) {
			case ServerEventType.Exited:
			case ServerEventType.CHILD_SIGNALED:
				change_target_state (TargetState.Exited);
				break;

			case ServerEventType.Callback:
			case ServerEventType.Callback_COMPLETED:
			case ServerEventType.RUNTIME_INVOKE_DONE:
			case ServerEventType.Stopped:
			case ServerEventType.Interrupted:
			case ServerEventType.Breakpoint:
			case ServerEventType.Notification:
				change_target_state (TargetState.Stopped);
				break;

			case ServerEventType.CHILD_EXECD:
				break;
			}

			if (opt_data != null)
				return new ServerEvent (message, arg, data1, data2, opt_data);

			return new ServerEvent (message, arg, data1, data2);
		}
#endif

		public static string GetFileContents (string filename)
		{
			try {
				StreamReader sr = File.OpenText (filename);
				string contents = sr.ReadToEnd ();

				sr.Close();

				return contents;
			}
			catch {
				return null;
			}
		}

		protected void SetupInferior ()
		{
			if (has_target)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			if ((server.Capabilities & ServerCapabilities.HasSignals) != 0) {
				signal_info = server_inferior.GetSignalInfo ();
				has_signals = true;
			}

			target_info = thread_manager.GetTargetMemoryInfo (address_domain);

			arch = process.Architecture;

			if (process.IsAttached) {
				string exe_file, cwd;
				string[] cmdline_args;
				exe_file = GetApplication (out cwd, out cmdline_args);

				start.SetupApplication (exe_file, cwd, cmdline_args);
			}

			has_target = true;
			initialized = true;

			change_target_state (TargetState.Stopped, 0);
		}

		public BreakpointManager BreakpointManager {
			get { return breakpoint_manager; }
		}

		public override int TargetIntegerSize {
			get {
				return target_info.TargetIntegerSize;
			}
		}

		public override int TargetLongIntegerSize {
			get {
				return target_info.TargetLongIntegerSize;
			}
		}

		public override int TargetAddressSize {
			get {
				return target_info.TargetAddressSize;
			}
		}

		public override bool IsBigEndian {
			get {
				return target_info.IsBigEndian;
			}
		}

		public override AddressDomain AddressDomain {
			get {
				return address_domain;
			}
		}

		public override TargetMemoryInfo TargetMemoryInfo {
			get {
				return target_info;
			}
		}

		byte[] read_buffer (TargetAddress address, int size)
		{
			return server_inferior.ReadMemory (address.Address, size);
		}

		public override byte[] ReadBuffer (TargetAddress address, int size)
		{
			check_disposed ();
			if (size == 0)
				return new byte [0];
			return read_buffer (address, size);
		}

		public override byte ReadByte (TargetAddress address)
		{
			check_disposed ();
			var buffer = read_buffer (address, 1);
			return buffer [0];
		}

		public override int ReadInteger (TargetAddress address)
		{
			check_disposed ();
			var buffer = read_buffer (address, 4);
			return BitConverter.ToInt32 (buffer, 0);
		}

		public override long ReadLongInteger (TargetAddress address)
		{
			check_disposed ();
			var buffer = read_buffer (address, 8);
			return BitConverter.ToInt64 (buffer, 0);
		}

		static TargetAddress create_address (TargetMemoryInfo info, long address)
		{
			switch (info.TargetAddressSize) {
			case 4:
				address &= 0x00000000ffffffffL;
				return new TargetAddress (info.AddressDomain, address);

			case 8:
				return new TargetAddress (info.AddressDomain, address);

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + info.TargetAddressSize);
			}
		}

		TargetAddress create_address (long address)
		{
			return create_address (target_info, address);
		}

		public override TargetAddress ReadAddress (TargetAddress address)
		{
			check_disposed ();
			TargetAddress res;
			switch (TargetAddressSize) {
			case 4:
				res = new TargetAddress (AddressDomain, (uint) ReadInteger (address));
				break;

			case 8:
				res = new TargetAddress (AddressDomain, ReadLongInteger (address));
				break;

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
			}

			if (res.Address == 0)
				return TargetAddress.Null;
			else
				return res;
		}

		public override string ReadString (TargetAddress address)
		{
			check_disposed ();
			StringBuilder sb = new StringBuilder ();

			while (true) {
				byte b = ReadByte (address);
				address++;

				if (b == 0)
					return sb.ToString ();

				sb.Append ((char) b);
			}
		}

		public override TargetBlob ReadMemory (TargetAddress address, int size)
		{
			check_disposed ();
			byte [] retval = ReadBuffer (address, size);
			return new TargetBlob (retval, target_info);
		}

		public override bool CanWrite {
			get {
				return true;
			}
		}

		public override void WriteBuffer (TargetAddress address, byte[] buffer)
		{
			check_disposed ();
			server_inferior.WriteMemory (address.Address, buffer);
			OnMemoryChanged ();
		}

		public override void WriteByte (TargetAddress address, byte value)
		{
			check_disposed ();
			var buffer = BitConverter.GetBytes (value);
			server_inferior.WriteMemory (address.Address, buffer);
			OnMemoryChanged ();
		}

		public override void WriteInteger (TargetAddress address, int value)
		{
			check_disposed ();
			var buffer = BitConverter.GetBytes (value);
			server_inferior.WriteMemory (address.Address, buffer);
			OnMemoryChanged ();
		}

		public override void WriteLongInteger (TargetAddress address, long value)
		{
			check_disposed ();
			var buffer = BitConverter.GetBytes (value);
			server_inferior.WriteMemory (address.Address, buffer);
			OnMemoryChanged ();
		}

		public override void WriteAddress (TargetAddress address, TargetAddress value)
		{
			check_disposed ();
			switch (TargetAddressSize) {
			case 4:
				WriteInteger (address, (int) value.Address);
				break;

			case 8:
				WriteLongInteger (address, value.Address);
				break;

			default:
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
			}
		}

		internal override void InsertBreakpoint (BreakpointHandle handle,
							 TargetAddress address, int domain)
		{
			breakpoint_manager.InsertBreakpoint (this, handle, address, domain);
		}

		internal override void RemoveBreakpoint (BreakpointHandle handle)
		{
			breakpoint_manager.RemoveBreakpoint (this, handle);
		}

		//
		// IInferior
		//

		public event StateChangedHandler StateChanged;

		TargetState target_state = TargetState.NoTarget;
		public TargetState State {
			get {
				check_disposed ();
				return target_state;
			}
		}

		protected TargetState change_target_state (TargetState new_state)
		{
			return change_target_state (new_state, 0);
		}

		TargetState change_target_state (TargetState new_state, int arg)
		{
			if (new_state == target_state)
				return target_state;

			TargetState old_state = target_state;
			target_state = new_state;

			if (StateChanged != null)
				StateChanged (target_state, arg);

			return old_state;
		}

		public void Step ()
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.Running);
			try {
				server_inferior.Step ();
			} catch {
				change_target_state (old_state);
				throw;
			}
		}

		public void Continue ()
		{
			check_disposed ();
			TargetState old_state = change_target_state (TargetState.Running);
			try {
				server_inferior.Continue ();
			} catch {
				change_target_state (old_state);
				throw;
			}
		}

		public void ResumeStepping ()
		{
			check_disposed ();

			TargetState old_state = change_target_state (TargetState.Running);
			try {
				server_inferior.ResumeStepping ();
			} catch {
				change_target_state (old_state);
				throw;
			}
		}

		// <summary>
		//   Stop the inferior.
		//   Returns true if it actually stopped the inferior and false if it was
		//   already stopped.
		//   Note that the target may have stopped abnormally in the meantime, in
		//   this case we return the corresponding ChildEvent.
		// </summary>
		public bool Stop (out ServerEvent new_event)
		{
			check_disposed ();
			new_event = null;
			return Stop ();

#if FIXME
			check_disposed ();
			int status;
			TargetError error = inferior.StopAndWait (out status);
			if (error != TargetError.None) {
				new_event = null;
				return false;
			} else if (status == 0) {
				new_event = null;
				return true;
			}

			new_event = ProcessEvent (status);
			return true;
#else
			throw new NotImplementedException ();
#endif
		}

		// <summary>
		//   Just send the inferior a stop signal, but don't wait for it to stop.
		//   Returns true if it actually sent the signal and false if the target
		//   was already stopped.
		// </summary>
		public bool Stop ()
		{
			check_disposed ();
			bool stopped = server_inferior.Stop ();
			change_target_state (TargetState.Stopped);
			return stopped;
		}

		public void SetSignal (int signal, bool send_it)
		{
			check_disposed ();
			server_inferior.SetSignal (signal, send_it);
		}

		public int GetPendingSignal ()
		{
			check_disposed ();
			return server_inferior.GetPendingSignal ();
		}

		public void Detach ()
		{
			check_disposed ();
			if (pushed_regs)
				server_inferior.PopRegisters ();
			server_inferior.Detach ();
		}

		public void Shutdown ()
		{
			server_inferior.Kill ();
		}

		public void Kill ()
		{
			check_disposed ();
			server_inferior.Kill ();
		}

		public TargetAddress CurrentFrame {
			get {
				ServerStackFrame frame = get_current_frame ();
				return create_address (frame.Address);
			}
		}

		public bool CurrentInstructionIsBreakpoint {
			get {
				check_disposed ();
				return server_inferior.CurrentInsnIsBpt ();
			}
		}

		internal Architecture Architecture {
			get {
				check_disposed ();
				return arch;
			}
		}

		public override Registers GetRegisters ()
		{
			return new Registers (arch, server_inferior.GetRegisters ());
		}

		public override void SetRegisters (Registers registers)
		{
			int count = arch.CountRegisters;

			Registers old_regs = GetRegisters ();
			for (int i = 0; i < count; i++) {
				if (registers [i] == null)
					continue;
				if (!registers [i].Valid)
					registers [i].SetValue (old_regs [i].Value);
			}

			server_inferior.SetRegisters (registers.Values);
		}

		public int[] GetThreads ()
		{
#if FIXME
			int[] threads;
			inferior.GetThreads (out threads);
			return threads;
#else
			throw new NotImplementedException ();
#endif
		}

		protected string GetApplication (out string cwd, out string[] cmdline_args)
		{
			return server_inferior.GetApplication (out cwd, out cmdline_args);
		}

		public void DetachAfterFork ()
		{
			server_inferior.DetachAfterFork ();
			Dispose ();
		}

		public TargetAddress PushRegisters ()
		{
			long new_rsp = server_inferior.PushRegisters ();
			pushed_regs = true;
			return create_address (new_rsp);
		}

		public void PopRegisters ()
		{
			pushed_regs = false;
			server_inferior.PopRegisters ();
		}

		internal CallbackFrame GetCallbackFrame (TargetAddress stack_pointer, bool exact_match)
		{
			var cframe = server_inferior.GetCallbackFrame (stack_pointer.Address, exact_match);
			if (cframe == null)
				return null;

			return new CallbackFrame (this, cframe);
		}

		internal class CallbackFrame
		{
			public readonly long ID;
			public readonly TargetAddress CallAddress;
			public readonly TargetAddress StackPointer;
			public readonly bool IsRuntimeInvokeFrame;
			public readonly bool IsExactMatch;
			public readonly Registers Registers;

			public CallbackFrame (Inferior inferior, ServerCallbackFrame cframe)
			{
				ID = cframe.ID;
				CallAddress = inferior.create_address (cframe.CallAddress);
				StackPointer = inferior.create_address (cframe.StackPointer);

				IsRuntimeInvokeFrame = cframe.IsRuntimeInvokeFrame;
				IsExactMatch = cframe.IsExactMatch;

				Registers = new Registers (inferior.arch, cframe.Registers);
			}

			public override string ToString ()
			{
				return String.Format ("Inferior.CallbackFrame ({0}:{1:x}:{2:x}:{3})", ID,
						      CallAddress, StackPointer, IsRuntimeInvokeFrame);
			}
		}

		internal void SetRuntimeInfo (MonoRuntimeHandle runtime)
		{
			server_inferior.SetRuntimeInfo (runtime);
		}

		internal class StackFrame
		{
			TargetAddress address, stack, frame;

			internal StackFrame (TargetMemoryInfo info, ServerStackFrame frame)
			{
				this.address = Inferior.create_address (info, frame.Address);
				this.stack = Inferior.create_address (info, frame.StackPointer);
				this.frame = Inferior.create_address (info, frame.FrameAddress);
			}

			internal StackFrame (TargetAddress address, TargetAddress stack,
					     TargetAddress frame)
			{
				this.address = address;
				this.stack = stack;
				this.frame = frame;
			}

			public TargetAddress Address {
				get {
					return address;
				}
			}

			public TargetAddress StackPointer {
				get {
					return stack;
				}
			}

			public TargetAddress FrameAddress {
				get {
					return frame;
				}
			}
		}

		ServerStackFrame get_current_frame ()
		{
			check_disposed ();
			return server_inferior.GetFrame ();
		}

		public StackFrame GetCurrentFrame (bool may_fail)
		{
			check_disposed ();
			try {
				var frame = server_inferior.GetFrame ();
				return new StackFrame (target_info, frame);
			} catch {
				if (may_fail)
					return null;
				throw;
			}
		}

		public StackFrame GetCurrentFrame ()
		{
			return GetCurrentFrame (false);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			// We cannot use System.IO to read this file because it is not
			// seekable.  Actually, the file is seekable, but it contains
			// "holes" and each line starts on a new 4096 bytes block.
			// So if you just read the first line from the file, the current
			// file position will be rounded up to the next 4096 bytes
			// boundary - it'll be different from what System.IO thinks is
			// the current file position and System.IO will try to "fix" this
			// by seeking back.
			string mapfile = String.Format ("/proc/{0}/maps", child_pid);
			string contents = GetFileContents (mapfile);

			if (contents == null)
				return null;

			ArrayList list = new ArrayList ();

			using (StringReader reader = new StringReader (contents)) {
				do {
					string l = reader.ReadLine ();
					if (l == null)
						break;

					bool is64bit;
					if (l [8] == '-')
						is64bit = false;
					else if (l [16] == '-')
						is64bit = true;
					else
						throw new InternalError ();

					string sstart = is64bit ? l.Substring (0,16) : l.Substring (0,8);
					string send = is64bit ? l.Substring (17,16) : l.Substring (9,8);
					string sflags = is64bit ? l.Substring (34,4) : l.Substring (18,4);

					long start = Int64.Parse (sstart, NumberStyles.HexNumber);
					long end = Int64.Parse (send, NumberStyles.HexNumber);

					string name;
					if (is64bit)
						name = (l.Length > 73) ? l.Substring (73) : "";
					else
						name = (l.Length > 49) ? l.Substring (49) : "";
					name = name.TrimStart (' ').TrimEnd (' ');
					if (name == "")
						name = null;

					TargetMemoryFlags flags = 0;
					if (sflags [1] != 'w')
						flags |= TargetMemoryFlags.ReadOnly;

					TargetMemoryArea area = new TargetMemoryArea (
						create_address (start),
						create_address (end),
						flags, name);
					list.Add (area);
				} while (true);
			}

			TargetMemoryArea[] maps = new TargetMemoryArea [list.Count];
			list.CopyTo (maps, 0);
			return maps;
		}

		protected virtual void OnMemoryChanged ()
		{
			// child_event (ChildEventType.CHILD_MEMORY_CHANGED, 0);
		}

		public bool HasSignals {
			get { return has_signals; }
		}

		public int SIGKILL {
			get {
				if (!has_signals || (signal_info.SIGKILL < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGKILL;
			}
		}

		public int SIGSTOP {
			get {
				if (!has_signals || (signal_info.SIGSTOP < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGSTOP;
			}
		}

		public int SIGINT {
			get {
				if (!has_signals || (signal_info.SIGINT < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGINT;
			}
		}

		public int SIGCHLD {
			get {
				if (!has_signals || (signal_info.SIGCHLD < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGCHLD;
			}
		}

		public bool Has_SIGWINCH {
			get { return has_signals && (signal_info.SIGWINCH > 0); }
		}

		public int SIGWINCH {
			get {
				if (!has_signals || (signal_info.SIGWINCH < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGWINCH;
			}
		}

		public bool IsManagedSignal (int signal)
		{
			if (!has_signals)
				throw new InvalidOperationException ();

			if ((signal == signal_info.SIGFPE) || (signal == signal_info.SIGQUIT) ||
			    (signal == signal_info.SIGABRT) || (signal == signal_info.SIGSEGV) ||
			    (signal == signal_info.SIGILL) || (signal == signal_info.SIGBUS))
				return true;

			return false;
		}

		/*
		 * CAUTION: This is the hard limit of the Linux kernel, not the first
		 *          user-visible real-time signal !
		 */
		public int Kernel_SIGRTMIN {
			get {
				if (!has_signals || (signal_info.Kernel_SIGRTMIN < 0))
					throw new InvalidOperationException ();

				return signal_info.Kernel_SIGRTMIN;
			}
		}

		public int MonoThreadAbortSignal {
			get {
				if (!has_signals || (signal_info.MonoThreadAbortSignal < 0))
					throw new InvalidOperationException ();

				return signal_info.MonoThreadAbortSignal;
			}
		}

		//
		// IDisposable
		//

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Inferior");
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				this.disposed = true;

				// Release unmanaged resources
				lock (this) {
					if (server_inferior != null) {
						server_inferior.Dispose ();
						server_inferior = null;
					}
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Inferior ()
		{
			Dispose (false);
		}
	}
}
