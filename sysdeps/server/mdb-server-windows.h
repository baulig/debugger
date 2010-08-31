#ifndef __MDB_SERVER_WINDOWS_H__
#define __MDB_SERVER_WINDOWS_H__ 1

#include <connection.h>
#include <mdb-server.h>

#ifdef _MSC_VER
#include <winsock2.h>
#endif
#include <ws2tcpip.h>

#include <windows.h>

class MdbServerWindows : public MdbServer
{
public:
	MdbServerWindows (Connection *connection) : MdbServer (connection)
	{ }

	static void Initialize (void);

	static void HandleDebugEvent (DEBUG_EVENT *de);

protected:

	void MainLoop (int conn_fd);
};

#endif
