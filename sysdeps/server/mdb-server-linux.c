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

static void
handle_wait_event (void)
{
	ServerHandle *server;
	ServerStatusMessageType message;
	guint64 arg, data1, data2;
	guint32 opt_data_size;
	gpointer opt_data;
	int pid, status;

	pid = waitpid (-1, &status, WUNTRACED | __WALL | __WCLONE | WNOHANG);
	if (pid < 0) {
		g_warning (G_STRLOC ": waitpid() failed: %s", g_strerror (errno));
		return;
	} else if (pid == 0)
		return;

	g_message (G_STRLOC ": waitpid(): %d - %x", pid, status);

	if (status >> 16) {
		switch (status >> 16) {
		case PTRACE_EVENT_CLONE: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
				return;
			}

			// *arg = new_pid;
			//return MESSAGE_CHILD_CREATED_THREAD;
			break;
		}

		case PTRACE_EVENT_FORK: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
				return;
			}

			// *arg = new_pid;
			//return MESSAGE_CHILD_FORKED;
			break;
		}

#if 0

		case PTRACE_EVENT_EXEC:
			return MESSAGE_CHILD_EXECD;

		case PTRACE_EVENT_EXIT: {
			int exitcode;

			if (ptrace (PTRACE_GETEVENTMSG, handle->inferior->pid, 0, &exitcode)) {
				g_warning (G_STRLOC ": %d - %s", handle->inferior->pid,
					   g_strerror (errno));
				return FALSE;
			}

			*arg = 0;
			return MESSAGE_CHILD_CALLED_EXIT;
		}
#endif

		default:
			g_warning (G_STRLOC ": Received unknown wait result %x on child %d",
				   status, pid);
			return;
		}
	}

	server = mdb_server_get_inferior_by_pid (pid);
	if (!server) {
		g_warning (G_STRLOC ": Got wait event for unknown pid: %d", pid);
		return;
	}

	message = mono_debugger_server_dispatch_event (
		server, status, &arg, &data1, &data2, &opt_data_size, &opt_data);

	mdb_server_process_child_event (
		message, pid, arg, data1, data2, opt_data_size, opt_data);
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
			read (self_pipe[0], &ret, 1);
			handle_wait_event ();
		}

		if (FD_ISSET (conn_fd, &readfds)) {
			if (!mdb_server_main_loop_iteration ())
				break;
		}
	}
}
