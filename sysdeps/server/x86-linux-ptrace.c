#if defined(__x86_64__)
#include "x86_64-arch.h"
#elif defined(__i386__)
#include "i386-arch.h"
#else
#error "Unknown architecture"
#endif

ServerType
mdb_server_get_server_type (void)
{
	return SERVER_TYPE_LINUX_PTRACE;
}

ServerCapabilities
mdb_server_get_capabilities (void)
{
	return SERVER_CAPABILITIES_THREAD_EVENTS | SERVER_CAPABILITIES_CAN_DETACH_ANY | SERVER_CAPABILIITES_HAS_SIGNALS;
}

static ServerCommandError
_server_ptrace_check_errno (ServerHandle *server)
{
	gchar *filename;

	if (!errno)
		return COMMAND_ERROR_NONE;
	else if (errno != ESRCH) {
		g_message (G_STRLOC ": %d - %s", server->inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	filename = g_strdup_printf ("/proc/%d/stat", server->inferior->pid);
	if (g_file_test (filename, G_FILE_TEST_EXISTS)) {
		g_free (filename);
		return COMMAND_ERROR_NOT_STOPPED;
	}

	g_warning (G_STRLOC ": %d - %s - %d (%s)", server->inferior->pid, filename,
		   errno, g_strerror (errno));
	g_free (filename);
	return COMMAND_ERROR_NO_TARGET;
}

ServerCommandError
mdb_inferior_make_memory_executable (ServerHandle *server, guint64 start, guint32 size)
{
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_ptrace_set_dr (ServerHandle *server, int regnum, guint64 value)
{
	errno = 0;
	ptrace (PTRACE_POKEUSER, server->inferior->pid, offsetof (struct user, u_debugreg [regnum]), value);
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", server->inferior->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}


static ServerCommandError
_ptrace_get_dr (ServerHandle *server, int regnum, guint64 *value)
{
	int ret;

	errno = 0;
	ret = ptrace (PTRACE_PEEKUSER, server->inferior->pid, offsetof (struct user, u_debugreg [regnum]));
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", server->inferior->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	*value = ret;
	return COMMAND_ERROR_NONE;
}

extern ServerCommandError
mdb_inferior_get_registers (ServerHandle *server, INFERIOR_REGS_TYPE *regs)
{
	ServerCommandError result;
	int i;

	if (ptrace (PT_GETREGS, server->inferior->pid, NULL, &regs->regs) != 0)
		return _server_ptrace_check_errno (server);

	if (ptrace (PT_GETFPREGS, server->inferior->pid, NULL, &regs->fpregs) != 0)
		return _server_ptrace_check_errno (server);

	result = _ptrace_get_dr (server, DR_CONTROL, &regs->dr_control);
	if (result)
		return result;

	result = _ptrace_get_dr (server, DR_STATUS, &regs->dr_status);
	if (result)
		return result;

	for (i = 0; i < DR_NADDR; i++) {
		result = _ptrace_get_dr (server, i, &regs->dr_regs[i]);
		if (result)
			return result;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_set_registers (ServerHandle *server, INFERIOR_REGS_TYPE *regs)
{
	ServerCommandError result;
	int i;

	if (ptrace (PT_SETREGS, server->inferior->pid, NULL, &regs->regs) != 0)
		return _server_ptrace_check_errno (server);

	if (ptrace (PT_SETFPREGS, server->inferior->pid, NULL, &regs->fpregs) != 0)
		return _server_ptrace_check_errno (server);

	result = _ptrace_set_dr (server, DR_CONTROL, regs->dr_control);
	if (result)
		return result;

	result = _ptrace_set_dr (server, DR_STATUS, regs->dr_status);
	if (result)
		return result;

	for (i = 0; i < DR_NADDR; i++) {
		result = _ptrace_set_dr (server, i, regs->dr_regs[i]);
		if (result)
			return result;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_read_memory (ServerHandle *server, guint64 start, guint32 size, gpointer buffer)
{
	guint8 *ptr = buffer;

	while (size) {
		int ret = pread64 (server->inferior->os.mem_fd, ptr, size, start);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			else if (errno == ESRCH)
				return COMMAND_ERROR_NOT_STOPPED;
			else if (errno == EIO)
				return COMMAND_ERROR_MEMORY_ACCESS;
			return COMMAND_ERROR_MEMORY_ACCESS;
		}

		size -= ret;
		ptr += ret;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_write_memory (ServerHandle *server, guint64 start,
			   guint32 size, gconstpointer buffer)
{
	ServerCommandError result;
	const long *ptr = buffer;
	guint64 addr = start;
	char temp [8];

	while (size >= sizeof (long)) {
		long word = *ptr++;

		errno = 0;
		if (ptrace (PT_WRITE_D, server->inferior->pid, GUINT_TO_POINTER (addr), word) != 0)
			return _server_ptrace_check_errno (server);

		addr += sizeof (long);
		size -= sizeof (long);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = mdb_inferior_read_memory (server, addr, sizeof (long), &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;

	memcpy (&temp, ptr, size);

	return mdb_inferior_write_memory (server, addr, sizeof (long), &temp);
}

ServerCommandError
mdb_inferior_poke_word (ServerHandle *server, guint64 addr, gsize value)
{
	errno = 0;
	if (ptrace (PT_WRITE_D, server->inferior->pid, GUINT_TO_POINTER (addr), value) != 0)
		return _server_ptrace_check_errno (server);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_continue (ServerHandle *server)
{
	errno = 0;
	server->inferior->stepping = FALSE;
	if (ptrace (PT_CONTINUE, server->inferior->pid, (caddr_t) 1, server->inferior->last_signal)) {
		return _server_ptrace_check_errno (server);
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_step (ServerHandle *server)
{
	errno = 0;
	server->inferior->stepping = TRUE;
	if (ptrace (PT_STEP, server->inferior->pid, (caddr_t) 1, server->inferior->last_signal))
		return _server_ptrace_check_errno (server);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_kill (ServerHandle *server)
{
	if (ptrace (PTRACE_KILL, server->inferior->pid, NULL, 0))
		return COMMAND_ERROR_UNKNOWN_ERROR;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *handle)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", handle->inferior->pid);

	// mdb_server_remove_hardware_breakpoints (handle);

	handle->inferior->os.mem_fd = open64 (filename, O_RDONLY);

	if (handle->inferior->os.mem_fd < 0) {
		if (errno == EACCES)
			return COMMAND_ERROR_PERMISSION_DENIED;

		g_warning (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	g_free (filename);
	return COMMAND_ERROR_NONE;
}

static void
_server_ptrace_finalize_inferior (ServerHandle *handle)
{
	close (handle->inferior->os.mem_fd);
	handle->inferior->os.mem_fd = -1;
}

static ServerCommandError
mdb_server_initialize_process (ServerHandle *handle)
{
	int flags = PTRACE_O_TRACECLONE | PTRACE_O_TRACEFORK | PTRACE_O_TRACEVFORK |
		PTRACE_O_TRACEEXEC;

	if (ptrace (PTRACE_SETOPTIONS, handle->inferior->pid, 0, flags)) {
		g_warning (G_STRLOC ": Can't PTRACE_SETOPTIONS %d: %s",
			   handle->inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out)
{
	SignalInfo *sinfo = g_new0 (SignalInfo, 1);

	sinfo->sigkill = SIGKILL;
	sinfo->sigstop = SIGSTOP;
	sinfo->sigint = SIGINT;
	sinfo->sigchld = SIGCHLD;

	sinfo->sigfpe = SIGFPE;
	sinfo->sigquit = SIGQUIT;
	sinfo->sigabrt = SIGABRT;
	sinfo->sigsegv = SIGSEGV;
	sinfo->sigill = SIGILL;
	sinfo->sigbus = SIGBUS;
	sinfo->sigwinch = SIGWINCH;

	/* __SIGRTMIN is the hard limit from the kernel, SIGRTMIN is the first
	 * user-visible real-time signal.  __SIGRTMIN and __SIGRTMIN+1 are used
	 * internally by glibc. */
	sinfo->kernel_sigrtmin = __SIGRTMIN;
#if defined(USING_MONO_FROM_TRUNK) || defined(MDB_SERVER)
	sinfo->mono_thread_abort = -1;
#else
	sinfo->mono_thread_abort = mono_thread_get_abort_signal ();
#endif

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
}

static void
mdb_server_global_init (void)
{
#ifndef MDB_SERVER
	_linux_wait_global_init ();
#endif
}

ServerCommandError
mdb_server_get_threads (ServerHandle *handle, guint32 *count, guint32 **threads)
{
	gchar *dirname = g_strdup_printf ("/proc/%d/task", handle->inferior->pid);
	const gchar *filename;
	GPtrArray *array;
	GDir *dir;
	int i;

	dir = g_dir_open (dirname, 0, NULL);
	if (!dir) {
		g_warning (G_STRLOC ": Can't get threads of %d", handle->inferior->pid);
		g_free (dirname);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	array = g_ptr_array_new ();

	while ((filename = g_dir_read_name (dir)) != NULL) {
		gchar *endptr;
		guint32 pid;

		pid = (guint32) strtol (filename, &endptr, 10);
		if (*endptr)
			goto out_error;

		g_ptr_array_add (array, GUINT_TO_POINTER (pid));
	}

	*count = array->len;
	*threads = g_new0 (guint32, array->len);

	for (i = 0; i < array->len; i++)
		(*threads) [i] = GPOINTER_TO_UINT (g_ptr_array_index (array, i));

	g_free (dirname);
	g_dir_close (dir);
	g_ptr_array_free (array, FALSE);
	return COMMAND_ERROR_NONE;

 out_error:
	g_free (dirname);
	g_dir_close (dir);
	g_ptr_array_free (array, FALSE);
	g_warning (G_STRLOC ": Can't get threads of %d", handle->inferior->pid);
	return COMMAND_ERROR_UNKNOWN_ERROR;
}

ServerCommandError
mdb_server_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			    guint32 *nargs, gchar ***cmdline_args)
{
	gchar *exe_filename = g_strdup_printf ("/proc/%d/exe", handle->inferior->pid);
	gchar *cwd_filename = g_strdup_printf ("/proc/%d/cwd", handle->inferior->pid);
	gchar *cmdline_filename = g_strdup_printf ("/proc/%d/cmdline", handle->inferior->pid);
	char buffer [BUFSIZ+1];
	GPtrArray *array;
	gchar *cmdline, **ptr;
	gsize pos, len;
	int i;

	len = readlink (exe_filename, buffer, BUFSIZ);
	if (len < 0) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);

		if (errno == EACCES)
			return COMMAND_ERROR_PERMISSION_DENIED;

		g_warning (G_STRLOC ": Can't get exe file of %d: %s", handle->inferior->pid,
			   g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	buffer [len] = 0;
	*exe_file = g_strdup (buffer);

	len = readlink (cwd_filename, buffer, BUFSIZ);
	if (len < 0) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get cwd of %d: %s", handle->inferior->pid,
			   g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	buffer [len] = 0;
	*cwd = g_strdup (buffer);

	if (!g_file_get_contents (cmdline_filename, &cmdline, &len, NULL)) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get cmdline args of %d", handle->inferior->pid);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	array = g_ptr_array_new ();

	pos = 0;
	while (pos < len) {
		g_ptr_array_add (array, cmdline + pos);
		pos += strlen (cmdline + pos) + 1;
	}

	*nargs = array->len;
	*cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (i = 0; i < array->len; i++)
		ptr  [i] = g_ptr_array_index (array, i);

	g_free (cwd_filename);
	g_free (exe_filename);
	g_free (cmdline_filename);
	g_ptr_array_free (array, FALSE);
	return COMMAND_ERROR_NONE;
}

#if !defined(MDB_SERVER)
#include "linux-wait.c"
#endif

#include "linux-ptrace.c"
