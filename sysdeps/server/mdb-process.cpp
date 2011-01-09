#include <mdb-process.h>
#include <mono-runtime.h>
#include <string.h>

GHashTable *MdbProcess::inferior_by_pid;
MdbProcess *MdbProcess::main_process;

void
MdbProcess::Initialize (void)
{
	inferior_by_pid = g_hash_table_new (NULL, NULL);
}

MdbInferior *
MdbProcess::GetInferiorByPID (int pid)
{
	return (MdbInferior *) g_hash_table_lookup (inferior_by_pid, GUINT_TO_POINTER (pid));
}

MdbInferior *
MdbProcess::GetInferiorByTID (gsize tid)
{
	return (MdbInferior *) g_hash_table_lookup (inferior_by_tid, GUINT_TO_POINTER (tid));
}

void
MdbProcess::AddInferior (int pid, MdbInferior *inferior)
{
	g_hash_table_insert (inferior_by_pid, GUINT_TO_POINTER (pid), inferior);
}

void
MdbProcess::AddInferiorByTID (gsize tid, MdbInferior *inferior)
{
	g_hash_table_insert (inferior_by_tid, GUINT_TO_POINTER (tid), inferior);
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

static void
get_all_threads_func (gpointer key, gpointer value, gpointer user_data)
{
	MdbInferior *inferior = (MdbInferior *) value;
	Buffer *out = (Buffer *) user_data;

	out->AddID (inferior->GetID ());
}

static void
get_all_modules_func (gpointer key, gpointer value, gpointer user_data)
{
	MdbExeReader *reader = (MdbExeReader *) value;
	Buffer *out = (Buffer *) user_data;

	out->AddID (reader->GetID ());
}

ErrorCode
MdbProcess::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	switch (command) {
	case CMD_PROCESS_INITIALIZE_PROCESS: {
		MdbInferior *inferior;
		int iid;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior)
			return ERR_NO_SUCH_INFERIOR;

		return InitializeProcess (inferior);
	}

	case CMD_PROCESS_GET_MAIN_READER:
		if (!main_reader)
			return ERR_NO_SUCH_EXE_READER;
		out->AddInt (main_reader->GetID ());
		break;

	case CMD_PROCESS_SPAWN: {
		char *cwd, **argv, *error;
		MdbInferior *inferior;
		guint32 thread_id;
		ErrorCode result;
		int argc, i;
		const char *envp[] = { "MONO_ENV_OPTIONS=TestManagedTypes.exe", "MONO_PATH=/data/data/martin", "MONO_XDEBUG=mdb", NULL };

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

		result = Spawn (cwd, (const gchar **) argv, envp, &inferior, &thread_id, &error);
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

	case CMD_PROCESS_ATTACH: {
		MdbInferior *inferior;
		guint32 thread_id;
		ErrorCode result;
		int pid;

		pid = in->DecodeInt ();

		result = Attach (pid, &inferior, &thread_id);
		if (result)
			return result;

		out->AddInt (inferior->GetID ());
		out->AddInt (thread_id);
		break;
	}


	case CMD_PROCESS_SUSPEND: {
		MdbInferior *inferior;
		int iid;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior)
			return ERR_NO_SUCH_INFERIOR;

		return SuspendProcess (inferior);
	}

	case CMD_PROCESS_RESUME: {
		MdbInferior *inferior;
		int iid;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior)
			return ERR_NO_SUCH_INFERIOR;

		return ResumeProcess (inferior);
	}

	case CMD_PROCESS_GET_ALL_THREADS: {
		int count;

		count = g_hash_table_size (inferior_by_pid);
		out->AddInt (count);

		g_hash_table_foreach (inferior_by_pid, get_all_threads_func, out);
		break;
	}

	case CMD_PROCESS_GET_MONO_RUNTIME: {
		if (mono_runtime)
			out->AddID (mono_runtime->GetID ());
		else
			out->AddID (0);
		break;
	}

	case CMD_PROCESS_GET_ALL_MODULES: {
		int count;

		count = g_hash_table_size (exe_file_hash);
		out->AddInt (count);

		g_hash_table_foreach (exe_file_hash, get_all_modules_func, out);
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
	this->main_reader = reader;

	if (initialized) {
		ServerEvent *e = g_new0 (ServerEvent, 1);

		e->type = SERVER_EVENT_MAIN_MODULE_LOADED;
		e->sender = this;
		e->arg_object = reader;
		server->SendEvent (e);
		g_free (e);
	}

	CheckLoadedDll (inferior, reader);
}

void
MdbProcess::CheckLoadedDll (MdbInferior *inferior, MdbExeReader *reader)
{
	if (mono_runtime)
		return;

	mono_runtime = MonoRuntime::Initialize (inferior, reader);
	if (!mono_runtime)
		return;

	if (initialized) {
		ServerEvent *e = g_new0 (ServerEvent, 1);

		e->type = SERVER_EVENT_MONO_RUNTIME_LOADED;
		e->sender = this;
		e->arg_object = mono_runtime;
		server->SendEvent (e);
		g_free (e);
	}
}

void
MdbProcess::OnDllLoaded (MdbInferior *inferior, MdbExeReader *reader)
{
	if (initialized) {
		ServerEvent *e = g_new0 (ServerEvent, 1);

		e->type = SERVER_EVENT_DLL_LOADED;
		e->sender = this;
		e->arg_object = reader;
		server->SendEvent (e);
		g_free (e);
	}

	CheckLoadedDll (inferior, reader);
}

typedef struct {
	InferiorForeachFunc func;
	gpointer user_data;
} ForeachDelegate;

static void
inferior_foreach (gpointer key, gpointer value, gpointer user_data)
{
	MdbInferior *inferior = (MdbInferior *) value;
	ForeachDelegate *dlg = (ForeachDelegate *) user_data;

	dlg->func (inferior, dlg->user_data);
}

void
MdbProcess::ForeachInferior (InferiorForeachFunc func, gpointer user_data)
{
	ForeachDelegate dlg;

	dlg.func = func;
	dlg.user_data = user_data;
	g_hash_table_foreach (inferior_by_pid, inferior_foreach, &dlg);
}

typedef struct {
	const char *name;
	MdbExeReader *result;
} ExeReaderData;

static void
exe_reader_foreach (gpointer key, gpointer value, gpointer user_data)
{
	const char *name = (const char *) key;
	MdbExeReader *reader = (MdbExeReader *) value;
	ExeReaderData *data = (ExeReaderData *) user_data;
	gchar *basename;

	basename = g_path_get_basename (reader->GetFileName ());
	if (!strcmp (data->name, basename))
		data->result = reader;
	g_free (basename);
}

MdbExeReader *
MdbProcess::LookupDll (const char *name, bool exact_match)
{
	ExeReaderData data;

	if (exact_match)
		g_hash_table_lookup (exe_file_hash, name);

	data.name = name;
	data.result = NULL;

	g_hash_table_foreach (exe_file_hash, exe_reader_foreach, &data);

	return data.result;
}

