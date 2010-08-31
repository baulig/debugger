#ifndef __MDB_SERVER_LINUX_H__
#define __MDB_SERVER_LINUX_H__ 1

#include <connection.h>
#include <mdb-server.h>

class MdbServerLinux : public MdbServer
{
public:
	MdbServerLinux (Connection *connection) : MdbServer (connection)
	{ }

protected:
	static MdbInferior *GetInferiorByPid (int pid);

	MdbInferior *CreateThread (MdbProcess *process, int pid, bool wait_for_it);

	bool HandleExtendedWaitEvent (MdbProcess *process, int pid, int status);

	void HandleLinuxWaitEvent (void);

	void MainLoop (int conn_fd);
};

#endif
