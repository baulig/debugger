#include <mdb-process.h>

ErrorCode
MdbProcess::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	switch (command) {
	case CMD_PROCESS_GET_MAIN_READER:
		if (!main_reader)
			return ERR_NO_SUCH_EXE_READER;
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

void
MdbProcess::OnMainModuleLoaded (MdbExeReader *reader)
{
	ServerEvent *e;

	this->main_reader = reader;

	e = g_new0 (ServerEvent, 1);

	e->type = SERVER_EVENT_MAIN_MODULE_LOADED;
	e->sender = this;
	e->arg_object = reader;
	server->SendEvent (e);
	g_free (e);
}

MdbExeReader *
MdbProcess::OnMainModuleLoaded (const char *filename)
{
	MdbExeReader *reader = server->GetExeReader (filename);
	if (reader)
		OnMainModuleLoaded (reader);
	return reader;
}

void
MdbProcess::OnDllLoaded (MdbExeReader *reader)
{
	ServerEvent *e;

	e = g_new0 (ServerEvent, 1);

	e->type = SERVER_EVENT_DLL_LOADED;
	e->sender = this;
	e->arg_object = reader;
	server->SendEvent (e);
	g_free (e);
}

MdbExeReader *
MdbProcess::OnDllLoaded (const char *filename)
{
	MdbExeReader *reader = server->GetExeReader (filename);
	if (reader)
		OnDllLoaded (reader);
	return reader;
}
