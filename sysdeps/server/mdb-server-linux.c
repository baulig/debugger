#include <mdb-server.h>
#include <errno.h>
#include <unistd.h>
#include <string.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <sys/wait.h>
#include <bfd.h>

static volatile sig_atomic_t got_SIGCHLD = 0;

static sigset_t sigmask, empty_mask;

static void
child_sig_handler (int sig)
{
	got_SIGCHLD = 1;
}

int
mdb_server_init_os (void)
{
	struct sigaction sa;
	int res;

	sigemptyset (&sigmask);
	sigaddset (&sigmask, SIGCHLD);

	res = sigprocmask (SIG_BLOCK, &sigmask, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigprocmask() failed!");
		return -1;
	}

	sa.sa_flags = 0;
	sa.sa_handler = child_sig_handler;
	sigemptyset (&sa.sa_mask);

	res = sigaction (SIGCHLD, &sa, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigaction() failed!");
		return -1;
	}

	sigemptyset (&empty_mask);

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
	int ret, status;

	ret = waitpid (-1, &status, WUNTRACED | __WALL | __WCLONE | WNOHANG);
	if (ret < 0) {
		g_warning (G_STRLOC ": waitpid() failed: %s", g_strerror (errno));
		return;
	} else if (ret == 0)
		return;

	g_message (G_STRLOC ": waitpid(): %d - %x", ret, status);

#ifdef PTRACE_EVENT_CLONE
	if (status >> 16) {
		switch (status >> 16) {
		case PTRACE_EVENT_CLONE: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, ret, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", ret, g_strerror (errno));
				return FALSE;
			}

			// *arg = new_pid;
			//return MESSAGE_CHILD_CREATED_THREAD;
			break;
		}

		case PTRACE_EVENT_FORK: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, ret, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", ret, g_strerror (errno));
				return FALSE;
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

		default:
			g_warning (G_STRLOC ": Received unknown wait result %x on child %d",
				   status, handle->inferior->pid);
			return MESSAGE_UNKNOWN_ERROR;
		}
#endif
	}
#endif

	server = mdb_server_get_inferior_by_pid (ret);
	if (!server) {
		g_warning (G_STRLOC ": Got wait event for unknown pid: %d", ret);
		return;
	}

	message = mono_debugger_server_dispatch_event (
		server, status, &arg, &data1, &data2, &opt_data_size, &opt_data);

	g_message (G_STRLOC ": dispatched child event: %d / %d - %Ld, %Lx, %Lx",
		   message, ret, arg, data1, data2);


	mdb_server_process_child_event (
		message, ret, arg, data1, data2, opt_data_size, opt_data);
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

		g_message (G_STRLOC ": pselect()");
		ret = pselect (nfds, &readfds, NULL, NULL, NULL, &empty_mask);
		g_message (G_STRLOC ": pselect() returned: %d - %d", ret, got_SIGCHLD);

		if (got_SIGCHLD) {
			got_SIGCHLD = 0;
			handle_wait_event ();
		}

		if (FD_ISSET (conn_fd, &readfds)) {
			if (!mdb_server_main_loop_iteration ())
				break;
		}
	}
}
