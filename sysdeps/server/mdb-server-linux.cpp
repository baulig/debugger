#include <mdb-server-linux.h>
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

MdbServer *
mdb_server_new (Connection *connection)
{
	return new MdbServerLinux (connection);
}

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

bool
MdbServer::Initialize (void)
{
	int res;

	sigemptyset (&sigmask);
	sigaddset (&sigmask, SIGCHLD);

	res = sigprocmask (SIG_BLOCK, &sigmask, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigprocmask() failed: %d - %s", res, g_strerror (errno));
		return false;
	}

	pipe (self_pipe);
	fcntl (self_pipe [0], F_SETFL, O_NONBLOCK);
	fcntl (self_pipe [1], F_SETFL, O_NONBLOCK);

	pthread_create (&wait_thread, NULL, (void * (*) (void *)) wait_thread_main, NULL);

	bfd_init ();

	return true;
}

void
MdbServerLinux::MainLoop (int conn_fd)
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

			HandleLinuxWaitEvent ();
		}

		if (FD_ISSET (conn_fd, &readfds)) {
			if (!MainLoopIteration ()) {
				break;
			}
		}
	}
}

bool
MdbServerLinux::HandleExtendedWaitEvent (MdbProcess *process, int pid, int status)
{
	if ((status >> 16) == 0)
		return false;

	switch (status >> 16) {
	case PTRACE_EVENT_CLONE: {
		MdbInferior *new_inferior;
		gsize new_pid;
		int ret, status;
		ServerEvent *e;

		if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &new_pid)) {
			g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
			return true;
		}

		new_inferior = GetInferiorByThreadId (new_pid);

		//
		// We already got a stop event for this new thread.
		//
		if (new_inferior)
			return true;

		new_inferior = CreateThread (process, new_pid, false);

		e = g_new0 (ServerEvent, 1);
		e->type = SERVER_EVENT_THREAD_CREATED;
		e->sender = process;
		e->arg_object = new_inferior;
		e->arg = new_pid;
		SendEvent (e);
		g_free (e);

		return true;
	}

	case PTRACE_EVENT_FORK: {
		gsize new_pid;
		ServerEvent *e;

		if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &new_pid)) {
			g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
			return false;
		}

		e = g_new0 (ServerEvent, 1);
		e->type = SERVER_EVENT_FORKED;
		e->sender = process;
		e->arg = new_pid;
		SendEvent (e);
		g_free (e);
		return true;
	}

	case PTRACE_EVENT_EXEC: {
		ServerEvent *e = g_new0 (ServerEvent, 1);

		e->type = SERVER_EVENT_EXECD;
		e->sender = process;
		SendEvent (e);
		g_free (e);
		return true;
	}

	case PTRACE_EVENT_EXIT: {
		gsize exitcode;
		ServerEvent *e;

		if (ptrace (PTRACE_GETEVENTMSG, pid, 0, &exitcode)) {
			g_warning (G_STRLOC ": %d - %s", pid, g_strerror (errno));
			return true;
		}

		e = g_new0 (ServerEvent, 1);
		e->type = SERVER_EVENT_CALLED_EXIT;
		e->sender = process;
		e->arg = exitcode;
		SendEvent (e);
		g_free (e);
		return true;
	}

	default:
		g_warning (G_STRLOC ": Received unknown wait result %x on child %d", status, pid);
		return true;
	}
}

void
MdbServerLinux::HandleLinuxWaitEvent (void)
{
	MdbInferior *inferior;
	MdbProcess *process;
	int pid, status;
	ServerEvent *e;

#if DEBUG_WAIT
	g_message (G_STRLOC ": HandleLinuxWaitEvent()");
#endif

	pid = waitpid (-1, &status, WUNTRACED | __WALL | __WCLONE | WNOHANG);
#if DEBUG_WAIT
	g_message (G_STRLOC ": HandleLinuxWaitEvent () - %d - %x", pid, status);
#endif

	if (pid < 0) {
		g_warning (G_STRLOC ": waitpid() failed: %s", g_strerror (errno));
		return;
	} else if (pid == 0)
		return;

	inferior = GetInferiorByThreadId (pid);

	if (!inferior) {
		//
		// There's a race condition in the Linux kernel which makes it sometimes
		// emit the first stop event for a new thread before the corresponding
		// PTRACE_EVENT_CLONE.
		//

		process = GetMainProcess ();

		if (WIFSTOPPED (status)) {
			ServerEvent *e;

			inferior = CreateThread (process, pid, true);

			e = g_new0 (ServerEvent, 1);
			e->sender = process;
			e->type = SERVER_EVENT_THREAD_CREATED;
			e->arg_object = inferior;
			e->arg = pid;

			SendEvent (e);
			g_free (e);
			return;
		}

		g_warning (G_STRLOC ": Got wait event %x for unknown pid: %d", status, pid);
		return;
	}

	process = inferior->GetProcess ();

	if (HandleExtendedWaitEvent (process, pid, status)) {
#if DEBUG_WAIT
		g_message (G_STRLOC ": extended event => %d - %x - %p", pid, status, inferior);
#endif
		if (inferior->Continue ()) {
			g_warning (G_STRLOC ": Can't resume after extended wait event.");
		}
		return;
	}

#if DEBUG_WAIT
	g_message (G_STRLOC ": normal event => %d - %x - %p", pid, status, inferior);
#endif

	e = inferior->HandleLinuxWaitEvent (status);
#if DEBUG_WAIT
	g_message (G_STRLOC ": normal event => %d - %x - %p - %p", pid, status, inferior, e);
#endif
	if (e) {
		SendEvent (e);
		g_free (e);
	}
}
