#include <config.h>
#include <mdb-server.h>
#include <mdb-inferior.h>
#include <mdb-exe-reader.h>
#include <debugger-mutex.h>
#include <errno.h>
#include <stdlib.h>
#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif
#include <string.h>
#ifdef HAVE_SYS_TYPES_H
#include <sys/types.h>
#endif
#ifdef HAVE_SYS_SELECT_H
#include <sys/select.h>
#endif
#ifdef HAVE_SYS_SOCKET_H
#include <sys/socket.h>
#endif
#ifdef HAVE_NETINET_TCP_H
#include <netinet/tcp.h>
#endif
#ifdef HAVE_NETINET_IN_H
#include <netinet/in.h>
#endif
#ifdef HAVE_NETDB_H
#include <netdb.h>
#endif
#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif

#ifdef WINDOWS
#include <winsock2.h>
#include <ws2tcpip.h>
#ifdef __GNUC__
/* cygwin's headers do not seem to define these */
void WSAAPI freeaddrinfo (struct addrinfo*);
int WSAAPI getaddrinfo (const char*,const char*,const struct addrinfo*,
                        struct addrinfo**);
int WSAAPI getnameinfo(const struct sockaddr*,socklen_t,char*,DWORD,
                       char*,DWORD,int);
#endif
#endif

static GHashTable *inferior_by_pid = NULL;


MdbInferior *
MdbServer::GetInferiorByPid (int pid)
{
	return (MdbInferior *) g_hash_table_lookup (inferior_by_pid, GUINT_TO_POINTER (pid));
}

void
MdbServer::AddInferior (MdbInferior *inferior, int pid)
{
	g_hash_table_insert (inferior_by_pid, GUINT_TO_POINTER (pid), inferior);
}

void
MdbServer::SendEvent (ServerEvent *e)
{
	connection->SendEvent (e);
}

int
main (int argc, char *argv[])
{
	MdbServer *server;
	Connection *connection;
	struct sockaddr_in serv_addr, cli_addr;
	int conn_fd, fd, res;
	socklen_t cli_len;
	char buf[128];

	inferior_by_pid = g_hash_table_new (NULL, NULL);

	ServerObject::Initialize ();

	if (!MdbServer::Initialize ()) {
		g_warning (G_STRLOC ": Failed to initialize OS backend.");
		exit (-1);
	}

	MdbInferior::Initialize ();

	BreakpointManager::Initialize ();

	fd = socket (AF_INET, SOCK_STREAM, 0);
#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": %d", fd);
#endif

	memset (&serv_addr, 0, sizeof (serv_addr));

	serv_addr.sin_family = AF_INET;
	serv_addr.sin_addr.s_addr = INADDR_ANY;
	serv_addr.sin_port = htons (8888);

	res = bind (fd, (struct sockaddr *) &serv_addr, sizeof (serv_addr));
	if (res < 0) {
		g_warning (G_STRLOC ": bind() failed: %s", g_strerror (errno));
		exit (-1);
	}

	listen (fd, 1);

	cli_len = sizeof (cli_addr);

	conn_fd = accept (fd, (struct sockaddr *) &cli_addr, &cli_len);
	if (res < 0) {
		g_warning (G_STRLOC ": accept() failed: %s", g_strerror (errno));
		exit (-1);
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": accepted!");
#endif

	connection = new Connection (conn_fd);

	if (!connection->Setup ())
		exit (-1);

	/* 
	 * Set TCP_NODELAY on the socket so the client receives events/command
	 * results immediately.
	 */
	{
		int flag = 1;
		int result = setsockopt (conn_fd, IPPROTO_TCP, TCP_NODELAY, (char *) &flag, sizeof (int));
		g_assert (result >= 0);
		flag = 1;
		result = setsockopt (conn_fd, SOL_SOCKET, SO_REUSEADDR, (char *) &flag, sizeof (int));
		flag = 1;
	}

	server = new MdbServer (connection);

	server->MainLoop (conn_fd);

#if WINDOWS
	shutdown (fd, SD_BOTH);
	shutdown (conn_fd, SD_BOTH);

	closesocket (fd);
	closesocket (conn_fd);
#else
	shutdown (fd, SHUT_RDWR);
	shutdown (conn_fd, SHUT_RDWR);

	close (fd);
	close (conn_fd);
#endif

	exit (0);
}

gboolean
MdbServer::MainLoopIteration (void)
{
	return connection->HandleIncomingRequest (this);
}

MdbExeReader *
MdbServer::GetExeReader (const char *filename)
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
MdbServer::GetDisassembler (MdbInferior *inferior)
{
	return main_reader->GetDisassembler (inferior);
}
