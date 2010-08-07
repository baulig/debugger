using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using ST = System.Threading;

namespace Mono.Debugger.Server
{
	internal class Connection
	{
		public Connection (IEventHandler handler)
		{
			this.EventHandler = handler;
		}

		const string HANDSHAKE_STRING = "9da91832-87f3-4cde-a92f-6384fec6536e";
		const int HEADER_LENGTH = 11;

		Socket socket;
		ST.Thread receiver_thread;
		bool disconnected;

		Dictionary<int, byte[]> reply_packets;
		Dictionary<int, ReplyCallback> reply_cbs;
		object reply_packets_monitor;

		int Receive (byte[] buf, int buf_offset, int len)
		{
			int offset = 0;

			while (offset < len) {
				int n = socket.Receive (buf, buf_offset + offset, len - offset, SocketFlags.None);

				if (n == 0)
					return offset;
				offset += n;
			}

			return offset;
		}

		enum CommandSet {
			SERVER = 1,
			INFERIOR = 2,
			EVENT = 3,
			BPM = 4,
			EXE_READER = 5
		}

		enum CmdServer {
			GET_TARGET_INFO = 1,
			GET_SERVER_TYPE = 2,
			GET_CAPABILITIES = 3,
			CREATE_INFERIOR = 4,
			CREATE_BPM = 5,
			CREATE_EXE_READER = 6
		}

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
			GET_DYNAMIC_INFO = 18,
			DISASSEMBLE_INSN
		}

		enum CmdBpm {
			LOOKUP_BY_ADDR = 1,
			LOOKUP_BY_ID = 2
		}

		enum CmdExeReader {
			GET_START_ADDRESS = 1,
			LOOKUP_SYMBOL = 2,
			GET_TARGET_NAME = 3,
			HAS_SECTION = 4,
			GET_SECTION_ADDRESS = 5,
			GET_SECTION_CONTENTS = 6
		}

		enum CmdEvent {
			COMPOSITE = 100
		}

		#region Events

		internal enum EventKind {
			TARGET_EVENT = 1
		}

		internal class EventInfo {
			public EventKind EventKind {
				get; set;
			}

			public int ReqId {
				get; set;
			}

			public DebuggerServer.ChildEvent ChildEvent {
				get; set;
			}

			public EventInfo (EventKind kind, int req_id) {
				EventKind = kind;
				ReqId = req_id;
			}
		}

		internal IEventHandler EventHandler {
			get; private set;
		}

		internal delegate void IEventHandler (EventInfo e);

		#endregion

		class Header {
			public int id;
			public int command_set;
			public int command;
			public int flags;
		}			

		public static int GetPacketLength (byte[] header) {
			int offset = 0;
			return decode_int (header, ref offset);
		}

		public static bool IsReplyPacket (byte[] packet) {
			int offset = 8;
			return decode_byte (packet, ref offset) == 0x80;
		}

		public static int GetPacketId (byte[] packet) {
			int offset = 4;
			return decode_int (packet, ref offset);
		}

		static int decode_byte (byte[] packet, ref int offset) {
			return packet [offset++];
		}

		static int decode_short (byte[] packet, ref int offset) {
			int res = ((int)packet [offset] << 8) | (int)packet [offset + 1];
			offset += 2;
			return res;
		}

		static int decode_int (byte[] packet, ref int offset) {
			int res = ((int)packet [offset] << 24) | ((int)packet [offset + 1] << 16) | ((int)packet [offset + 2] << 8) | (int)packet [offset + 3];
			offset += 4;
			return res;
		}

		static long decode_id (byte[] packet, ref int offset) {
			return decode_int (packet, ref offset);
		}

		static long decode_long (byte[] packet, ref int offset) {
			uint high = (uint)decode_int (packet, ref offset);
			uint low = (uint)decode_int (packet, ref offset);

			return (long)(((ulong)high << 32) | (ulong)low);
		}

		static Header decode_command_header (byte[] packet) {
			int offset = 0;
			Header res = new Header ();

			decode_int (packet, ref offset);
			res.id = decode_int (packet, ref offset);
			res.flags = decode_byte (packet, ref offset);
			res.command_set = decode_byte (packet, ref offset);
			res.command = decode_byte (packet, ref offset);

			return res;
		}

		static void encode_byte (byte[] buf, int b, ref int offset) {
			buf [offset] = (byte)b;
			offset ++;
		}

		static void encode_int (byte[] buf, int i, ref int offset) {
			buf [offset] = (byte)((i >> 24) & 0xff);
			buf [offset + 1] = (byte)((i >> 16) & 0xff);
			buf [offset + 2] = (byte)((i >> 8) & 0xff);
			buf [offset + 3] = (byte)((i >> 0) & 0xff);
			offset += 4;
		}

		static void encode_id (byte[] buf, long id, ref int offset) {
			encode_int (buf, (int)id, ref offset);
		}

		static void encode_long (byte[] buf, long l, ref int offset) {
			encode_int (buf, (int)((l >> 32) & 0xffffffff), ref offset);
			encode_int (buf, (int)(l & 0xffffffff), ref offset);
		}

		public static byte[] EncodePacket (int id, int commandSet, int command, byte[] data, int dataLen) {
			byte[] buf = new byte [dataLen + 11];
			int offset = 0;
			
			encode_int (buf, buf.Length, ref offset);
			encode_int (buf, id, ref offset);
			encode_byte (buf, 0, ref offset);
			encode_byte (buf, commandSet, ref offset);
			encode_byte (buf, command, ref offset);

			for (int i = 0; i < dataLen; ++i)
				buf [offset + i] = data [i];

			return buf;
		}

		delegate void ReplyCallback (int packet_id, byte[] packet);

		class PacketReader {
			byte[] packet;
			int offset;

			public PacketReader (byte[] packet) {
				this.packet = packet;

				// For event packets
				Header header = decode_command_header (packet);
				CommandSet = (CommandSet)header.command_set;
				Command = header.command;

				// For reply packets
				offset = 0;
				ReadInt (); // length
				ReadInt (); // id
				ReadByte (); // flags
				ErrorCode = ReadShort ();
			}

			public CommandSet CommandSet {
				get; set;
			}

			public int Command {
				get; set;
			}

			public int ErrorCode {
				get; set;
			}

			public int Offset {
				get {
					return offset;
				}
			}

			public int ReadByte () {
				return decode_byte (packet, ref offset);
			}

			public int ReadShort () {
				return decode_short (packet, ref offset);
			}

			public int ReadInt () {
				return decode_int (packet, ref offset);
			}

			public long ReadId () {
				return decode_id (packet, ref offset);
			}

			public long ReadLong () {
				return decode_long (packet, ref offset);
			}

			public string ReadString () {
				int len = decode_int (packet, ref offset);
				string res = new String (Encoding.UTF8.GetChars (packet, offset, len));
				offset += len;
				return res;
			}

			public byte[] ReadData (int len) {
				byte[] retval = new byte [len];
				Array.Copy (packet, offset, retval, 0, len);
				return retval;
			}
		}

		class PacketWriter {
			byte[] data;
			int offset;

			public PacketWriter () {
				// FIXME:
				data = new byte [1024];
				offset = 0;
			}

			public PacketWriter WriteByte (byte val) {
				encode_byte (data, val, ref offset);
				return this;
			}

			public PacketWriter WriteInt (int val) {
				encode_int (data, val, ref offset);
				return this;
			}

			public PacketWriter WriteId (long id) {
				encode_id (data, id, ref offset);
				return this;
			}

			public PacketWriter WriteLong (long val) {
				encode_long (data, val, ref offset);
				return this;
			}

			public PacketWriter WriteInts (int[] ids) {
				for (int i = 0; i < ids.Length; ++i)
					WriteInt (ids [i]);
				return this;
			}

			public PacketWriter WriteIds (long[] ids) {
				for (int i = 0; i < ids.Length; ++i)
					WriteId (ids [i]);
				return this;
			}

			public PacketWriter WriteString (string s) {
				encode_int (data, s.Length, ref offset);
				byte[] b = Encoding.UTF8.GetBytes (s);
				Buffer.BlockCopy (b, 0, data, offset, b.Length);
				offset += b.Length;
				return this;
			}

			public PacketWriter WriteBool (bool val) {
				WriteByte (val ? (byte)1 : (byte)0);
				return this;
			}

			public PacketWriter WriteData (byte[] buffer) {
				Buffer.BlockCopy (buffer, 0, data, offset, buffer.Length);
				offset += buffer.Length;
				return this;
			}

			public byte[] Data {
				get {
					return data;
				}
			}

			public int Offset {
				get {
					return offset;
				}
			}
		}

		public byte[] ReadPacket () {
			// FIXME: Throw ClosedConnectionException () if the connection is closed
			// FIXME: Throw ClosedConnectionException () if another thread closes the connection
			// FIXME: Locking
			byte[] header = new byte [HEADER_LENGTH];

			int len = Receive (header, 0, header.Length);
			if (len == 0)
				return new byte [0];
			if (len != HEADER_LENGTH) {
				// FIXME:
				throw new IOException ("Packet of length " + len + " is read.");
			}

			int packetLength = GetPacketLength (header);
			if (packetLength < 11)
				throw new IOException ("Invalid packet length.");

			if (packetLength == 11) {
				return header;
			} else {
				byte[] buf = new byte [packetLength];
				for (int i = 0; i < header.Length; ++i)
					buf [i] = header [i];
				len = Receive (buf, header.Length, packetLength - header.Length);
				if (len != packetLength - header.Length)
					throw new IOException ();
				return buf;
			}
		}

		public void WritePacket (byte[] packet) {
			// FIXME: Throw ClosedConnectionException () if the connection is closed
			// FIXME: Throw ClosedConnectionException () if another thread closes the connection
			// FIXME: Locking
			socket.Send (packet);
		}

		public void Connect ()
		{
			socket = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect (IPAddress.Parse ("192.168.8.1"), 8888);

			byte[] buf = new byte [HANDSHAKE_STRING.Length];
			char[] cbuf = new char [buf.Length];

			// FIXME: Add a timeout
			int n = Receive (buf, 0, buf.Length);
			if (n == 0)
				throw new IOException ("Handshake failed.");
			for (int i = 0; i < buf.Length; ++i)
				cbuf [i] = (char)buf [i];

			if (new String (cbuf) != HANDSHAKE_STRING)
				throw new IOException ("Handshake failed.");

			socket.Send (buf);

			reply_packets = new Dictionary<int, byte[]> ();
			reply_cbs = new Dictionary<int, ReplyCallback> ();
			reply_packets_monitor = new Object ();

			receiver_thread = new ST.Thread (new ST.ThreadStart (receiver_thread_main));
			receiver_thread.Start ();
		}

		void receiver_thread_main ()
		{
			while (true) {
				try {
					bool res = ReceivePacket ();
					if (!res)
						break;
				} catch (Exception ex) {
					Console.WriteLine (ex);
					break;
				}
			}

			lock (reply_packets_monitor) {
				disconnected = true;
				ST.Monitor.PulseAll (reply_packets_monitor);
			}
		}

		bool ReceivePacket ()
		{
			byte[] packet = ReadPacket ();

			if (packet.Length == 0) {
				return false;
			}

			if (IsReplyPacket (packet)) {
				int id = GetPacketId (packet);
				ReplyCallback cb = null;
				lock (reply_packets_monitor) {
					reply_cbs.TryGetValue (id, out cb);
					if (cb == null) {
						reply_packets [id] = packet;
						ST.Monitor.PulseAll (reply_packets_monitor);
					}
				}

				if (cb != null)
					cb.Invoke (id, packet);
			} else {
				PacketReader r = new PacketReader (packet);

				if (r.CommandSet == CommandSet.EVENT && r.Command == (int)CmdEvent.COMPOSITE) {
					EventKind kind = (EventKind)r.ReadByte ();
					int req_id = r.ReadInt ();

					EventInfo e;

					if (kind == EventKind.TARGET_EVENT) {
						var type = (DebuggerServer.ChildEventType) r.ReadByte ();
						var arg = r.ReadLong ();
						var data1 = r.ReadLong ();
						var data2 = r.ReadLong ();
						var opt_data_size = r.ReadInt ();
						byte[] opt_data = null;
						if (opt_data_size > 0)
							opt_data = r.ReadData (opt_data_size);

						Console.WriteLine ("EVENT: {0} {1} {2:x} {3:x} {4:x} {5}",
								   type, req_id, arg, data1, data2, opt_data_size);

						DebuggerServer.ChildEvent ev;
						if (opt_data != null)
							ev = new DebuggerServer.ChildEvent (type, arg, data1, data2, opt_data);
						else
							ev = new DebuggerServer.ChildEvent (type, arg, data1, data2);

						e = new EventInfo (kind, req_id) { ChildEvent = ev };
					} else {
						throw new InternalError ("Unknown event: {0}", kind);
					}

					ST.ThreadPool.QueueUserWorkItem (delegate {
						EventHandler (e);
					});
				}
			}

			return true;
		}
	
		/* Send a request and call cb when a result is received */
		int Send (CommandSet command_set, int command, PacketWriter packet, Action<PacketReader> cb) {
			int id = IdGenerator;

			lock (reply_packets_monitor) {
				reply_cbs [id] = delegate (int packet_id, byte[] p) {
					/* Run the callback on a tp thread to avoid blocking the receive thread */
					PacketReader r = new PacketReader (p);
					cb.BeginInvoke (r, null, null);
				};
			}
						
			if (packet == null)
				WritePacket (EncodePacket (id, (int)command_set, command, null, 0));
			else
				WritePacket (EncodePacket (id, (int)command_set, command, packet.Data, packet.Offset));

			return id;
		}

		PacketReader SendReceive (CommandSet command_set, int command, PacketWriter packet) {
			int id = IdGenerator;

			if (packet == null)
				WritePacket (EncodePacket (id, (int)command_set, command, null, 0));
			else
				WritePacket (EncodePacket (id, (int)command_set, command, packet.Data, packet.Offset));

			int packetId = id;

			/* Wait for the reply packet */
			while (true) {
				lock (reply_packets_monitor) {
					if (reply_packets.ContainsKey (packetId)) {
						byte[] reply = reply_packets [packetId];
						reply_packets.Remove (packetId);
						PacketReader r = new PacketReader (reply);
						if (r.ErrorCode == 0)
							return r;
						else if (r.ErrorCode < 0x1000)
							throw new TargetException ((TargetError) r.ErrorCode);
						else {
							Console.WriteLine ("ERROR: {0}", r.ErrorCode);
							throw new NotImplementedException ("No error handler set.");
						}
					} else {
						if (disconnected)
							throw new IOException ("Disconnected !");
						ST.Monitor.Wait (reply_packets_monitor);
					}
				}
			}
		}

		PacketReader SendReceive (CommandSet command_set, int command) {
			return SendReceive (command_set, command, null);
		}

		int packet_id_generator;

		int IdGenerator {
			get {
				return ST.Interlocked.Increment (ref packet_id_generator);
			}
		}

		public TargetInfo GetTargetInfo ()
		{
			var reader = SendReceive (CommandSet.SERVER, (int)CmdServer.GET_TARGET_INFO, null);
			return new TargetInfo (reader.ReadInt (), reader.ReadInt (), reader.ReadInt (), reader.ReadByte () != 0);
		}

		public DebuggerServer.ServerType GetServerType ()
		{
			return (DebuggerServer.ServerType) SendReceive (CommandSet.SERVER, (int)CmdServer.GET_SERVER_TYPE, null).ReadInt ();
		}

		public DebuggerServer.ServerCapabilities GetCapabilities ()
		{
			return (DebuggerServer.ServerCapabilities) SendReceive (CommandSet.SERVER, (int)CmdServer.GET_CAPABILITIES, null).ReadInt ();
		}

		public int CreateInferior (int bpm_iid)
		{
			return SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_INFERIOR, new PacketWriter ().WriteInt (bpm_iid)).ReadInt ();
		}

		public int Spawn (int iid, string cwd, string[] argv)
		{
			var writer = new PacketWriter ();
			writer.WriteInt (iid);
			writer.WriteString (cwd);

			int argc = argv.Length;
			if (argv [argc-1] == null)
				argc--;
			writer.WriteInt (argc);
			for (int i = 0; i < argc; i++)
				writer.WriteString (argv [i] ?? "dummy");
			int pid = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.SPAWN, writer).ReadInt ();
			Console.WriteLine ("CHILD PID: {0}", pid);
			return pid;
		}

		public void InitializeProcess (int iid)
		{
			SendReceive (CommandSet.SERVER, (int)CmdInferior.INITIALIZE_PROCESS, null);
		}

		public int CreateBreakpointManager ()
		{
			return SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_BPM, null).ReadInt ();
		}

		public int CreateExeReader (string filename)
		{
			return SendReceive (CommandSet.SERVER, (int)CmdServer.CREATE_EXE_READER, new PacketWriter ().WriteString (filename)).ReadInt ();
		}

		public long BfdGetStartAddress (int iid)
		{
			return SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_START_ADDRESS, new PacketWriter ().WriteInt (iid)).ReadLong ();
		}

		public long BfdLookupSymbol (int iid, string name)
		{
			return SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.LOOKUP_SYMBOL, new PacketWriter ().WriteInt (iid).WriteString (name)).ReadLong ();
		}

		public string BfdGetTargetName (int iid)
		{
			return SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_TARGET_NAME, new PacketWriter ().WriteInt (iid)).ReadString ();
		}

		public bool BfdHasSection (int iid, string name)
		{
			return SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.HAS_SECTION, new PacketWriter ().WriteInt (iid).WriteString (name)).ReadByte () != 0;
		}

		public long BfdGetSectionAddress (int iid, string name)
		{
			return SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_SECTION_ADDRESS, new PacketWriter ().WriteInt (iid).WriteString (name)).ReadLong ();
		}

		public byte[] BfdGetSectionContents (int iid, string name)
		{
			var reader = SendReceive (CommandSet.EXE_READER, (int)CmdExeReader.GET_SECTION_CONTENTS, new PacketWriter ().WriteInt (iid).WriteString (name));
			int size = reader.ReadInt ();
			if (size < 0)
				return null;
			return reader.ReadData (size);
		}

		public long BfdGetDynamicInfo (int inferior_iid, int bfd_iid)
		{
			return SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_DYNAMIC_INFO, new PacketWriter ().WriteInt (inferior_iid).WriteInt (bfd_iid)).ReadLong ();
		}

		public DebuggerServer.SignalInfo GetSignalInfo (int iid)
		{
			var reader = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_SIGNAL_INFO, new PacketWriter ().WriteInt (iid));

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

		public string GetApplication (int iid, out string cwd, out string[] cmdline_args)
		{
			var reader = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_APPLICATION, new PacketWriter ().WriteInt (iid));

			string exe = reader.ReadString ();
			cwd = reader.ReadString ();

			int nargs = reader.ReadInt ();
			cmdline_args = new string [nargs];
			for (int i = 0; i < nargs; i++)
				cmdline_args [i] = reader.ReadString ();

			return exe;
		}

		public DebuggerServer.ServerStackFrame GetFrame (int iid)
		{
			var reader = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_FRAME, new PacketWriter ().WriteInt (iid));
			DebuggerServer.ServerStackFrame frame;
			frame.Address = reader.ReadLong ();
			frame.StackPointer = reader.ReadLong ();
			frame.FrameAddress = reader.ReadLong ();
			return frame;
		}

		public int InsertBreakpoint (int iid, long address)
		{
			return SendReceive (CommandSet.INFERIOR, (int)CmdInferior.INSERT_BREAKPOINT,
					    new PacketWriter ().WriteInt (iid).WriteLong (address)).ReadInt ();
		}

		public void EnableBreakpoint (int iid, int breakpoint)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.ENABLE_BREAKPOINT,
				     new PacketWriter ().WriteInt (iid).WriteInt (breakpoint));
		}

		public void DisableBreakpoint (int iid, int breakpoint)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISABLE_BREAKPOINT,
				     new PacketWriter ().WriteInt (iid).WriteInt (breakpoint));
		}

		public void RemoveBreakpoint (int iid, int breakpoint)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.REMOVE_BREAKPOINT,
				     new PacketWriter ().WriteInt (iid).WriteInt (breakpoint));
		}

		public void Step (int iid)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.STEP, new PacketWriter ().WriteInt (iid));
		}

		public void Continue (int iid)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.CONTINUE, new PacketWriter ().WriteInt (iid));
		}

		public void Resume (int iid)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.RESUME, new PacketWriter ().WriteInt (iid));
		}

		public long[] GetRegisters (int iid)
		{
			var reader = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_REGISTERS, new PacketWriter ().WriteInt (iid));
			int count = reader.ReadInt ();
			long[] regs = new long [count];
			for (int i = 0; i < count; i++)
				regs [i] = reader.ReadLong ();

			return regs;
		}

		public byte[] ReadMemory (int iid, long address, int size)
		{
			var reader = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.READ_MEMORY, new PacketWriter ().WriteInt (iid).WriteLong (address).WriteInt (size));
			return reader.ReadData (size);
		}

		public void WriteMemory (int iid, long address, byte[] data)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.WRITE_MEMORY, new PacketWriter ().WriteInt (iid).WriteLong (address).WriteInt (data.Length).WriteData (data));
		}

		public int GetPendingSignal (int iid)
		{
			return SendReceive (CommandSet.INFERIOR, (int)CmdInferior.GET_PENDING_SIGNAL, new PacketWriter ().WriteInt (iid)).ReadInt ();
		}

		public void SetSignal (int iid, int sig, bool send_it)
		{
			SendReceive (CommandSet.INFERIOR, (int)CmdInferior.SET_SIGNAL, new PacketWriter ().WriteInt (iid).WriteInt (sig).WriteByte (send_it ? (byte)1 : (byte)0));
		}

		public int LookupBreakpointByAddr (int iid, long address, out bool enabled)
		{
			var reader = SendReceive (CommandSet.BPM, (int)CmdBpm.LOOKUP_BY_ADDR, new PacketWriter ().WriteInt (iid).WriteLong (address));
			var index = reader.ReadInt ();
			enabled = reader.ReadByte () != 0;
			return index;
		}

		public bool LookupBreakpointById (int iid, int id, out bool enabled)
		{
			var reader = SendReceive (CommandSet.BPM, (int)CmdBpm.LOOKUP_BY_ID, new PacketWriter ().WriteInt (iid).WriteInt (id));
			var success = reader.ReadByte () != 0;
			if (!success) {
				enabled = false;
				return false;
			}

			enabled = reader.ReadByte () != 0;
			return true;
		}

		public string DisassembleInsn (int iid, long address, out int insn_size)
		{
			var reader = SendReceive (CommandSet.INFERIOR, (int)CmdInferior.DISASSEMBLE_INSN, new PacketWriter ().WriteInt (iid).WriteLong (address));
			insn_size = reader.ReadInt ();
			return reader.ReadString ();
		}
	}
}
