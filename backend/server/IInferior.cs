using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Server
{
	internal enum HardwareBreakpointType {
		None = 0,
		Execute,
		Read,
		Write
	}

	internal struct ServerStackFrame
	{
		public long Address;
		public long StackPointer;
		public long FrameAddress;
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

	internal class ServerCallbackFrame
	{
		public readonly long ID;
		public readonly long CallAddress;
		public readonly long StackPointer;
		public readonly bool IsRuntimeInvokeFrame;
		public readonly bool IsExactMatch;
		public readonly long[] Registers;

		public ServerCallbackFrame (IntPtr data, int count_regs)
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

	internal enum InvocationType
	{
		LongLong = 1,
		LongLongLongString,
		LongData,
		Context
	}

	[StructLayout (LayoutKind.Sequential)]
	internal struct InvocationData
	{
		public readonly InvocationType Type;
		public readonly long MethodAddress;
		public readonly long CallbackID;
		public readonly long Arg1;
		public readonly long Arg2;
		public readonly long Arg3;
		public readonly string StringArg;
		public readonly byte[] DataArg;

		public InvocationData (long method, long id, long arg1, long arg2)
		{
			Type = InvocationType.LongLong;
			MethodAddress = method;
			CallbackID = id;
			Arg1 = arg1;
			Arg2 = arg2;
			Arg3 = 0;
			StringArg = null;
			DataArg = null;
		}

		public InvocationData (long method, long id, long arg1, long arg2,
				       long arg3, string string_arg)
		{
			Type = InvocationType.LongLongLongString;
			MethodAddress = method;
			CallbackID = id;
			Arg1 = arg1;
			Arg2 = arg2;
			Arg3 = arg3;
			StringArg = string_arg;
			DataArg = null;
		}

		public InvocationData (long method, long id, long arg1, byte[] data)
		{
			Type = InvocationType.LongData;
			MethodAddress = method;
			CallbackID = id;
			Arg1 = arg1;
			Arg2 = 0;
			Arg3 = 0;
			StringArg = null;
			DataArg = data;
		}

		public InvocationData (long method, long id)
		{
			Type = InvocationType.Context;
			MethodAddress = method;
			CallbackID = id;
			Arg1 = 0;
			Arg2 = 0;
			Arg3 = 0;
			StringArg = null;
			DataArg = null;
		}
	}

	internal abstract class MonoRuntimeHandle
	{ }

	internal interface IInferior : IServerObject, IDisposable
	{
		int PID {
			get;
		}

		long TID {
			get;
		}

		bool Stop ();

		SignalInfo GetSignalInfo ();

		string GetApplication (out string cwd, out string[] cmdline_args);

		ServerStackFrame GetFrame ();

		int InsertBreakpoint (long address);

		int InsertHardwareBreakpoint (HardwareBreakpointType type, bool fallback,
					      long address, out int hw_index);

		void EnableBreakpoint (int breakpoint);

		void DisableBreakpoint (int breakpoint);

		void RemoveBreakpoint (int breakpoint);

		bool CurrentInsnIsBpt ();

		void Step ();

		void Continue ();

		void ResumeStepping ();

		long[] GetRegisters ();

		void SetRegisters (long[] regs);

		byte[] ReadMemory (long address, int size);

		void WriteMemory (long address, byte[] data);

		int GetPendingSignal ();

		void SetSignal (int sig, bool send_it);

		string DisassembleInstruction (long address, out int insn_size);

		void MarkRuntimeInvokeFrame ();

		void AbortInvoke (long rti_id);

		void RuntimeInvoke (long invoke_method, long method_address, int num_params,
				    byte[] blob, int[] blob_offsets, long[] addresses,
				    long callback_arg, bool debug);

		ServerCallbackFrame GetCallbackFrame (long stack_pointer, bool exact_match);

		void SetRuntimeInfo (MonoRuntimeHandle runtime);

		long PushRegisters ();

		void PopRegisters ();

		void Detach ();

		void DetachAfterFork ();

		void Kill ();

		void CallMethod (InvocationData data);
	}
}