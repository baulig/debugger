#include <mdb-process.h>
#include <mono-runtime.h>

GHashTable *MdbProcess::inferior_by_thread_id;
MdbProcess *MdbProcess::main_process;

void
MdbProcess::Initialize (void)
{
	inferior_by_thread_id = g_hash_table_new (NULL, NULL);
}

MdbInferior *
MdbProcess::GetInferiorByThreadId (guint32 thread_id)
{
	return (MdbInferior *) g_hash_table_lookup (inferior_by_thread_id, GUINT_TO_POINTER (thread_id));
}

void
MdbProcess::AddInferior (guint32 thread_id, MdbInferior *inferior)
{
	g_hash_table_insert (inferior_by_thread_id, GUINT_TO_POINTER (thread_id), inferior);
}

MdbExeReader *
MdbProcess::GetExeReader (const char *filename)
{
	MdbExeReader *reader;

	reader = (MdbExeReader *) g_hash_table_lookup (exe_file_hash, filename);
	if (reader)
		return reader;

	reader = mdb_server_create_exe_reader (filename);
	g_hash_table_insert (exe_file_hash, g_strdup (filename), reader);

	if (!main_reader)
		main_reader = reader;

	return reader;
}

MdbDisassembler *
MdbProcess::GetDisassembler (MdbInferior *inferior)
{
	return main_reader->GetDisassembler (inferior);
}

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

	case CMD_PROCESS_SPAWN: {
		char *cwd, **argv, *error;
		MdbInferior *inferior;
		guint32 thread_id;
		ErrorCode result;
		int argc, i;

		cwd = in->DecodeString ();
		argc = in->DecodeInt ();

		argv = g_new0 (char *, argc + 1);
		for (i = 0; i < argc; i++)
			argv [i] = in->DecodeString ();
		argv [argc] = NULL;

		if (!*cwd) {
			g_free (cwd);
			cwd = g_get_current_dir ();
		}

		result = Spawn (cwd, (const gchar **) argv, NULL, &inferior, &thread_id, &error);
		if (result)
			return result;

		out->AddInt (inferior->GetID ());
		out->AddInt (thread_id);

		g_free (cwd);
		for (i = 0; i < argc; i++)
			g_free (argv [i]);
		g_free (argv);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

void
MdbProcess::OnMainModuleLoaded (MdbInferior *inferior, MdbExeReader *reader)
{
	ServerEvent *e;

	this->main_reader = reader;

	e = g_new0 (ServerEvent, 1);

	e->type = SERVER_EVENT_MAIN_MODULE_LOADED;
	e->sender = this;
	e->arg_object = reader;
	server->SendEvent (e);
	g_free (e);

	CheckLoadedDll (inferior, reader);
}

void
MdbProcess::CheckLoadedDll (MdbInferior *inferior, MdbExeReader *reader)
{
	ServerEvent *e;

	if (mono_runtime)
		return;

	mono_runtime = MonoRuntime::Initialize (inferior, reader);
	if (!mono_runtime)
		return;

	e = g_new0 (ServerEvent, 1);

	e->type = SERVER_EVENT_MONO_RUNTIME_LOADED;
	e->sender = this;
	e->arg_object = mono_runtime;
	server->SendEvent (e);
	g_free (e);
}

void
MdbProcess::OnDllLoaded (MdbInferior *inferior, MdbExeReader *reader)
{
	ServerEvent *e;

	e = g_new0 (ServerEvent, 1);

	e->type = SERVER_EVENT_DLL_LOADED;
	e->sender = this;
	e->arg_object = reader;
	server->SendEvent (e);
	g_free (e);

	CheckLoadedDll (inferior, reader);
}
