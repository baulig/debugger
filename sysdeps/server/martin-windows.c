#include <server.h>
#include <string.h>
#include <assert.h>
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <glib.h>

struct InferiorHandle
{
	HANDLE process_handle;
	gint argc;
	gchar **argv;
};

static wchar_t windows_error_message [2048];
static const size_t windows_error_message_len = 2048;

/* Format a more readable error message on failures which set ErrorCode */
static void
format_windows_error_message (DWORD error_code)
{
	DWORD dw_rval;
	dw_rval = FormatMessage (FORMAT_MESSAGE_FROM_SYSTEM, NULL,
				 error_code, 0, windows_error_message,
				 windows_error_message_len, NULL);

	if (FALSE == dw_rval) {
		fprintf (stderr, "Could not get error message from windows\n");
	} else {
		fwprintf (stderr, L"WINDOWS ERROR (code=%x): %s\n", error_code, windows_error_message);
	}
}

static void
server_win32_global_init (void)
{
}

static ServerType
server_win32_get_server_type (void)
{
	return SERVER_TYPE_WIN32;
}

static ServerCapabilities
server_win32_get_capabilities (void)
{
	return SERVER_CAPABILITIES_NONE;
}

static ServerHandle *
server_win32_create_inferior (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);

	return handle;
}

static ServerCommandError
server_win32_initialize_process (ServerHandle *handle)
{
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			      guint32 *target_address_size, guint32 *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint32);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_spawn (ServerHandle *handle, const gchar *working_directory,
		    const gchar **argv, const gchar **envp, gboolean redirect_fds,
		    gint *child_pid, IOThreadData **io_data, gchar **error)
{
	gunichar2* utf16_argv = NULL;
	gunichar2* utf16_envp = NULL;
	gunichar2* utf16_working_directory = NULL;
	InferiorHandle *inferior = handle->inferior;
	STARTUPINFO si = {0};
	PROCESS_INFORMATION pi = {0};
	SECURITY_ATTRIBUTES sa;
	SECURITY_DESCRIPTOR sd;
	LPSECURITY_ATTRIBUTES lpsa = NULL;
	BOOL b_ret;

	if (io_data)
		*io_data = NULL;
	if (error)
		*error = NULL;

	if (working_directory)
		utf16_working_directory = g_utf8_to_utf16 (working_directory, -1, NULL, NULL, NULL);

	if (envp) {
		guint len = 0;
		const gchar** envp_temp = envp;
		gunichar2* envp_concat;

		while (*envp_temp) {
			len += strlen (*envp_temp) + 1;
			envp_temp++;
		}
		len++; /* add one for double NULL at end */
		envp_concat = utf16_envp = g_malloc (len*sizeof (gunichar2));

		envp_temp = envp;
		while (*envp_temp) {
			gunichar2* utf16_envp_temp = g_utf8_to_utf16 (*envp_temp, -1, NULL, NULL, NULL);
			int written = snwprintf (envp_concat, len, L"%s%s", utf16_envp_temp, L"\0");
			g_free (utf16_envp_temp);
			envp_concat += written + 1;
			len -= written;
			envp_temp++;
		}
		snwprintf (envp_concat, len, L"%s", L"\0"); /* double NULL at end */
	}

	if (argv) {
		gint argc = 0;
		guint len = 0;
		gint index = 0;
		const gchar** argv_temp = argv;
		gunichar2* argv_concat;

		while (*argv_temp) {
			len += strlen (*argv_temp) + 1;
			argv_temp++;
			argc++;
		}
		inferior->argc = argc;
		inferior->argv = g_malloc0 ( (argc+1) * sizeof (gpointer));
		argv_concat = utf16_argv = g_malloc (len*sizeof (gunichar2));

		argv_temp = argv;
		while (*argv_temp) {
			gunichar2* utf16_argv_temp = g_utf8_to_utf16 (*argv_temp, -1, NULL, NULL, NULL);
			int written = snwprintf (argv_concat, len, L"%s ", utf16_argv_temp);
			inferior->argv [index++] = g_strdup (*argv_temp);
			g_free (utf16_argv_temp);
			argv_concat += written;
			len -= written;
			argv_temp++;
		}
	}


	si.cb = sizeof (STARTUPINFO);
	
	/* fill in the process's startup information
	   we have to check how this startup info have to be filled and how 
	   redirection should be setup, this is an REMINDER for doing that */

	InitializeSecurityDescriptor (&sd, SECURITY_DESCRIPTOR_REVISION);
	SetSecurityDescriptorDacl (&sd, TRUE, NULL, FALSE);
	sa.nLength = sizeof (SECURITY_ATTRIBUTES);
	sa.bInheritHandle = TRUE;
	sa.lpSecurityDescriptor = &sd;
	lpsa = &sa;

	b_ret = CreateProcess (NULL, utf16_argv, lpsa, lpsa, FALSE,
			       DEBUG_PROCESS | DEBUG_ONLY_THIS_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT,
			       utf16_envp, utf16_working_directory, &si, &pi);

	if (!b_ret) {
		/* cleanup code where to place here or one function above REMIND */
		format_windows_error_message (GetLastError ());
		/* this should find it's way to the proper glib error mesage handler */
		fwprintf (stderr, windows_error_message);
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	*child_pid = pi.dwProcessId;
	inferior->process_handle = pi.hProcess;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_get_frame (ServerHandle *handle, StackFrame *frame)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer buffer)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *bhandle)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_remove_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_get_breakpoints (ServerHandle *handle, guint32 *count, guint32 **retval)
{
	int i;
	GPtrArray *breakpoints;

	mono_debugger_breakpoint_manager_lock ();
	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	*count = breakpoints->len;
	*retval = g_new0 (guint32, breakpoints->len);

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		 (*retval) [i] = info->id;
	}
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;	
}

static ServerCommandError
server_win32_count_registers (InferiorHandle *inferior, guint32 *out_count)
{
	*out_count = -1;
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_get_registers (ServerHandle *handle, guint64 *values) 
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_win32_set_registers (ServerHandle *handle, guint64 *values) 
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}     

static ServerCommandError
server_win32_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out)
{
	SignalInfo *sinfo = g_new0 (SignalInfo, 1);

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			       guint32 *nargs, gchar ***cmdline_args)
{
	gint index = 0;
	GPtrArray *array;
	gchar **ptr;
	/* No supported way to get command line of a process
	   see http://blogs.msdn.com/oldnewthing/archive/2009/02/23/9440784.aspx */

/*	gunichar2 utf16_exe_file [1024];
	gunichar2 utf16_cmd_line [10240];
	gunichar2 utf16_env_vars [10240];
	BOOL ret;
	if (!GetModuleFileNameEx (handle->inferior->process_handle, NULL, utf16_exe_file, sizeof (utf16_exe_file)/sizeof (utf16_exe_file [0]))) {
		DWORD error = GetLastError ();
		return COMMAND_ERROR_INTERNAL_ERROR;
	}
	*/
	*exe_file = g_strdup (handle->inferior->argv [0]);
	*nargs = handle->inferior->argc;
	*cwd = NULL;

	array = g_ptr_array_new ();

	for (index = 0; index < handle->inferior->argc; index++)
		g_ptr_array_add (array, handle->inferior->argv [index]);

	*cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (index = 0; index < array->len; index++)
		ptr  [index] = g_ptr_array_index (array, index);

	g_ptr_array_free (array, FALSE);

	return COMMAND_ERROR_NONE;
}

InferiorVTable i386_windows_inferior = {
	server_win32_global_init,
	server_win32_get_server_type,
	server_win32_get_capabilities,
	server_win32_create_inferior,
	server_win32_initialize_process,
	NULL,					/* initialize_thread */
	NULL,					/* set_runtime_info */
	NULL,					/* io_thread_main */
	server_win32_spawn,			/* spawn */
	NULL,					/* attach */
	NULL,					/* detach */
	NULL,					/* finalize */
	NULL,					/* global_wait */
	NULL,					/* stop_and_wait, */
	NULL,					/* dispatch_event */
	NULL,					/* dispatch_simple */
	server_win32_get_target_info,		/* get_target_info */
	NULL,					/* continue */
	NULL,					/* step */
	NULL,					/* resume */
	server_win32_get_frame,			/* get_frame */
	NULL,					/* current_insn_is_bpt */
	NULL,					/* peek_word */
	server_win32_read_memory,		/* read_memory, */
	server_win32_write_memory,		/* write_memory */
	NULL,					/* call_method */
	NULL,					/* call_method_1 */
	NULL,					/* call_method_2 */
	NULL,					/* call_method_3 */
	NULL,					/* call_method_invoke */
	NULL,					/* execute_instruction */
	NULL,					/* mark_rti_frame */
	NULL,					/* abort_invoke */
	server_win32_insert_breakpoint,		/* insert_breakpoint */
	NULL,					/* insert_hw_breakpoint */
	server_win32_remove_breakpoint,		/* remove_breakpoint */
	NULL,					/* enable_breakpoint */
	NULL,					/* disable_breakpoint */
	server_win32_get_breakpoints,		/* get_breakpoints */
	server_win32_count_registers,		/* count_registers */
	server_win32_get_registers,		/* get_registers */
	server_win32_set_registers,		/* set_registers */
	NULL,					/* stop */
	NULL,					/* set_signal */
	NULL,					/* server_ptrace_get_pending_signal */
	NULL,					/* kill */
	server_win32_get_signal_info,		/* get_signal_info */
	NULL,					/* get_threads */
	server_win32_get_application,		/* get_application */
	NULL,					/* detach_after_fork */
	NULL,					/* push_registers */
	NULL,					/* pop_registers */
	NULL,					/* get_callback_frame */
	NULL,					/* server_ptrace_restart_notification */
	NULL,					/* get_registers_from_core_file */
	NULL,					/* get_current_pid */
	NULL					/* get_current_thread */
};
