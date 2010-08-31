#include <mdb-process.h>

ErrorCode
MdbProcess::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	switch (command) {
	case CMD_PROCESS_GET_MAIN_READER:
		out->AddInt (main_reader->GetID ());
		break;

	case CMD_PROCESS_INITIALIZE_PROCESS: {
		MdbInferior *inferior;
		int iid;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior)
			return ERR_NO_SUCH_INFERIOR;

		InitializeProcess (inferior);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

