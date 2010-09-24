using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal class MdbInferior : ServerObject, IInferior
	{
		public MdbInferior (Connection connection, int id)
			: base (connection, id, ServerObjectKind.Inferior)
		{ }

		enum CmdInferior {
			GET_SIGNAL_INFO = 3,
			GET_APPLICATION = 4,
			GET_FRAME = 5,
			INSERT_BREAKPOINT = 6,
			ENABLE_BREAKPOINT = 7,
			DISABLE_BREAKPOINT = 8,
			REMOVE_BREAKPOINT = 9,
			STEP = 10,
			CONTINUE = 11,
			RESUME_STEPPING = 12,
			GET_REGISTERS = 13,
			SET_REGISTERS = 14,
			READ_MEMORY = 15,
			WRITE_MEMORY = 16,
			GET_PENDING_SIGNAL = 17,
			SET_SIGNAL = 18,
			DISASSEMBLE_INSN = 19,
			STOP = 20,
			CALL_METHOD = 21,
			GET_PID = 22
		}

		public bool Stop ()
		{
			try {
				Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.STOP, new Connection.PacketWriter ().WriteId (ID));
				return true;
			} catch (TargetException ex) {
				if (ex.Type == TargetError.AlreadyStopped)
					return false;
				throw;
			}
		}

		public SignalInfo GetSignalInfo ()
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_SIGNAL_INFO, new Connection.PacketWriter ().WriteId (ID));

			SignalInfo sinfo;

			sinfo.SIGKILL = reader.ReadInt();
			sinfo.SIGSTOP = reader.ReadInt();
			sinfo.SIGINT = reader.ReadInt();
			sinfo.SIGCHLD = reader.ReadInt();
			sinfo.SIGFPE = reader.ReadInt();
			sinfo.SIGQUIT = reader.ReadInt();
			sinfo.SIGABRT = reader.ReadInt();
			sinfo.SIGSEGV = reader.ReadInt();
			sinfo.SIGILL = reader.ReadInt();
			sinfo.SIGBUS = reader.ReadInt();
			sinfo.SIGWINCH = reader.ReadInt();
			sinfo.Kernel_SIGRTMIN = reader.ReadInt();
			sinfo.MonoThreadAbortSignal = -1;

			return sinfo;
		}

		public string GetApplication (out string cwd, out string[] cmdline_args)
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_APPLICATION, new Connection.PacketWriter ().WriteId (ID));

			string exe = reader.ReadString ();
			cwd = reader.ReadString ();

			int nargs = reader.ReadInt ();
			cmdline_args = new string [nargs];
			for (int i = 0; i < nargs; i++)
				cmdline_args [i] = reader.ReadString ();

			return exe;
		}

		public ServerStackFrame GetFrame ()
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_FRAME, new Connection.PacketWriter ().WriteId (ID));
			ServerStackFrame frame;
			frame.Address = reader.ReadLong ();
			frame.StackPointer = reader.ReadLong ();
			frame.FrameAddress = reader.ReadLong ();
			return frame;
		}

		public int InsertBreakpoint (long address)
		{
			return Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.INSERT_BREAKPOINT,
						       new Connection.PacketWriter ().WriteId (ID).WriteLong (address)).ReadInt ();
		}

		public int InsertHardwareBreakpoint (HardwareBreakpointType type, bool fallback,
						     long address, out int hw_index)
		{
			if ((type == HardwareBreakpointType.None) && fallback) {
				hw_index = -1;
				return InsertBreakpoint (address);
			}

			throw new NotImplementedException ();
		}

		public void EnableBreakpoint (int breakpoint)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.ENABLE_BREAKPOINT,
						new Connection.PacketWriter ().WriteId (ID).WriteInt (breakpoint));
		}

		public void DisableBreakpoint (int breakpoint)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISABLE_BREAKPOINT,
						new Connection.PacketWriter ().WriteId (ID).WriteInt (breakpoint));
		}

		public void RemoveBreakpoint (int breakpoint)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.REMOVE_BREAKPOINT,
						new Connection.PacketWriter ().WriteId (ID).WriteInt (breakpoint));
		}

		public bool CurrentInsnIsBpt ()
		{
			throw new NotImplementedException ();
		}

		public void Step ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.STEP, new Connection.PacketWriter ().WriteId (ID));
		}

		public void Continue ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.CONTINUE, new Connection.PacketWriter ().WriteId (ID));
		}

		public void ResumeStepping ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.RESUME_STEPPING, new Connection.PacketWriter ().WriteId (ID));
		}

		public long[] GetRegisters ()
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_REGISTERS, new Connection.PacketWriter ().WriteId (ID));
			int count = reader.ReadInt ();
			long[] regs = new long [count];
			for (int i = 0; i < count; i++)
				regs [i] = reader.ReadLong ();

			return regs;
		}

		public void SetRegisters (long[] regs)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteId (ID);
			foreach (long reg in regs)
				writer.WriteLong (reg);
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.SET_REGISTERS, writer);
		}

		public byte[] ReadMemory (long address, int size)
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.READ_MEMORY, new Connection.PacketWriter ().WriteId (ID).WriteLong (address).WriteInt (size));
			return reader.ReadData (size);
		}

		public void WriteMemory (long address, byte[] data)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.WRITE_MEMORY, new Connection.PacketWriter ().WriteId (ID).WriteLong (address).WriteInt (data.Length).WriteData (data));
		}

		public int GetPendingSignal ()
		{
			return Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_PENDING_SIGNAL, new Connection.PacketWriter ().WriteId (ID)).ReadInt ();
		}

		public void SetSignal (int sig, bool send_it)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.SET_SIGNAL, new Connection.PacketWriter ().WriteId (ID).WriteInt (sig).WriteByte (send_it ? (byte)1 : (byte)0));
		}

		public string DisassembleInstruction (long address, out int insn_size)
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISASSEMBLE_INSN, new Connection.PacketWriter ().WriteId (ID).WriteLong (address));
			insn_size = reader.ReadInt ();
			return reader.ReadString ();
		}

		public void MarkRuntimeInvokeFrame ()
		{
			throw new NotImplementedException ();
		}

		public void AbortInvoke (long rti_id)
		{
			throw new NotImplementedException ();
		}

		public void RuntimeInvoke (long invoke_method, long method_address, int num_params,
					   byte[] blob, int[] blob_offsets, long[] addresses,
					   long callback_arg, bool debug)
		{
			throw new NotImplementedException ();
		}

		public ServerCallbackFrame GetCallbackFrame (long stack_pointer, bool exact_match)
		{
			return null;
		}

		public void SetRuntimeInfo (MonoRuntimeHandle runtime)
		{
			throw new NotImplementedException ();
		}

		public long PushRegisters ()
		{
			throw new NotImplementedException ();
		}

		public void PopRegisters ()
		{
			throw new NotImplementedException ();
		}

		public void Detach ()
		{
			throw new NotImplementedException ();
		}

		public void DetachAfterFork ()
		{
			throw new NotImplementedException ();
		}

		public void Kill ()
		{
			throw new NotImplementedException ();
		}

		public void CallMethod (InvocationData data)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteId (ID);
			writer.WriteByte ((byte) data.Type);
			writer.WriteLong (data.MethodAddress);
			writer.WriteLong (data.CallbackID);
			writer.WriteLong (data.Arg1);
			writer.WriteLong (data.Arg2);
			writer.WriteLong (data.Arg3);
			writer.WriteString (data.StringArg);

			Connection.SendReceive (CommandSet.INFERIOR, (int) CmdInferior.CALL_METHOD, writer);
		}

		bool initialized;
		int pid;
		long tid;

		void initialize ()
		{
			lock (this) {
				if (initialized)
					return;

				var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_PID, new Connection.PacketWriter ().WriteId (ID));
				pid = reader.ReadInt ();
				tid = reader.ReadLong ();

				initialized = true;
			}
		}

		public int PID {
			get {
				initialize ();
				return pid;
			}
		}

		public long TID {
			get {
				initialize ();
				return tid;
			}
		}
	}
}
