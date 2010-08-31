#ifndef __MDB_SERVER_LINUX_H__
#define __MDB_SERVER_LINUX_H__ 1

#include <connection.h>
#include <mdb-server.h>

class MdbServerLinux : public MdbServer
{
public:
	MdbServerLinux (Connection *connection) : MdbServer (connection)
	{ }

	static void Initialize (void);

protected:
	static MdbInferior *GetInferiorByPid (int pid);

	MdbInferior *CreateThread (int pid, bool wait_for_it);

	bool HandleExtendedWaitEvent (int pid, int status);

	void HandleLinuxWaitEvent (void);

	void MainLoop (int conn_fd);

	ErrorCode Spawn (const gchar *working_directory,
			 const gchar **argv, const gchar **envp,
			 MdbInferior **out_inferior, int *out_child_pid,
			 gchar **out_error);

private:
	static GHashTable *inferior_by_pid;
};

#endif
