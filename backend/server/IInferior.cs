using System;

namespace Mono.Debugger.Server
{
	internal interface IInferior : IDisposable
	{
		int Spawn (string cwd, string[] argv, string[] envp);

		void Attach (int pid);

		void InitializeProcess ();

		void InitializeThread (int child_pid, bool wait);

		void InitializeAtEntryPoint ();

		bool Stop ();

		DebuggerServer.SignalInfo GetSignalInfo ();

		string GetApplication (out string cwd, out string[] cmdline_args);

		DebuggerServer.ServerStackFrame GetFrame ();

		int InsertBreakpoint (long address);

		int InsertHardwareBreakpoint (DebuggerServer.HardwareBreakpointType type, bool fallback,
					      long address, out int hw_index);

		void EnableBreakpoint (int breakpoint);

		void DisableBreakpoint (int breakpoint);

		void RemoveBreakpoint (int breakpoint);

		bool CurrentInsnIsBpt ();

		void Step ();

		void Continue ();

		void Resume ();

		long[] GetRegisters ();

		void SetRegisters (long[] regs);

		byte[] ReadMemory (long address, int size);

		void WriteMemory (long address, byte[] data);

		int GetPendingSignal ();

		void SetSignal (int sig, bool send_it);

		string DisassembleInstruction (long address, out int insn_size);

		void CallMethod (long method_address, long arg1, long arg2, long callback_arg);

		void CallMethod (long method_address, long arg1, long arg2, long arg3,
				 string string_arg, long callback_arg);

		void CallMethod (long method_address, byte[] data, long callback_arg);

		void CallMethod (long method_address, long arg1, long arg2,
				 byte[] data, long callback_arg);

		void MarkRuntimeInvokeFrame ();

		void AbortInvoke (long rti_id);

		void RuntimeInvoke (long invoke_method, long method_address, int num_params,
				    byte[] blob, int[] blob_offsets, long[] addresses,
				    long callback_arg, bool debug);

		void ExecuteInstruction (byte[] instruction, bool update_ip);

		DebuggerServer.CallbackFrame GetCallbackFrame (long stack_pointer, bool exact_match);

		void SetRuntimeInfo (DebuggerServer.MonoRuntimeHandle runtime);

		long PushRegisters ();

		void PopRegisters ();

		void Detach ();

		void DetachAfterFork ();

		void Kill ();
	}
}
