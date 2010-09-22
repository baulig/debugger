#ifndef __MDB_PROCESS_H__
#define __MDB_PROCESS_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

class MonoRuntime;

typedef void (* InferiorForeachFunc) (MdbInferior *inferior, gpointer user_data);

class MdbProcess : public ServerObject
{
public:
	static void Initialize (void);

	MdbExeReader *GetMainReader (void)
	{
		return main_reader;
	}

	MdbExeReader *GetExeReader (const char *filename);

	MdbDisassembler *GetDisassembler (MdbInferior *inferior);

	virtual ErrorCode Spawn (const gchar *working_directory,
				 const gchar **argv, const gchar **envp,
				 MdbInferior **out_inferior, guint32 *out_thread_id,
				 gchar **out_error) = 0;

	virtual ErrorCode Attach (int pid, MdbInferior **out_inferior, guint32 *out_thread_id) = 0;

	virtual ErrorCode SuspendProcess (MdbInferior *caller) = 0;

	virtual ErrorCode ResumeProcess (MdbInferior *caller) = 0;

	void OnMainModuleLoaded (MdbInferior *inferior, MdbExeReader *reader);

	MdbExeReader *OnMainModuleLoaded (MdbInferior *inferior, const char *filename)
	{
		MdbExeReader *reader = GetExeReader (filename);
		if (reader)
			OnMainModuleLoaded (inferior, reader);
		return reader;
	}

	void OnDllLoaded (MdbInferior *inferior, MdbExeReader *reader);

	MdbExeReader *OnDllLoaded (MdbInferior *inferior, const char *filename)
	{
		MdbExeReader *reader = GetExeReader (filename);
		if (reader)
			OnDllLoaded (inferior, reader);
		return reader;
	}

	static MdbProcess *GetMainProcess (void)
	{
		return main_process;
	}

	MonoRuntime *GetMonoRuntime (void)
	{
		return mono_runtime;
	}

	static MdbInferior *GetInferiorByPID (int pid);
	static void AddInferior (int pid, MdbInferior *inferior);

	MdbInferior *GetInferiorByTID (gsize tid);
	void AddInferiorByTID (gsize tid, MdbInferior *inferior);

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

	MdbExeReader *LookupDll (const char *name, bool exact_match);

	GList *GetThreads (void);

protected:
	MdbProcess (MdbServer *server)
		: ServerObject (SERVER_OBJECT_KIND_PROCESS)
	{
		this->server = server;
		this->main_reader = NULL;
		this->exe_file_hash = g_hash_table_new (NULL, NULL);
		this->inferior_by_tid = g_hash_table_new (NULL, NULL);
		this->mono_runtime = NULL;
	}

	void ForeachInferior (InferiorForeachFunc func, gpointer user_data);

	bool initialized;

	MdbServer *server;
	MonoRuntime *mono_runtime;
	static MdbProcess *main_process;

private:
	GHashTable *exe_file_hash;
	MdbExeReader *main_reader;

	void CheckLoadedDll (MdbInferior *inferior, MdbExeReader *reader);

	static GHashTable *inferior_by_pid;
	GHashTable *inferior_by_tid;
};

extern MdbProcess *mdb_process_new (MdbServer *server);

#endif
