using System;
using System.Runtime.InteropServices;
using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbInferior : ServerObject, DebuggerServer.InferiorHandle
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

		public int Spawn (string cwd, string[] argv)
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

		public void InitializeProcess ()
		{
			Connection.SendReceive (CommandSet.SERVER, (int)CmdInferior.INITIALIZE_PROCESS, null);
		}

		public void InitializeAtEntryPoint ()
		{
			Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.INIT_AT_ENTRYPOINT, new Connection.PacketWriter ().WriteInt (ID));
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

		public string DisassembleInsn (long address, out int insn_size)
		{
			var reader = Connection.SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISASSEMBLE_INSN, new Connection.PacketWriter ().WriteInt (ID).WriteLong (address));
			insn_size = reader.ReadInt ();
			return reader.ReadString ();
		}

		internal override void HandleEvent (ServerEvent e)
		{
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
