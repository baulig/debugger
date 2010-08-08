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

static ServerEvent *
handle_inferior_event (ServerHandle *server, int status)
{
	ServerEvent *e;

	if (WIFSTOPPED (status)) {
#if __MACH__
		server->inferior->os.wants_to_run = FALSE;
#endif
		int stopsig;

		stopsig = WSTOPSIG (status);
		if (stopsig == SIGCONT)
			stopsig = 0;

		if (stopsig == SIGSTOP) {
			e->type = SERVER_EVENT_CHILD_INTERRUPTED;
			return e;
		}

		return mdb_arch_child_stopped (server, stopsig);
	} else if (WIFEXITED (status)) {
		e = g_new0 (ServerEvent, 1);
		e->sender_iid = server->iid;

		e->type = SERVER_EVENT_CHILD_EXITED;
		e->arg = WEXITSTATUS (status);
		return e;
	} else if (WIFSIGNALED (status)) {
		e = g_new0 (ServerEvent, 1);
		e->sender_iid = server->iid;

		if ((WTERMSIG (status) == SIGTRAP) || (WTERMSIG (status) == SIGKILL)) {
			e->type = SERVER_EVENT_CHILD_EXITED;
			e->arg = 0;
			return e;
		} else {
			e->type = SERVER_EVENT_CHILD_SIGNALED;
			e->arg = WTERMSIG (status);
			return e;
		}
	}

	g_warning (G_STRLOC ": Got unknown waitpid() result: %x", status);

	e = g_new0 (ServerEvent, 1);
	e->sender_iid = server->iid;

	e->type = SERVER_EVENT_UNKNOWN_ERROR;
	e->arg = status;
	return e;
}

#ifndef MDB_SERVER

static ServerEventType
mdb_server_dispatch_event (ServerHandle *server, guint32 status, guint64 *arg,
			   guint64 *data1, guint64 *data2, guint32 *opt_data_size,
			   gpointer *opt_data)
{
	ServerEvent *e;
	ServerEventType type;

	e = handle_extended_event (server->inferior->pid, status);
	if (!e)
		e = handle_inferior_event (server, status);

	if (!e) {
		g_warning (G_STRLOC ": Got unknown waitpid() result: %x", status);
		return SERVER_EVENT_UNKNOWN_ERROR;
	}

	*arg = e->arg;
	*data1 = e->data1;
	*data2 = e->data2;
	*opt_data_size = e->opt_data_size;
	*opt_data = e->opt_data;

	type = e->type;
	g_free (e);
	return type;
}

static ServerEventType
mdb_server_dispatch_simple (guint32 status, guint32 *arg)
{
	if (status >> 16)
		return SERVER_EVENT_UNKNOWN_ERROR;

	if (WIFSTOPPED (status)) {
		int stopsig = WSTOPSIG (status);

		if ((stopsig == SIGSTOP) || (stopsig == SIGTRAP))
			stopsig = 0;

		*arg = stopsig;
		return SERVER_EVENT_CHILD_STOPPED;
	} else if (WIFEXITED (status)) {
		*arg = WEXITSTATUS (status);
		return SERVER_EVENT_CHILD_EXITED;
	} else if (WIFSIGNALED (status)) {
		if ((WTERMSIG (status) == SIGTRAP) || (WTERMSIG (status) == SIGKILL)) {
			*arg = 0;
			return SERVER_EVENT_CHILD_EXITED;
		} else {
			*arg = WTERMSIG (status);
			return SERVER_EVENT_CHILD_SIGNALED;
		}
	}

	return SERVER_EVENT_UNKNOWN_ERROR;
}

#endif

ServerEvent *
mdb_server_handle_wait_event (void)
{
	ServerEvent *e;
	ServerHandle *server;
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

	server = mdb_server_get_inferior_by_pid (pid);
	if (!server) {
		g_warning (G_STRLOC ": Got wait event for unknown pid: %d", pid);
		return NULL;
	}

	return handle_inferior_event (server, status);
}

#ifdef MDB_SERVER

gboolean
_server_ptrace_wait_for_new_thread (ServerHandle *handle)
{
	guint32 ret = 0;
	int status;

	/*
	 * There is a race condition in the Linux kernel which shows up on >= 2.6.27:
	 *
	 * When creating a new thread, the initial stopping event of that thread is sometimes
	 * sent before sending the `PTRACE_EVENT_CLONE' for it.
	 *
	 * Because of this, we wait here until the new thread has been stopped and ignore
	 * any "early" stopping events.
	 *
	 * See also bugs #423518 and #466012.
.	 *
	 */

	ret = waitpid (handle->inferior->pid, &status, WUNTRACED | __WALL | __WCLONE);

	/*
	 * Safety check: make sure we got the correct event.
	 */

	if ((ret != handle->inferior->pid) || !WIFSTOPPED (status) ||
	    ((WSTOPSIG (status) != SIGSTOP) && (WSTOPSIG (status) != SIGTRAP))) {
		g_warning (G_STRLOC ": Wait failed: %d, got pid %d, status %x", handle->inferior->pid, ret, status);
		return FALSE;
	}

	/*
	 * Just as an extra safety check.
	 */

	if (mdb_arch_get_registers (handle) != COMMAND_ERROR_NONE) {
		g_warning (G_STRLOC ": Failed to get registers: %d", handle->inferior->pid);
		return FALSE;
	}

	return TRUE;
}

#endif
