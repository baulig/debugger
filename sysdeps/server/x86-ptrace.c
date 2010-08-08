#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#define DEBUG_WAIT 1

#include <config.h>
#include <server.h>
#include <breakpoints.h>
#include <stdio.h>
#include <stdlib.h>
#include <pthread.h>
#include <semaphore.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/socket.h>
#include <sys/wait.h>
#include <sys/poll.h>
#include <sys/select.h>
#include <sys/resource.h>
#include <signal.h>
#include <unistd.h>
#include <sys/syscall.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

#ifdef MDB_SERVER
#include "mdb-server.h"
#endif

/*
 * NOTE:  The manpage is wrong about the POKE_* commands - the last argument
 *        is the data (a word) to be written, not a pointer to it.
 *
 * In general, the ptrace(2) manpage is very bad, you should really read
 * kernel/ptrace.c and arch/i386/kernel/ptrace.c in the Linux source code
 * to get a better understanding for this stuff.
 */

#ifdef __linux__
#include "x86-linux-ptrace.h"
#if !defined(MDB_SERVER)
#include "linux-wait.h"
#endif
#endif

#ifdef __FreeBSD__
#include "x86-freebsd-ptrace.h"
#endif

#ifdef __MACH__
#include "darwin-ptrace.h"
#endif

#include "x86-arch.h"

struct IOThreadData
{
	int output_fd, error_fd;
};

MonoRuntimeInfo *
mono_debugger_server_initialize_mono_runtime (guint32 address_size,
					      guint64 notification_address,
					      guint64 executable_code_buffer,
					      guint32 executable_code_buffer_size,
					      guint64 breakpoint_info_area,
					      guint64 breakpoint_table,
					      guint32 breakpoint_table_size)
{
	MonoRuntimeInfo *runtime = g_new0 (MonoRuntimeInfo, 1);

	runtime->address_size = address_size;
	runtime->notification_address = notification_address;
	runtime->executable_code_buffer = executable_code_buffer;
	runtime->executable_code_buffer_size = executable_code_buffer_size;
	runtime->executable_code_chunk_size = EXECUTABLE_CODE_CHUNK_SIZE;
	runtime->executable_code_total_chunks = executable_code_buffer_size / EXECUTABLE_CODE_CHUNK_SIZE;

	runtime->breakpoint_info_area = breakpoint_info_area;
	runtime->breakpoint_table = breakpoint_table;
	runtime->breakpoint_table_size = breakpoint_table_size;

	runtime->breakpoint_table_bitfield = g_malloc0 (breakpoint_table_size);
	runtime->executable_code_bitfield = g_malloc0 (runtime->executable_code_total_chunks);

	return runtime;
}

void
mono_debugger_server_initialize_code_buffer (MonoRuntimeInfo *runtime,
					     guint64 executable_code_buffer,
					     guint32 executable_code_buffer_size)
{
	runtime->executable_code_buffer = executable_code_buffer;
	runtime->executable_code_buffer_size = executable_code_buffer_size;
	runtime->executable_code_chunk_size = EXECUTABLE_CODE_CHUNK_SIZE;
	runtime->executable_code_total_chunks = executable_code_buffer_size / EXECUTABLE_CODE_CHUNK_SIZE;
}

void
mono_debugger_server_finalize_mono_runtime (MonoRuntimeInfo *runtime)
{
	runtime->executable_code_buffer = 0;
}

static void
mdb_server_finalize (ServerHandle *handle)
{
	mdb_arch_finalize (handle->arch);

	_server_ptrace_finalize_inferior (handle);

	g_free (handle->inferior);
	g_free (handle);
}

#ifndef __MACH__
#endif

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
mdb_server_detach (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PT_DETACH, inferior->pid, NULL, 0)) {
		g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_peek_word (InferiorHandle *inferior, guint64 start, guint64 *retval)
{
	return mdb_inferior_read_memory (inferior, start, sizeof (gsize), retval);
}

ServerCommandError
mdb_server_peek_word (ServerHandle *server, guint64 start, guint64 *retval)
{
	return mdb_inferior_read_memory (server->inferior, start, sizeof (gsize), retval);
}

ServerCommandError
mdb_server_read_memory (ServerHandle *server, guint64 start, guint32 size, gpointer buffer)
{
	ServerCommandError result;

	result = mdb_inferior_read_memory (server->inferior, start, size, buffer);
	if (result)
		return result;

	mdb_server_remove_breakpoints_from_target_memory (server, start, size, buffer);
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer buffer)
{
	return mdb_inferior_write_memory (handle->inferior, start, size, buffer);
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
	handle->arch = mdb_arch_initialize ();

	return handle;
}

static void
child_setup_func (InferiorHandle *inferior)
{
	if (ptrace (PT_TRACE_ME, getpid (), NULL, 0))
		g_error (G_STRLOC ": Can't PT_TRACEME: %s", g_strerror (errno));

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
	ServerCommandError result;

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

#if !defined(__MACH__)
	if (!_server_ptrace_wait_for_new_thread (handle))
		return COMMAND_ERROR_INTERNAL_ERROR;
#endif

	result = _server_ptrace_setup_inferior (handle);
	if (result != COMMAND_ERROR_NONE) {
		if (redirect_fds) {
			close (inferior->output_fd[0]);
			close (inferior->error_fd[0]);
		}
		return result;
	}

#ifdef __MACH__
	*child_pid = COMPOSED_PID(inferior->pid, inferior->os.thread_index);
#endif

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_server_initialize_thread (ServerHandle *handle, guint32 pid, gboolean do_wait)
{
	InferiorHandle *inferior = handle->inferior;

	inferior->is_thread = TRUE;

#ifdef __MACH__
	inferior->pid = GET_PID(pid);
	inferior->os.thread = get_thread_from_index(GET_THREAD_INDEX(pid));
#else
	inferior->pid = pid;
	if (do_wait && !_server_ptrace_wait_for_new_thread (handle))
		return COMMAND_ERROR_INTERNAL_ERROR;
#endif

	return _server_ptrace_setup_inferior (handle);
}

static ServerCommandError
mdb_server_attach (ServerHandle *handle, guint32 pid)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PT_ATTACH, pid, NULL, 0) != 0) {
		g_warning (G_STRLOC ": Can't attach to %d - %s", pid,
			   g_strerror (errno));
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	inferior->pid = pid;
	inferior->is_thread = TRUE;

#if !defined(__MACH__)
	if (!_server_ptrace_wait_for_new_thread (handle))
		return COMMAND_ERROR_INTERNAL_ERROR;
#endif

	return _server_ptrace_setup_inferior (handle);
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

#ifndef MDB_SERVER

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

#endif

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

static int
disasm_read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	ServerHandle *server = info->application_data;

	return mdb_server_read_memory (server, memaddr, length, myaddr);
}

static int
disasm_fprintf_func (gpointer stream, const char *message, ...)
{
	ServerHandle *server = stream;
	va_list args;
	char *start;
	int len, max, retval;

	len = strlen (server->inferior->disasm_buffer);
	start = server->inferior->disasm_buffer + len;
	max = 1024 - len;

	va_start (args, message);
	retval = vsnprintf (start, max, message, args);
	va_end (args);

	return retval;
}

static void
disasm_print_address_func (bfd_vma addr, struct disassemble_info *info)
{
	ServerHandle *server = info->application_data;
	char buf[30];

	sprintf_vma (buf, addr);
	(*info->fprintf_func) (info->stream, "0x%s", buf);
}

static void
init_disassembler (ServerHandle *server)
{
	struct disassemble_info *info;

	if (server->inferior->disassembler)
		return;

	info = g_new0 (struct disassemble_info, 1);
	INIT_DISASSEMBLE_INFO (*info, stderr, fprintf);
	info->flavour = bfd_target_elf_flavour;
	info->arch = bfd_arch_i386;
	info->mach = bfd_mach_i386_i386;
	info->octets_per_byte = 1;
	info->display_endian = info->endian = BFD_ENDIAN_LITTLE;

	info->application_data = server;
	info->read_memory_func = disasm_read_memory_func;
	info->print_address_func = disasm_print_address_func;
	info->fprintf_func = disasm_fprintf_func;
	info->stream = server;

	server->inferior->disassembler = info;
}

gchar *
mdb_server_disassemble_insn (ServerHandle *server, guint64 address, guint32 *out_insn_size)
{
	int ret;

	init_disassembler (server);

	memset (server->inferior->disasm_buffer, 0, 1024);

	ret = print_insn_i386 (address, server->inferior->disassembler);

	if (out_insn_size)
		*out_insn_size = ret;

	return g_strdup (server->inferior->disasm_buffer);
}

#ifdef __linux__
#include "x86-linux-ptrace.c"
#endif

#ifdef __FreeBSD__
#include "x86-freebsd-ptrace.c"
#endif

#ifdef __MACH__
#include "darwin-ptrace.c"
#endif

InferiorVTable i386_ptrace_inferior = {
	mdb_server_global_init,
	mdb_server_get_server_type,
	mdb_server_get_capabilities,
	mdb_server_get_arch_type,
	mdb_server_create_inferior,
	mdb_server_initialize_process,
	mdb_server_initialize_thread,
	mdb_server_set_runtime_info,
#ifdef MDB_SERVER
	NULL, // io_thread_main
#else
	_server_ptrace_io_thread_main,
#endif
	mdb_server_spawn,
	mdb_server_attach,
	mdb_server_detach,
	mdb_server_finalize,
#ifdef MDB_SERVER
	NULL, // global_wait
	NULL, // stop_and_wait
	NULL, // dispatch_event
	NULL, // dispatch_simple
#else
	mdb_server_global_wait,
	mdb_server_stop_and_wait,
	mdb_server_dispatch_event,
	mdb_server_dispatch_simple,
#endif
	mdb_server_get_target_info,
	mdb_server_continue,
	mdb_server_step,
	mdb_server_resume,
	mdb_server_get_frame,
	mdb_server_current_insn_is_bpt,
	mdb_server_peek_word,
	mdb_server_read_memory,
	mdb_server_write_memory,
	mdb_server_call_method,
	mdb_server_call_method_1,
	mdb_server_call_method_2,
	mdb_server_call_method_3,
	mdb_server_call_method_invoke,
	mdb_server_execute_instruction,
	mdb_server_mark_rti_frame,
	mdb_server_abort_invoke,
	mdb_server_insert_breakpoint,
	mdb_server_insert_hw_breakpoint,
	mdb_server_remove_breakpoint,
	mdb_server_enable_breakpoint,
	mdb_server_disable_breakpoint,
	mdb_server_get_breakpoints,
	mdb_server_count_registers,
	mdb_server_get_registers,
	mdb_server_set_registers,
#ifdef MDB_SERVER
	NULL, // stop
#else
	mdb_server_stop,
#endif
	mdb_server_set_signal,
	mdb_server_get_pending_signal,
	mdb_server_kill,
	mdb_server_get_signal_info,
	mdb_server_get_threads,
	mdb_server_get_application,
#ifdef MDB_SERVER
	NULL, // detach_after_fork
#else
	mdb_server_detach_after_fork,
#endif
	mdb_server_push_registers,
	mdb_server_pop_registers,
	mdb_server_get_callback_frame,
	mdb_server_restart_notification,
	mdb_server_get_registers_from_core_file,
	mdb_server_get_current_pid,
	mdb_server_get_current_thread
};
