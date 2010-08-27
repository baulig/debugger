#include <mdb-server.h>
#include <mdb-inferior.h>
#include <errno.h>
#include <unistd.h>
#include <signal.h>
#include <string.h>
#include <fcntl.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <sys/ptrace.h>
#include <sys/wait.h>
#include <pthread.h>
#include <bfd.h>

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

gboolean
MdbServer::Initialize (void)
{
	int res;

	sigemptyset (&sigmask);
	sigaddset (&sigmask, SIGCHLD);

	res = sigprocmask (SIG_BLOCK, &sigmask, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigprocmask() failed: %d - %s", res, g_strerror (errno));
		return FALSE;
	}

	pipe (self_pipe);
	fcntl (self_pipe [0], F_SETFL, O_NONBLOCK);
	fcntl (self_pipe [1], F_SETFL, O_NONBLOCK);

	pthread_create (&wait_thread, NULL, (void * (*) (void *)) wait_thread_main, NULL);

	bfd_init ();

	return TRUE;
}

void
MdbServer::MainLoop (void)
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

			e = HandleLinuxWaitEvent ();
			if (e) {
				ProcessChildEvent (e);
				g_free (e);
			}
		}

		if (FD_ISSET (conn_fd, &readfds)) {
		  if (!MainLoopIteration ())
				break;
		}
	}
}

static ServerEvent *
handle_extended_event (int pid, int status)
{
	ServerEvent *e;

	if ((status >> 16) == 0)
		return NULL;

	e = g_new0 (ServerEvent, 1);

	switch (status >> 16) {
	case PTRACE_EVENT_CLONE: {
		int new_pid;

		if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &new_pid)) {
			g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
			e->type = SERVER_EVENT_UNKNOWN_ERROR;
			return e;
		}

		e->type = SERVER_EVENT_CHILD_CREATED_THREAD;
		e->arg = new_pid;
		return e;
	}

	case PTRACE_EVENT_FORK: {
		int new_pid;

		if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &new_pid)) {
			g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
			e->type = SERVER_EVENT_UNKNOWN_ERROR;
			return e;
		}

		e->type = SERVER_EVENT_CHILD_FORKED;
		e->arg = new_pid;
		return e;
	}

	case PTRACE_EVENT_EXEC: {
		e = g_new0 (ServerEvent, 1);
		e->type = SERVER_EVENT_CHILD_EXECD;
		return e;
	}

	case PTRACE_EVENT_EXIT: {
		int exitcode;

		if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &exitcode)) {
			g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
			e->type = SERVER_EVENT_UNKNOWN_ERROR;
			return e;
		}

		e->type = SERVER_EVENT_CHILD_CALLED_EXIT;
		e->arg = exitcode;
		return e;
	}

	default:
		g_warning (G_STRLOC ": Received unknown wait result %x on child %d", status, pid);
		e->type = SERVER_EVENT_UNKNOWN_ERROR;
		e->arg = pid;
		return e;
	}
}

ServerEvent *
MdbServer::HandleLinuxWaitEvent (void)
{
	ServerEvent *e;
	MdbInferior *inferior;
	int pid, status;

	pid = waitpid (-1, &status, WUNTRACED | __WALL | __WCLONE | WNOHANG);
	if (pid < 0) {
		g_warning (G_STRLOC ": waitpid() failed: %s", g_strerror (errno));
		return NULL;
	} else if (pid == 0)
		return NULL;

	e = handle_extended_event (pid, status);
	if (e)
		return e;

	inferior = GetInferiorByPid (pid);
	if (!inferior) {
		g_warning (G_STRLOC ": Got wait event for unknown pid: %d", pid);
		return NULL;
	}

	return inferior->HandleLinuxWaitEvent (status);
}

