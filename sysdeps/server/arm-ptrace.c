#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#define DEBUG_WAIT 1

#include <config.h>
#include <server.h>
#include <breakpoints.h>
#include <mdb-server.h>
#include <dis-asm.h>
#include <stdio.h>
#include <stdlib.h>
#include <pthread.h>
#include <sys/stat.h>
#include <sys/poll.h>
#include <sys/wait.h>
#include <sys/ptrace.h>
#include <sys/resource.h>
#include <signal.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

#include "linux-ptrace.h"

struct _InferiorRegsType {
	struct pt_regs regs;
};

struct InferiorHandle
{
	guint32 pid;
	int stepping;
	int last_signal;
	int redirect_fds;
	int output_fd [2], error_fd [2];
	int is_thread;
	INFERIOR_REGS_TYPE current_regs;
	struct disassemble_info *disassembler;
	char disasm_buffer [1024];
	MdbExeReader *main_bfd;
};

struct IOThreadData
{
	int output_fd, error_fd;
};

ArchType
mdb_server_get_arch_type (void)
{
	return ARCH_TYPE_I386;
}

static void
mdb_server_global_init (void)
{
}

static ServerHandle *
mdb_server_create_inferior (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	if ((getuid () == 0) || (geteuid () == 0)) {
		g_message ("WARNING: Running mdb as root may be a problem because setuid() and\n"
			   "seteuid() do nothing.\n"
			   "See http://primates.ximian.com/~martin/blog/entry_150.html for details.");
	}

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);

	return handle;
}

static void
child_setup_func (InferiorHandle *inferior)
{
	if (ptrace (PTRACE_TRACEME, getpid (), NULL, 0))
		g_error (G_STRLOC ": Can't PTRACE_TRACEME: %s", g_strerror (errno));

	if (inferior->redirect_fds) {
		dup2 (inferior->output_fd[1], 1);
		dup2 (inferior->error_fd[1], 2);
	}
}

ServerCommandError
mdb_server_spawn (ServerHandle *handle, const gchar *working_directory,
		  const gchar **argv, const gchar **envp, gboolean redirect_fds,
		  gint *child_pid, IOThreadData **io_data, gchar **error)
{	
	InferiorHandle *inferior = handle->inferior;
	int fd[2], ret, len, i;

	if (error)
		*error = NULL;
	inferior->redirect_fds = redirect_fds;

	if (redirect_fds) {
		pipe (inferior->output_fd);
		pipe (inferior->error_fd);

		*io_data = g_new0 (IOThreadData, 1);
		(*io_data)->output_fd = inferior->output_fd[0];
		(*io_data)->error_fd = inferior->error_fd[0];
	} else {
		if (io_data)
			*io_data = NULL;
	}

	pipe (fd);

	*child_pid = fork ();
	if (*child_pid == 0) {
		gchar *error_message;
		struct rlimit core_limit;
		int open_max;

		open_max = sysconf (_SC_OPEN_MAX);
		for (i = 3; i < open_max; i++)
			fcntl (i, F_SETFD, FD_CLOEXEC);

		setsid ();

		getrlimit (RLIMIT_CORE, &core_limit);
		core_limit.rlim_cur = 0;
		setrlimit (RLIMIT_CORE, &core_limit);

		child_setup_func (inferior);
		execve (argv [0], (char **) argv, (char **) envp);

		error_message = g_strdup_printf ("Cannot exec `%s': %s", argv [0], g_strerror (errno));
		len = strlen (error_message) + 1;
		write (fd [1], &len, sizeof (len));
		write (fd [1], error_message, len);
		_exit (1);
	} else if (*child_pid < 0) {
		if (redirect_fds) {
			close (inferior->output_fd[0]);
			close (inferior->output_fd[1]);
			close (inferior->error_fd[0]);
			close (inferior->error_fd[1]);
		}
		close (fd [0]);
		close (fd [1]);

		if (error)
			*error = g_strdup_printf ("fork() failed: %s", g_strerror (errno));
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	if (redirect_fds) {
		close (inferior->output_fd[1]);
		close (inferior->error_fd[1]);
	}
	close (fd [1]);

	ret = read (fd [0], &len, sizeof (len));

	if (ret != 0) {
		g_assert (ret == 4);

		if (error) {
			*error = g_malloc0 (len);
			read (fd [0], *error, len);
		}
		close (fd [0]);
		if (redirect_fds) {
			close (inferior->output_fd[0]);
			close (inferior->error_fd[0]);
		}
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	close (fd [0]);

	inferior->pid = *child_pid;

	if (!_server_ptrace_wait_for_new_thread (handle))
		return COMMAND_ERROR_INTERNAL_ERROR;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_initialize_thread (ServerHandle *handle, guint32 pid, gboolean do_wait)
{
	InferiorHandle *inferior = handle->inferior;

	inferior->is_thread = TRUE;

	inferior->pid = pid;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_attach (ServerHandle *handle, guint32 pid)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PTRACE_ATTACH, pid, NULL, 0) != 0) {
		g_warning (G_STRLOC ": Can't attach to %d - %s", pid,
			   g_strerror (errno));
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	inferior->pid = pid;
	inferior->is_thread = TRUE;

	return COMMAND_ERROR_NONE;
}

static void
process_output (int fd, gboolean is_stderr, ChildOutputFunc func)
{
	char buffer [BUFSIZ + 2];
	int count;

	count = read (fd, buffer, BUFSIZ);
	if (count < 0)
		return;

	buffer [count] = 0;
	func (is_stderr, buffer);
}

static void
_server_ptrace_io_thread_main (IOThreadData *io_data, ChildOutputFunc func)
{
	struct pollfd fds [2];
	int ret;

	fds [0].fd = io_data->output_fd;
	fds [0].events = POLLIN | POLLHUP | POLLERR;
	fds [0].revents = 0;
	fds [1].fd = io_data->error_fd;
	fds [1].events = POLLIN | POLLHUP | POLLERR;
	fds [1].revents = 0;

	while (1) {
		ret = poll (fds, 2, -1);

		if ((ret < 0) && (errno != EINTR))
			break;

		if (fds [0].revents & POLLIN)
			process_output (io_data->output_fd, FALSE, func);
		if (fds [1].revents & POLLIN)
			process_output (io_data->error_fd, TRUE, func);

		if ((fds [0].revents & (POLLHUP | POLLERR))
		    || (fds [1].revents & (POLLHUP | POLLERR)))
			break;
	}

	close (io_data->output_fd);
	close (io_data->error_fd);
	g_free (io_data);
}

static ServerCommandError
mdb_server_set_signal (ServerHandle *handle, guint32 sig, guint32 send_it)
{
	if (send_it)
		kill (handle->inferior->pid, sig);
	else
		handle->inferior->last_signal = sig;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_get_pending_signal (ServerHandle *handle, guint32 *out_signal)
{
	*out_signal = handle->inferior->last_signal;
	return COMMAND_ERROR_NONE;
}

static void
mdb_server_set_runtime_info (ServerHandle *handle, MonoRuntimeInfo *mono_runtime)
{
	handle->mono_runtime = mono_runtime;
}

static guint32
mdb_server_get_current_pid (void)
{
	return getpid ();
}

static guint64
mdb_server_get_current_thread (void)
{
	return pthread_self ();
}

void
_mdb_inferior_set_last_signal (InferiorHandle *inferior, int last_signal)
{
	inferior->last_signal = last_signal;
}

int
_mdb_inferior_get_last_signal (InferiorHandle *inferior)
{
	return inferior->last_signal;
}

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
_server_ptrace_check_errno (InferiorHandle *inferior)
{
	gchar *filename;

	if (!errno)
		return COMMAND_ERROR_NONE;
	else if (errno != ESRCH) {
		g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	filename = g_strdup_printf ("/proc/%d/stat", inferior->pid);
	if (g_file_test (filename, G_FILE_TEST_EXISTS)) {
		g_free (filename);
		return COMMAND_ERROR_NOT_STOPPED;
	}

	g_warning (G_STRLOC ": %d - %s - %d (%s)", inferior->pid, filename,
		   errno, g_strerror (errno));
	g_free (filename);
	return COMMAND_ERROR_NO_TARGET;
}

ServerCommandError
mdb_inferior_make_memory_executable (InferiorHandle *inferior, guint64 start, guint32 size)
{
	return COMMAND_ERROR_NONE;
}

extern ServerCommandError
mdb_inferior_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PTRACE_GETREGS, inferior->pid, NULL, &regs->regs) != 0)
		return _server_ptrace_check_errno (inferior);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PTRACE_SETREGS, inferior->pid, NULL, &regs->regs) != 0)
		return _server_ptrace_check_errno (inferior);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_registers (ServerHandle *server, guint64 *values)
{
	int i;

	for (i = 0; i < 18; i++)
		values [i] = server->inferior->current_regs.regs.uregs [i];

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_set_registers (ServerHandle *server, guint64 *values)
{
	int i;

	for (i = 0; i < 18; i++)
		server->inferior->current_regs.regs.uregs [i] = values [i];

	return mdb_inferior_set_registers (server->inferior, &server->inferior->current_regs);
}

ServerCommandError
mdb_inferior_read_memory (InferiorHandle *inferior, guint64 start, guint32 size, gpointer buffer)
{
	ServerCommandError result;
	long *ptr = buffer;
	gsize addr = start;
	char temp [8];

	while (size >= sizeof (long)) {
		errno = 0;
		*ptr++ = ptrace (PTRACE_PEEKDATA, inferior->pid, GINT_TO_POINTER (addr), NULL);
		if (errno) {
			g_message (G_STRLOC ": peek failed!");
			return _server_ptrace_check_errno (inferior);
		}

		addr += sizeof (long);
		size -= sizeof (long);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = mdb_inferior_read_memory (inferior, addr, sizeof (long), &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;

	memcpy (&temp, ptr, size);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_write_memory (InferiorHandle *inferior, guint64 start,
			   guint32 size, gconstpointer buffer)
{
	ServerCommandError result;
	const long *ptr = buffer;
	gsize addr = start;
	char temp [8];

	while (size >= sizeof (long)) {
		long word = *ptr++;

		errno = 0;
		if (ptrace (PTRACE_POKEDATA, inferior->pid, GINT_TO_POINTER (addr), GINT_TO_POINTER (word)) != 0)
			return _server_ptrace_check_errno (inferior);

		addr += sizeof (long);
		size -= sizeof (long);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = mdb_inferior_read_memory (inferior, addr, sizeof (long), &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;

	memcpy (&temp, ptr, size);

	return mdb_inferior_write_memory (inferior, addr, sizeof (long), &temp);
}

ServerCommandError
mdb_inferior_poke_word (InferiorHandle *inferior, guint64 addr, gsize value)
{
	errno = 0;
	if (ptrace (PTRACE_POKEDATA, inferior->pid, GINT_TO_POINTER ((gsize) addr), GINT_TO_POINTER (value)) != 0)
		return _server_ptrace_check_errno (inferior);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_peek_word (ServerHandle *server, guint64 start, guint64 *retval)
{
	gsize word;

	errno = 0;
	if (ptrace (PTRACE_POKEDATA, server->inferior->pid, GINT_TO_POINTER ((gsize) start), &word) != 0)
		return _server_ptrace_check_errno (server->inferior);

	*retval = word;
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_read_memory (ServerHandle *server, guint64 start, guint32 size, gpointer buffer)
{
	ServerCommandError result;

	result = mdb_inferior_read_memory (server->inferior, start, size, buffer);
	if (result)
		return result;

	// mdb_server_remove_breakpoints_from_target_memory (server, start, size, buffer);
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer buffer)
{
	return mdb_inferior_write_memory (handle->inferior, start, size, buffer);
}

ServerCommandError
mdb_server_continue (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	errno = 0;
	inferior->stepping = FALSE;
	if (ptrace (PTRACE_CONT, inferior->pid, (caddr_t) 1, GINT_TO_POINTER (inferior->last_signal))) {
		return _server_ptrace_check_errno (inferior);
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_step (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	errno = 0;
	inferior->stepping = TRUE;
	if (ptrace (PTRACE_SINGLESTEP, inferior->pid, (caddr_t) 1, GINT_TO_POINTER (inferior->last_signal)))
		return _server_ptrace_check_errno (inferior);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_resume (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	if (inferior->stepping)
		return mdb_server_step (handle);
	else
		return mdb_server_continue (handle);
}

static ServerCommandError
mdb_server_kill (ServerHandle *handle)
{
	if (ptrace (PTRACE_KILL, handle->inferior->pid, NULL, 0))
		return COMMAND_ERROR_UNKNOWN_ERROR;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_initialize_process (ServerHandle *handle)
{
	int flags = PTRACE_O_TRACECLONE | PTRACE_O_TRACEFORK | PTRACE_O_TRACEVFORK |
		PTRACE_O_TRACEEXEC;

	if (ptrace (PTRACE_SETOPTIONS, handle->inferior->pid, 0, GINT_TO_POINTER (flags))) {
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

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
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

ServerCommandError
mdb_server_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			    guint32 *target_address_size, guint32 *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (gsize);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_arch_get_registers (ServerHandle *server)
{
	return mdb_inferior_get_registers (server->inferior, &server->inferior->current_regs);
}

ServerCommandError
mdb_server_count_registers (ServerHandle *server, guint32 *out_count)
{
	*out_count = 18;
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_frame (ServerHandle *server, StackFrame *frame)
{
	ServerCommandError result;

	result = mdb_arch_get_registers (server);
	if (result != COMMAND_ERROR_NONE)
		return result;

	frame->address = server->inferior->current_regs.regs.ARM_pc;
	frame->stack_pointer = server->inferior->current_regs.regs.ARM_sp;
	frame->frame_address = server->inferior->current_regs.regs.ARM_fp;

	g_message (G_STRLOC ": TARGET INFO: %d - %d", sizeof (gsize), sizeof (void *));

	g_message (G_STRLOC ": %ld - %lx - %lx", (long) frame->address,
		   (long) frame->stack_pointer, (long) frame->frame_address);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_insert_breakpoint (ServerHandle *server, guint64 address, guint32 *bhandle)
{
	*bhandle = 0;
	return COMMAND_ERROR_NONE;
}

static int
disasm_read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	InferiorHandle *inferior = info->application_data;

	return mdb_inferior_read_memory (inferior, memaddr, length, myaddr);
}

static int
disasm_fprintf_func (gpointer stream, const char *message, ...)
{
	InferiorHandle *inferior = stream;
	va_list args;
	char *start;
	int len, max, retval;

	len = strlen (inferior->disasm_buffer);
	start = inferior->disasm_buffer + len;
	max = 1024 - len;

	va_start (args, message);
	retval = vsnprintf (start, max, message, args);
	va_end (args);

	return retval;
}

static void
disasm_print_address_func (bfd_vma addr, struct disassemble_info *info)
{
	InferiorHandle *inferior = info->application_data;
	const gchar *sym;
	char buf[30];

	sprintf_vma (buf, addr);

	if (inferior->main_bfd) {
		sym = mdb_exe_reader_lookup_symbol_by_addr (inferior->main_bfd, addr);
		if (sym) {
			(*info->fprintf_func) (info->stream, "%s(0x%s)", sym, buf);
			return;
		}
	}

	(*info->fprintf_func) (info->stream, "0x%s", buf);
}

static void
init_disassembler (InferiorHandle *inferior)
{
	struct disassemble_info *info;

	if (inferior->disassembler)
		return;

	info = g_new0 (struct disassemble_info, 1);
	INIT_DISASSEMBLE_INFO (*info, stderr, fprintf);
	info->flavour = bfd_target_coff_flavour;
	info->arch = bfd_arch_i386;
	info->mach = bfd_mach_i386_i386;
	info->octets_per_byte = 1;
	info->display_endian = info->endian = BFD_ENDIAN_LITTLE;

	info->application_data = inferior;
	info->read_memory_func = disasm_read_memory_func;
	info->print_address_func = disasm_print_address_func;
	info->fprintf_func = disasm_fprintf_func;
	info->stream = inferior;

	inferior->disassembler = info;
}

gchar *
mdb_server_disassemble_insn (ServerHandle *server, guint64 address, guint32 *out_insn_size)
{
	InferiorHandle *inferior = server->inferior;
	int ret;

	init_disassembler (inferior);

	memset (inferior->disasm_buffer, 0, 1024);

	ret = print_insn_little_arm (address, inferior->disassembler);

	if (out_insn_size)
		*out_insn_size = ret;

	return g_strdup (inferior->disasm_buffer);
}

ServerCommandError
mdb_arch_disable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

ServerEvent *
mdb_arch_child_stopped (ServerHandle *server, int stopsig)
{
	ServerEvent *e;

	mdb_arch_get_registers (server);

	e = g_new0 (ServerEvent, 1);
	e->sender_iid = server->iid;
	e->type = SERVER_EVENT_CHILD_STOPPED;

	return e;
}

#include "linux-ptrace.c"

InferiorVTable arm_ptrace_inferior = {
	mdb_server_global_init,
	mdb_server_get_server_type,
	mdb_server_get_capabilities,
	mdb_server_get_arch_type,
	mdb_server_create_inferior,
	mdb_server_initialize_process,
	mdb_server_initialize_thread,
	mdb_server_set_runtime_info,
	_server_ptrace_io_thread_main,
	mdb_server_spawn,
	mdb_server_attach,
	NULL, // detach
	NULL, // finalize
	NULL, // global_wait
	NULL, // stop_and_wait,
	NULL, // dispatch_event,
	NULL, // dispatch_simple,
	mdb_server_get_target_info,
	mdb_server_continue,
	mdb_server_step,
	mdb_server_resume,
	mdb_server_get_frame,
	NULL, // mdb_server_current_insn_is_bpt,
	mdb_server_peek_word,
	mdb_server_read_memory,
	mdb_server_write_memory,
	NULL, // mdb_server_call_method,
	NULL, // mdb_server_call_method_1,
	NULL, // mdb_server_call_method_2,
	NULL, // mdb_server_call_method_3,
	NULL, // mdb_server_call_method_invoke,
	NULL, // mdb_server_execute_instruction,
	NULL, // mdb_server_mark_rti_frame,
	NULL, // mdb_server_abort_invoke,
	mdb_server_insert_breakpoint,
	NULL, // mdb_server_insert_hw_breakpoint,
	NULL, // mdb_server_remove_breakpoint,
	NULL, // mdb_server_enable_breakpoint,
	NULL, // mdb_server_disable_breakpoint,
	NULL, // mdb_server_get_breakpoints,
	mdb_server_count_registers,
	mdb_server_get_registers,
	mdb_server_set_registers,
	NULL, // mdb_server_stop,
	mdb_server_set_signal,
	mdb_server_get_pending_signal,
	mdb_server_kill,
	mdb_server_get_signal_info,
	mdb_server_get_threads,
	mdb_server_get_application,
	NULL, // mdb_server_detach_after_fork,
	NULL, // mdb_server_push_registers,
	NULL, // mdb_server_pop_registers,
	NULL, // mdb_server_get_callback_frame,
	NULL, // mdb_server_restart_notification,
	NULL, // mdb_server_get_registers_from_core_file,
	mdb_server_get_current_pid,
	mdb_server_get_current_thread
};

