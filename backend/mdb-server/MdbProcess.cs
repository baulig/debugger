using System;

using Mono.Debugger.Server;

namespace Mono.Debugger.MdbServer
{
	internal class MdbProcess : ServerObject, IProcess
	{
		public MdbProcess (Connection connection, int id)
			: base (connection, id, ServerObjectKind.Process)
		{ }

		bool initialized;
		MdbExeReader main_reader;

		void initialize ()
		{
			if (initialized)
				return;

			int reader_iid = Connection.SendReceive (CommandSet.PROCESS, (int) CmdProcess.GET_MAIN_READER, new Connection.PacketWriter ().WriteInt (ID)).ReadInt ();
			main_reader = new MdbExeReader (Connection, reader_iid);
			initialized = true;
		}

		enum CmdProcess {
			GET_MAIN_READER = 1,
			INITIALIZE_PROCESS = 2
		}

		public MdbExeReader MainReader {
			get { 
				lock (this) {
					initialize ();
					return main_reader;
				}
			}
		}

		IExecutableReader IProcess.MainReader {
			get { return MainReader; }
		}

		public void InitializeProcess (MdbInferior inferior)
		{
			Connection.SendReceive (CommandSet.PROCESS, (int)CmdProcess.INITIALIZE_PROCESS, new Connection.PacketWriter ().WriteInt (ID).WriteInt (inferior.ID));
		}

		void IProcess.InitializeProcess (IInferior inferior)
		{
			InitializeProcess ((MdbInferior) inferior);
		}
	}
}
