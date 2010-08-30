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
	internal class DebuggerServer : DebuggerMarshalByRefObject, IDisposable
	{
		IDebuggerServer server;

		protected DebuggerServer (Debugger debugger, IDebuggerServer server)
		{
			this.server = server;

			Debugger = debugger;
			ThreadManager = new ThreadManager (debugger, this);

			Type = server.ServerType;
			Capabilities = server.Capabilities;
			ArchType = server.ArchType;
		}

		public Debugger Debugger {
			get; private set;
		}

		public ThreadManager ThreadManager {
			get; private set;
		}

		public ServerType Type {
			get; private set;
		}

		public ServerCapabilities Capabilities {
			get; private set;
		}

		public ArchType ArchType {
			get; private set;
		}

		public IBreakpointManager CreateBreakpointManager ()
		{
			return server.CreateBreakpointManager ();
		}

		public IInferior CreateInferior (SingleSteppingEngine sse, Inferior inferior,
						 IBreakpointManager breakpoint_manager)
		{
			return server.CreateInferior (breakpoint_manager);
		}

		public ExecutableReader GetExecutableReader (OperatingSystemBackend os, TargetMemoryInfo memory,
							     string filename, TargetAddress base_address, bool is_loaded)
		{
			var mdb_reader = server.CreateExeReader (filename);
			var reader = new ExecutableReader (os, memory, this, mdb_reader, filename);
			reader.ReadDebuggingInfo ();
			return reader;
		}

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

			DLL_LOADED		= 0x41,

			UNHANDLED_EXCEPTION	= 0x4001,
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

		public TargetInfo GetTargetInfo ()
		{
			return server.GetTargetInfo ();
		}

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

		internal abstract class MonoRuntimeHandle
		{ }

		public MonoRuntimeHandle InitializeMonoRuntime (
			int address_size, long notification_address,
			long executable_code_buffer, int executable_code_buffer_size,
			long breakpoint_info, long breakpoint_info_index,
			int breakpoint_table_size)
		{
			throw new NotImplementedException ();
		}

		public void InitializeCodeBuffer (MonoRuntimeHandle runtime, long executable_code_buffer,
						  int executable_code_buffer_size)
		{
			throw new NotImplementedException ();
		}

		public void FinalizeMonoRuntime (MonoRuntimeHandle runtime)
		{
			throw new NotImplementedException ();
		}

		public Disassembler GetDisassembler ()
		{
			return new ServerDisassembler (this);
		}

                protected string DisassembleInsn (Inferior inferior, long address, out int insn_size)
                {
                        var handle = (IInferior) inferior.InferiorHandle;
                        return handle.DisassembleInstruction (address, out insn_size);
                }

		class ServerDisassembler : Disassembler
		{
			public readonly DebuggerServer Server;

			public ServerDisassembler (DebuggerServer server)
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

