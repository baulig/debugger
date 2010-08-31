using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using ST = System.Threading;

using Mono.Debugger.Server;
using Mono.Debugger.Backend;

namespace Mono.Debugger.MdbServer
{
	internal enum ErrorCode
	{
		NONE = 0,

		UNKNOWN_ERROR,
		INTERNAL_ERROR,
		NO_TARGET,
		ALREADY_HAVE_TARGET,
		CANNOT_START_TARGET,
		NOT_STOPPED,
		ALREADY_STOPPED,
		RECURSIVE_CALL,
		NO_SUCH_BREAKPOINT,
		NO_SUCH_REGISTER,
		DR_OCCUPIED,
		MEMORY_ACCESS,
		NOT_IMPLEMENTED = 0x1002,
		IO_ERROR,
		NO_CALLBACK_FRAME,
		PERMISSION_DENIED,

		TARGET_ERROR_MASK = 0x0fff,

		NO_SUCH_INFERIOR = 0x1001,
		NO_SUCH_BPM = 0x1002,
		NO_SUCH_EXE_READER = 0x1003,
		CANNOT_OPEN_EXE = 0x1004,
	}

	internal enum CommandSet
	{
		SERVER = 1,
		INFERIOR = 2,
		EVENT = 3,
		BPM = 4,
		EXE_READER = 5,
		PROCESS = 6
	}

	internal class Connection
	{
		const string HANDSHAKE_STRING = "9da91832-87f3-4cde-a92f-6384fec6536e";
		const int HEADER_LENGTH = 11;

		MdbServer server;

		Socket socket;
		ST.Thread receiver_thread;
		ST.Thread wait_thread;
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

		enum CmdEvent {
			COMPOSITE = 100
		}

		class Header {
			public int id;
			public int command_set;
			public int command;
			public int flags;
		}

		internal static int GetPacketLength (byte[] header) {
			int offset = 0;
			return decode_int (header, ref offset);
		}

		internal static bool IsReplyPacket (byte[] packet) {
			int offset = 8;
			return decode_byte (packet, ref offset) == 0x80;
		}

		internal static int GetPacketId (byte[] packet) {
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

		internal static byte[] EncodePacket (int id, int commandSet, int command, byte[] data, int dataLen) {
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

		internal class PacketReader
		{
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
				ErrorCode = (ErrorCode) ReadShort ();
			}

			public CommandSet CommandSet {
				get; set;
			}

			public int Command {
				get; set;
			}

			public ErrorCode ErrorCode {
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

		internal class PacketWriter
		{
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

		internal byte[] ReadPacket () {
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

		internal void WritePacket (byte[] packet) {
			// FIXME: Throw ClosedConnectionException () if the connection is closed
			// FIXME: Throw ClosedConnectionException () if another thread closes the connection
			// FIXME: Locking
			socket.Send (packet);
		}

		public static MdbServer Connect (IPEndPoint endpoint)
		{
			var connection = new Connection (endpoint);
			return connection.server;
		}

		protected Connection (IPEndPoint endpoint)
		{
			socket = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect (endpoint);

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

			wait_queue = new DebuggerEventQueue ("event_queue");

			receiver_thread = new ST.Thread (new ST.ThreadStart (receiver_thread_main));
			receiver_thread.Start ();

			wait_thread = new ST.Thread (new ST.ThreadStart (wait_thread_main));
			wait_thread.Start ();

			server = new MdbServer (this);
		}

		public void Close ()
		{
			disconnected = true;
			socket.Close ();
			receiver_thread.Join ();
		}

		DebuggerEventQueue wait_queue;
		ServerEvent current_event;
		Queue<ServerEvent> event_queue = new Queue<ServerEvent> ();

		void wait_thread_main ()
		{
			while (!disconnected) {
				wait_queue.Lock ();

				if (event_queue.Count == 0)
					wait_queue.Wait ();

				var e = event_queue.Dequeue ();

				wait_queue.Unlock ();

				try {
					server.HandleEvent (e);
				} catch (Exception ex) {
					Console.WriteLine ("FUCK: {0}", ex);
				}
			}
		}

		void receiver_thread_main ()
		{
			while (!disconnected) {
				try {
					bool res = ReceivePacket ();
					if (!res)
						break;
				} catch (Exception ex) {
					if (!disconnected)
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
					ServerObject sender = null;
					ServerObject arg_object = null;

					var sender_kind = (ServerObjectKind) r.ReadByte ();
					if (sender_kind != ServerObjectKind.None)
						sender = ServerObject.GetObjectByID (r.ReadInt (), sender_kind);
					else
						sender = server;

					var arg_obj_kind = (ServerObjectKind) r.ReadByte ();
					if (arg_obj_kind != ServerObjectKind.None)
						arg_object = ServerObject.GetOrCreateObject (this, r.ReadInt (), arg_obj_kind);

					var type = (ServerEventType) r.ReadByte ();
					var arg = r.ReadLong ();
					var data1 = r.ReadLong ();
					var data2 = r.ReadLong ();
					var opt_data_size = r.ReadInt ();
					byte[] opt_data = null;
					if (opt_data_size > 0)
						opt_data = r.ReadData (opt_data_size);

					var e = new ServerEvent (type, sender, arg, data1, data2, arg_object, opt_data);

					//
					// Don't block the receiver thread.
					//

					wait_queue.Lock ();

					event_queue.Enqueue (e);

					if (event_queue.Count == 1)
						wait_queue.Signal ();

					wait_queue.Unlock ();

					return true;
				}
			}

			return true;
		}

		/* Send a request and call cb when a result is received */
		int Send (CommandSet command_set, int command, PacketWriter packet, Action<PacketReader> cb)
		{
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

		internal PacketReader SendReceive (CommandSet command_set, int command, PacketWriter packet)
		{
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
						if (r.ErrorCode == ErrorCode.NONE)
							return r;
						else if ((int) r.ErrorCode < 0x1000)
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

		internal PacketReader SendReceive (CommandSet command_set, int command)
		{
			return SendReceive (command_set, command, null);
		}

		int packet_id_generator;

		int IdGenerator {
			get {
				return ST.Interlocked.Increment (ref packet_id_generator);
			}
		}
	}
}
