#include <mdb-server-windows.h>
#include <mdb-inferior.h>
#include <errno.h>
#if HAVE_UNISTD_H
#include <unistd.h>
#endif
#include <string.h>
#include <bfd.h>

MdbServer *
mdb_server_new (Connection *connection)
{
	return new MdbServerWindows (connection);
}

bool
MdbServer::Initialize (void)
{
	WORD wVersionRequested;
	WSADATA wsaData;
	int err;

	bfd_init ();

	/* Use the MAKEWORD(lowbyte, highbyte) macro declared in Windef.h */
	wVersionRequested = MAKEWORD (2, 2);

	err = WSAStartup (wVersionRequested, &wsaData);
	if (err != 0) {
		g_warning (G_STRLOC ": WSAStartup failed with error: %d", err);
		return false;
	}

	if (LOBYTE(wsaData.wVersion) != 2 || HIBYTE(wsaData.wVersion) != 2) {
		g_warning (G_STRLOC ": Could not find a usable version of Winsock.dll");
		return false;
	}

	return true;
}

void
MdbServerWindows::MainLoop (int conn_fd)
{
	while (TRUE) {
		fd_set readfds;
		int ret, nfds;

		FD_ZERO (&readfds);
		FD_SET (conn_fd, &readfds);
		nfds = conn_fd + 1;

#if TRANSPORT_DEBUG
		g_message (G_STRLOC ": select()");
#endif
		ret = select (nfds, &readfds, NULL, NULL, NULL);
#if TRANSPORT_DEBUG
		g_message (G_STRLOC ": select() returned: %d - %d", ret);
#endif

		if (FD_ISSET (conn_fd, &readfds)) {
			if (!MainLoopIteration ())
				break;
		}
	}
}
