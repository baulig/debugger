using System;

using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbProcess : ServerObject, IProcess
	{
		public MdbProcess (Connection connection, int id)
			: base (connection, id, ServerObjectKind.Process)
		{
			int reader_iid = connection.SendReceive (CommandSet.PROCESS, (int)CmdProcess.GET_MAIN_READER, new Connection.PacketWriter ().WriteInt (ID)).ReadInt ();
			MainReader = new MdbExeReader (connection, reader_iid);
		}

		enum CmdProcess {
			GET_MAIN_READER = 1,
			INITIALIZE_PROCESS = 2
		}

		public MdbExeReader MainReader {
			get; private set;
		}

		IExecutableReader IProcess.MainReader {
			get { return MainReader; }
		}

		public void InitializeProcess ()
		{
			Connection.SendReceive (CommandSet.PROCESS, (int)CmdProcess.INITIALIZE_PROCESS, new Connection.PacketWriter ().WriteInt (ID));
		}

		internal override void HandleEvent (ServerEvent e)
		{
			throw new InternalError ("GOT UNEXPECTED EVENT: {0}", e);
		}
	}
}
