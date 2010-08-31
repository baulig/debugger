#ifndef __MDB_SERVER_WINDOWS_H__
#define __MDB_SERVER_WINDOWS_H__ 1

#include <connection.h>
#include <mdb-server.h>

class MdbServerWindows : public MdbServer
{
public:
	MdbServerWindows (Connection *connection) : MdbServer (connection)
	{ }

	static void Initialize (void);

protected:

	void MainLoop (int conn_fd);
};

#endif
