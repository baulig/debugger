#include <mdb-server.h>
#include <errno.h>
#include <unistd.h>
#include <signal.h>
#include <string.h>
#include <fcntl.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <sys/wait.h>
#include <pthread.h>
#include <bfd.h>

#include "linux-ptrace.h"

static sigset_t sigmask;
static pthread_t wait_thread;
static int self_pipe[2];

static void
wait_thread_main (void)
{
	while (TRUE) {
		int sig;

		sigwait (&sigmask, &sig);
		write (self_pipe[1], &sig, 1);
	}
}

int
mdb_server_init_os (void)
{
	int res;

	sigemptyset (&sigmask);
	sigaddset (&sigmask, SIGCHLD);

	res = sigprocmask (SIG_BLOCK, &sigmask, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigprocmask() failed: %d - %s", res, g_strerror (errno));
		return -1;
	}

	pipe (self_pipe);
	fcntl (self_pipe [0], F_SETFL, O_NONBLOCK);
	fcntl (self_pipe [1], F_SETFL, O_NONBLOCK);

	pthread_create (&wait_thread, NULL, (void * (*) (void *)) wait_thread_main, NULL);

	bfd_init ();

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
		FD_SET (self_pipe[0], &readfds);
		nfds = MAX (conn_fd, self_pipe[0]) + 1;

#ifdef TRANSPORT_DEBUG
		g_message (G_STRLOC ": select()");
#endif

		ret = select (nfds, &readfds, NULL, NULL, NULL);

#ifdef TRANSPORT_DEBUG
		g_message (G_STRLOC ": select() returned: %d - %d/%d", ret,
			   FD_ISSET (self_pipe[0], &readfds), FD_ISSET (conn_fd, &readfds));
#endif

		if (FD_ISSET (self_pipe[0], &readfds)) {
			ServerEvent *e;

			read (self_pipe[0], &ret, 1);

			e = mdb_server_handle_wait_event ();
			if (e) {
				mdb_server_process_child_event (e);
				g_free (e);
			}
		}

		if (FD_ISSET (conn_fd, &readfds)) {
			if (!mdb_server_main_loop_iteration ())
				break;
		}
	}
}
