#include <debugger-mutex.h>
#include <sys/syscall.h>

static DebuggerMutex *wait_mutex;
static DebuggerMutex *wait_mutex_2;
static DebuggerMutex *wait_mutex_3;

void
_linux_wait_global_init (void)
{
	wait_mutex = debugger_mutex_new ();
	wait_mutex_2 = debugger_mutex_new ();
	wait_mutex_3 = debugger_mutex_new ();
}

static int
do_wait (int pid, int *status, gboolean nohang)
{
	int ret, flags;

#if DEBUG_WAIT
	g_message (G_STRLOC ": do_wait (%d%s)", pid, nohang ? ",nohang" : "");
#endif
	flags = WUNTRACED | __WALL | __WCLONE;
	if (nohang)
		flags |= WNOHANG;
	ret = waitpid (pid, status, flags);
#if DEBUG_WAIT
	g_message (G_STRLOC ": do_wait (%d) finished: %d - %x", pid, ret, *status);
#endif
	if (ret < 0) {
		if (errno == EINTR)
			return 0;
		else if (errno == ECHILD)
			return -1;
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", pid, g_strerror (errno));
		return -1;
	}

	return ret;
}

static int stop_requested = 0;
static int stop_status = 0;

guint32
mdb_server_global_wait (guint32 *status_ret)
{
	int ret, status;

 again:
	debugger_mutex_lock (wait_mutex);
	ret = do_wait (-1, &status, FALSE);
	if (ret <= 0)
		goto out;

#if DEBUG_WAIT
	g_message (G_STRLOC ": global wait finished: %d - %x", ret, status);
#endif

	debugger_mutex_lock (wait_mutex_2);

#if DEBUG_WAIT
	g_message (G_STRLOC ": global wait finished #1: %d - %x - %d",
		   ret, status, stop_requested);
#endif

	if (ret == stop_requested) {
		*status_ret = 0;
		stop_status = status;
		debugger_mutex_unlock (wait_mutex_2);
		debugger_mutex_unlock (wait_mutex);

		debugger_mutex_lock (wait_mutex_3);
		debugger_mutex_unlock (wait_mutex_3);
		goto again;
	}
	debugger_mutex_unlock (wait_mutex_2);

	*status_ret = status;
 out:
	debugger_mutex_unlock (wait_mutex);
	return ret;
}

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

	if (!debugger_mutex_trylock (wait_mutex)) {
		/* This should never happen, but let's not deadlock here. */
		g_warning (G_STRLOC ": Can't lock mutex: %d", handle->inferior->pid);
		return FALSE;
	}

	/*
	 * We own the `wait_mutex', so no other thread is currently waiting for the target
	 * and we can safely wait for it here.
	 */

	ret = waitpid (handle->inferior->pid, &status, WUNTRACED | __WALL | __WCLONE);

	/*
	 * Safety check: make sure we got the correct event.
	 */

	if ((ret != handle->inferior->pid) || !WIFSTOPPED (status) ||
	    ((WSTOPSIG (status) != SIGSTOP) && (WSTOPSIG (status) != SIGTRAP))) {
		g_warning (G_STRLOC ": Wait failed: %d, got pid %d, status %x", handle->inferior->pid, ret, status);
		debugger_mutex_unlock (wait_mutex);
		return FALSE;
	}

	/*
	 * Just as an extra safety check.
	 */

	if (mdb_arch_get_registers (handle) != COMMAND_ERROR_NONE) {
		debugger_mutex_unlock (wait_mutex);
		g_warning (G_STRLOC ": Failed to get registers: %d", handle->inferior->pid);
		return FALSE;
	}

	debugger_mutex_unlock (wait_mutex);
	return TRUE;
}

ServerCommandError
mdb_server_stop (ServerHandle *handle)
{
	ServerCommandError result;

	/*
	 * Try to get the thread's registers.  If we suceed, then it's already stopped
	 * and still alive.
	 */
	result = mdb_arch_get_registers (handle);
	if (result == COMMAND_ERROR_NONE)
		return COMMAND_ERROR_ALREADY_STOPPED;

	if (syscall (__NR_tkill, handle->inferior->pid, SIGSTOP)) {
		/*
		 * It's already dead.
		 */
		if (errno == ESRCH)
			return COMMAND_ERROR_NO_TARGET;
		else
			return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_stop_and_wait (ServerHandle *handle, guint32 *out_status)
{
	ServerCommandError result;
	gboolean already_stopped = FALSE;
	int ret;

	/*
	 * Try to get the thread's registers.  If we suceed, then it's already stopped
	 * and still alive.
	 */
#if DEBUG_WAIT
	g_message (G_STRLOC ": stop and wait %d", handle->inferior->pid);
#endif
	debugger_mutex_lock (wait_mutex_2);
	result = mdb_server_stop (handle);
	if (result == COMMAND_ERROR_ALREADY_STOPPED) {
#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - already stopped", handle->inferior->pid);
#endif
		already_stopped = TRUE;
	} else if (result != COMMAND_ERROR_NONE) {
#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - cannot stop %d", handle->inferior->pid, result);
#endif

		debugger_mutex_unlock (wait_mutex_2);
		return result;
	}

	debugger_mutex_lock (wait_mutex_3);

	stop_requested = handle->inferior->pid;
	debugger_mutex_unlock (wait_mutex_2);

#if DEBUG_WAIT
	if (!already_stopped)
		g_message (G_STRLOC ": %d - sent SIGSTOP", handle->inferior->pid);
#endif

	debugger_mutex_lock (wait_mutex);
#if DEBUG_WAIT
	g_message (G_STRLOC ": %d - got stop status %x", handle->inferior->pid, stop_status);
#endif
	if (stop_status) {
		*out_status = stop_status;
		stop_requested = stop_status = 0;
		debugger_mutex_unlock (wait_mutex);
		debugger_mutex_unlock (wait_mutex_3);
		return COMMAND_ERROR_NONE;
	}

	stop_requested = stop_status = 0;

	do {
		int status;

#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - waiting", handle->inferior->pid);
#endif
		ret = do_wait (handle->inferior->pid, &status, already_stopped);
#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - done waiting %d, %x",
			   handle->inferior->pid, ret, status);
#endif

		*out_status = status;
	} while (ret == 0);
	debugger_mutex_unlock (wait_mutex);
	debugger_mutex_unlock (wait_mutex_3);

	/*
	 * Should never happen.
	 */
	if (ret < 0)
		return COMMAND_ERROR_NO_TARGET;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_detach_after_fork (ServerHandle *handle)
{
	ServerCommandError result;
	GPtrArray *breakpoints;
	int ret, status, i;

	ret = waitpid (handle->inferior->pid, &status, WUNTRACED | WNOHANG | __WALL | __WCLONE);
	if (ret < 0)
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", handle->inferior->pid, g_strerror (errno));

	/*
	 * Make sure we're stopped.
	 */
	if (mdb_arch_get_registers (handle) != COMMAND_ERROR_NONE)
		do_wait (handle->inferior->pid, &status, FALSE);

	result = mdb_arch_get_registers (handle);
	if (result != COMMAND_ERROR_NONE)
		return result;

	mono_debugger_breakpoint_manager_lock ();

	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		mdb_arch_disable_breakpoint (handle, info);
	}

	mono_debugger_breakpoint_manager_unlock ();

	if (ptrace (PTRACE_DETACH, handle->inferior->pid, NULL, NULL) != 0)
		return _server_ptrace_check_errno (handle);

	return COMMAND_ERROR_NONE;
}
