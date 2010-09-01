#ifndef __MDB_PROCESS_H__
#define __MDB_PROCESS_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

class MdbProcess : public ServerObject
{
public:
	static void Initialize (void);

	virtual void InitializeProcess (MdbInferior *inferior) = 0;

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

	void OnMainModuleLoaded (MdbExeReader *reader);
	MdbExeReader *OnMainModuleLoaded (const char *filename);

	void OnDllLoaded (MdbExeReader *reader);
	MdbExeReader *OnDllLoaded (const char *filename);

	static MdbProcess *GetMainProcess (void)
	{
		return main_process;
	}

	static MdbInferior *GetInferiorByThreadId (guint32 thread_id);
	static void AddInferior (guint32 thread_id, MdbInferior *inferior);

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

protected:
	MdbProcess (MdbServer *server)
		: ServerObject (SERVER_OBJECT_KIND_PROCESS)
	{
		this->server = server;
		this->main_reader = NULL;
		this->exe_file_hash = g_hash_table_new (NULL, NULL);
	}

protected:
	MdbServer *server;
	static MdbProcess *main_process;

private:
	GHashTable *exe_file_hash;
	MdbExeReader *main_reader;

	static GHashTable *inferior_by_thread_id;
};

extern MdbProcess *mdb_process_new (MdbServer *server);

#endif
