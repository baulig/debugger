using System;
using System.Runtime.InteropServices;
using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbInferior : ServerObject, IInferior
	{
		public MdbInferior (Connection connection, int id)
			: base (connection, id, ServerObjectKind.Inferior)
		{ }

		enum CmdInferior {
			SPAWN = 1,
			INITIALIZE_PROCESS = 2,
			GET_SIGNAL_INFO = 3,
			GET_APPLICATION = 4,
			GET_FRAME = 5,
			INSERT_BREAKPOINT = 6,
			ENABLE_BREAKPOINT = 7,
			DISABLE_BREAKPOINT = 8,
			REMOVE_BREAKPOINT = 9,
			STEP = 10,
			CONTINUE = 11,
			RESUME = 12,
			GET_REGISTERS = 13,
			READ_MEMORY = 14,
			WRITE_MEMORY = 15,
			GET_PENDING_SIGNAL = 16,
			SET_SIGNAL = 17,
			INIT_AT_ENTRYPOINT = 18,
			DISASSEMBLE_INSN
		}

		public int Spawn (string cwd, string[] argv, string[] envp)
		{
			var writer = new Connection.PacketWriter ();
			writer.WriteInt (ID);
			writer.WriteString (cwd ?? "");

			int argc = argv.Length;
			if (argv [argc-1] == null)
				argc--;
			writer.WriteInt (argc);
			for (int i = 0; i < argc; i++)
				writer.WriteString (argv [i] ?? "dummy");
			int pid = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.SPAWN, writer).ReadInt ();
			Console.WriteLine ("CHILD PID: {0}", pid);
			return pid;
		}

		public void Attach (int pid)
		{
			throw new NotImplementedException ();
		}

		public void InitializeProcess ()
		{
			Connection.SendReceive (CommandSet.SERVER, (int)CmdInferior.INITIALIZE_PROCESS, null);
		}

		public void InitializeThread (int child_pid, bool wait)
		{
			throw new NotImplementedException ();
		}

		public void InitializeAtEntryPoint ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.INIT_AT_ENTRYPOINT, new Connection.PacketWriter ().WriteInt (ID));
		}

		public bool Stop ()
		{
			throw new NotImplementedException ();
		}

		public DebuggerServer.SignalInfo GetSignalInfo ()
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_SIGNAL_INFO, new Connection.PacketWriter ().WriteInt (ID));

			DebuggerServer.SignalInfo sinfo;

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
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_APPLICATION, new Connection.PacketWriter ().WriteInt (ID));

			string exe = reader.ReadString ();
			cwd = reader.ReadString ();

			int nargs = reader.ReadInt ();
			cmdline_args = new string [nargs];
			for (int i = 0; i < nargs; i++)
				cmdline_args [i] = reader.ReadString ();

			return exe;
		}

		public DebuggerServer.ServerStackFrame GetFrame ()
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_FRAME, new Connection.PacketWriter ().WriteInt (ID));
			DebuggerServer.ServerStackFrame frame;
			frame.Address = reader.ReadLong ();
			frame.StackPointer = reader.ReadLong ();
			frame.FrameAddress = reader.ReadLong ();
			return frame;
		}

		public int InsertBreakpoint (long address)
		{
			return Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.INSERT_BREAKPOINT,
						       new Connection.PacketWriter ().WriteInt (ID).WriteLong (address)).ReadInt ();
		}

		public int InsertHardwareBreakpoint (DebuggerServer.HardwareBreakpointType type, bool fallback,
						     long address, out int hw_index)
		{
			throw new NotImplementedException ();
		}

		public void EnableBreakpoint (int breakpoint)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.ENABLE_BREAKPOINT,
						new Connection.PacketWriter ().WriteInt (ID).WriteInt (breakpoint));
		}

		public void DisableBreakpoint (int breakpoint)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISABLE_BREAKPOINT,
						new Connection.PacketWriter ().WriteInt (ID).WriteInt (breakpoint));
		}

		public void RemoveBreakpoint (int breakpoint)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.REMOVE_BREAKPOINT,
						new Connection.PacketWriter ().WriteInt (ID).WriteInt (breakpoint));
		}

		public bool CurrentInsnIsBpt ()
		{
			throw new NotImplementedException ();
		}

		public void Step ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.STEP, new Connection.PacketWriter ().WriteInt (ID));
		}

		public void Continue ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.CONTINUE, new Connection.PacketWriter ().WriteInt (ID));
		}

		public void Resume ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.RESUME, new Connection.PacketWriter ().WriteInt (ID));
		}

		public long[] GetRegisters ()
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_REGISTERS, new Connection.PacketWriter ().WriteInt (ID));
			int count = reader.ReadInt ();
			long[] regs = new long [count];
			for (int i = 0; i < count; i++)
				regs [i] = reader.ReadLong ();

			return regs;
		}

		public void SetRegisters (long[] regs)
		{
			throw new NotImplementedException ();
		}

		public byte[] ReadMemory (long address, int size)
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.READ_MEMORY, new Connection.PacketWriter ().WriteInt (ID).WriteLong (address).WriteInt (size));
			return reader.ReadData (size);
		}

		public void WriteMemory (long address, byte[] data)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.WRITE_MEMORY, new Connection.PacketWriter ().WriteInt (ID).WriteLong (address).WriteInt (data.Length).WriteData (data));
		}

		public int GetPendingSignal ()
		{
			return Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_PENDING_SIGNAL, new Connection.PacketWriter ().WriteInt (ID)).ReadInt ();
		}

		public void SetSignal (int sig, bool send_it)
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.SET_SIGNAL, new Connection.PacketWriter ().WriteInt (ID).WriteInt (sig).WriteByte (send_it ? (byte)1 : (byte)0));
		}

		public string DisassembleInstruction (long address, out int insn_size)
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISASSEMBLE_INSN, new Connection.PacketWriter ().WriteInt (ID).WriteLong (address));
			insn_size = reader.ReadInt ();
			return reader.ReadString ();
		}

		public void CallMethod (long method_address, long arg1, long arg2, long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public void CallMethod (long method_address, long arg1, long arg2, long arg3,
					string string_arg, long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public void CallMethod (long method_address, byte[] data, long callback_arg)
		{
			throw new NotImplementedException ();
		}

		public void CallMethod (long method_address, long arg1, long arg2,
					byte[] data, long callback_arg)
		{
			throw new NotImplementedException ();
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

		public void ExecuteInstruction (byte[] instruction, bool update_ip)
		{
			throw new NotImplementedException ();
		}

		public DebuggerServer.CallbackFrame GetCallbackFrame (long stack_pointer, bool exact_match)
		{
			throw new NotImplementedException ();
		}

		public void SetRuntimeInfo (DebuggerServer.MonoRuntimeHandle runtime)
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

		internal override void HandleEvent (ServerEvent e)
		{
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
