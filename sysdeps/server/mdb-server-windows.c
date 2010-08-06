#include <mdb-server.h>
#include <errno.h>
#include <unistd.h>
#include <string.h>
#include <bfd.h>

#ifdef _MSC_VER
#include <winsock2.h>
#endif
#include <ws2tcpip.h>

static HANDLE debug_thread;
static HANDLE command_event;
static HANDLE ready_event;
static HANDLE command_mutex;

static InferiorDelegate *inferior_delegate;

static DWORD WINAPI debugging_thread_main (LPVOID dummy_arg);

int
mdb_server_init_os (void)
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
		return -1;
	}

	if (LOBYTE(wsaData.wVersion) != 2 || HIBYTE(wsaData.wVersion) != 2) {
		g_warning (G_STRLOC ": Could not find a usable version of Winsock.dll");
		return -1;
	}

	command_event = CreateEvent (NULL, FALSE, FALSE, NULL);
	g_assert (command_event);

	ready_event = CreateEvent (NULL, FALSE, FALSE, NULL);
	g_assert (ready_event);

	command_mutex = CreateMutex (NULL, FALSE, NULL);
	g_assert (command_mutex);

	debug_thread = CreateThread (NULL, 0, debugging_thread_main, NULL, 0, NULL);
	g_assert (debug_thread);

	return 0;
}

gboolean
mdb_server_inferior_command (InferiorDelegate *delegate)
{
	if (WaitForSingleObject (command_mutex, 0) != 0) {
		g_warning (G_STRLOC ": Failed to acquire command mutex !");
		return FALSE;
	}

	inferior_delegate = delegate;
	SetEvent (command_event);

	WaitForSingleObject (ready_event, INFINITE);

	ReleaseMutex (command_mutex);
	return TRUE;
}

DWORD WINAPI
debugging_thread_main (LPVOID dummy_arg)
{
	while (TRUE) {
		InferiorDelegate *delegate;

		WaitForSingleObject (command_event, INFINITE);

		g_message (G_STRLOC ": Got command event!");

		delegate = inferior_delegate;
		inferior_delegate = NULL;

		delegate->func (delegate->user_data);

		g_message (G_STRLOC ": Command event done!");

		SetEvent (ready_event);
	}

	return 0;
}

void
mdb_server_main_loop (int conn_fd)
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
			if (!mdb_server_main_loop_iteration ())
				break;
		}
	}
}
