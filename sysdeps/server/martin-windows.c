#include <mdb-server.h>
#include "x86-arch.h"
#include <string.h>
#include <assert.h>
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <glib.h>

struct InferiorHandle
{
	HANDLE process_handle;
	HANDLE thread_handle;
	DWORD process_id;
	DWORD thread_id;
	CONTEXT current_context;
	gint argc;
	gchar **argv;
};

#define INFERIOR_REG_EIP(r)	r.Eip
#define INFERIOR_REG_ESP(r)	r.Esp
#define INFERIOR_REG_EBP(r)	r.Ebp
#define INFERIOR_REG_EAX(r)	r.Eax
#define INFERIOR_REG_EBX(r)	r.Ebx
#define INFERIOR_REG_ECX(r)	r.Ecx
#define INFERIOR_REG_EDX(r)	r.Edx
#define INFERIOR_REG_ESI(r)	r.Esi
#define INFERIOR_REG_EDI(r)	r.Edi
#define INFERIOR_REG_EFLAGS(r)	r.EFlags
#define INFERIOR_REG_ESP(r)	r.Esp

#define INFERIOR_REG_FS(r)	r.SegFs
#define INFERIOR_REG_ES(r)	r.SegEs
#define INFERIOR_REG_DS(r)	r.SegDs
#define INFERIOR_REG_CS(r)	r.SegCs
#define INFERIOR_REG_SS(r)	r.SegSs
#define INFERIOR_REG_GS(r)	r.SegGs

#define DEBUG_EVENT_WAIT_TIMEOUT 5000
#define BP_OPCODE 0xCC  /* INT 3 instruction */
#define TF_BIT 0x100    /* single-step register bit */

static HANDLE debug_thread;
static HANDLE command_event;
static HANDLE ready_event;
static HANDLE wait_event;
static HANDLE command_mutex;

static InferiorDelegate *inferior_delegate;

static DWORD WINAPI debugging_thread_main (LPVOID dummy_arg);


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
	command_event = CreateEvent (NULL, FALSE, FALSE, NULL);
	g_assert (command_event);

	ready_event = CreateEvent (NULL, FALSE, FALSE, NULL);
	g_assert (ready_event);

	wait_event = CreateEvent (NULL, TRUE, FALSE, NULL);
	g_assert (wait_event);

	command_mutex = CreateMutex (NULL, FALSE, NULL);
	g_assert (command_mutex);

	debug_thread = CreateThread (NULL, 0, debugging_thread_main, NULL, 0, NULL);
	g_assert (debug_thread);
}

gboolean
mdb_server_inferior_command (InferiorDelegate *delegate)
{
	if (WaitForSingleObject (command_mutex, 0) != 0) {
		g_warning (G_STRLOC ": Failed to acquire command mutex !");
		return FALSE;
	}

	inferior_delegate = delegate;
	SetEvent (command_event);

	WaitForSingleObject (ready_event, INFINITE);

	ReleaseMutex (command_mutex);
	return TRUE;
}

/* helper to just print what event has occurred */
static void
show_debug_event (DEBUG_EVENT *debug_event)
{
	DWORD exception_code;

	switch (debug_event->dwDebugEventCode) {
	case EXCEPTION_DEBUG_EVENT:
		exception_code = debug_event->u.Exception.ExceptionRecord.ExceptionCode;
		if (exception_code== EXCEPTION_BREAKPOINT || exception_code == EXCEPTION_SINGLE_STEP)
			return;
		// DecodeException (e,tampon);
		g_debug ( "ExceptionCode: %d at address 0x%p",exception_code, 
			  debug_event->u.Exception.ExceptionRecord.ExceptionAddress);

		if (debug_event->u.Exception.dwFirstChance != 0)
			g_debug ("First Chance\n");
		else
			g_debug ("Second Chance \n");
		break;
	// ------------------------------------------------------------------
	// new thread started
	// ------------------------------------------------------------------
	case CREATE_THREAD_DEBUG_EVENT:
		g_debug ("Creating thread %d: hThread: %p,\tLocal base: %p, start at %p",
			 debug_event->dwThreadId,
			 debug_event->u.CreateThread.hThread,
			 debug_event->u.CreateThread.lpThreadLocalBase,
			 debug_event->u.CreateThread.lpStartAddress);
		/* thread  list !! to be places not in the show helper AddThread (DebugEvent); REMIND */
		break;
	// ------------------------------------------------------------------
	// new process started
	// ------------------------------------------------------------------
	case CREATE_PROCESS_DEBUG_EVENT:
		g_debug ("CreateProcess:\thProcess: %p\thThread: %p\n + %s%p\t%s%d"
			 "\n + %s%d\t%s%p\n + %s%p\t%s%p\t%s%d",
			 debug_event->u.CreateProcessInfo.hProcess,
			 debug_event->u.CreateProcessInfo.hThread,
			 TEXT ("Base of image:"), debug_event->u.CreateProcessInfo.lpBaseOfImage,
			 TEXT ("Debug info file offset: "), debug_event->u.CreateProcessInfo.dwDebugInfoFileOffset,
			 TEXT ("Debug info size: "), debug_event->u.CreateProcessInfo.nDebugInfoSize,
			 TEXT ("Thread local base:"), debug_event->u.CreateProcessInfo.lpThreadLocalBase,
			 TEXT ("Start Address:"), debug_event->u.CreateProcessInfo.lpStartAddress,
			 TEXT ("Image name:"), debug_event->u.CreateProcessInfo.lpImageName,
			 TEXT ("fUnicode: "), debug_event->u.CreateProcessInfo.fUnicode);
		break;
	// ------------------------------------------------------------------
	// existing thread terminated
	// ------------------------------------------------------------------
	case EXIT_THREAD_DEBUG_EVENT:
		g_debug ("Thread %d finished with code %d",
			 debug_event->dwThreadId, debug_event->u.ExitThread.dwExitCode);
		/* thread list deletion placement DeleteThreadFromList (DebugEvent); REMND */
		break;
	// ------------------------------------------------------------------
	// existing process terminated
	// ------------------------------------------------------------------
	case EXIT_PROCESS_DEBUG_EVENT:
		g_debug ("Exit Process %d Exit code: %d",debug_event->dwProcessId,debug_event->u.ExitProcess.dwExitCode);
		break;
	// ------------------------------------------------------------------
	// new DLL loaded
	// ------------------------------------------------------------------
	case LOAD_DLL_DEBUG_EVENT:
		g_debug ("Load DLL: Base %p",debug_event->u.LoadDll.lpBaseOfDll);
		break;
	// ------------------------------------------------------------------
	// existing DLL explicitly unloaded
	// ------------------------------------------------------------------
	case UNLOAD_DLL_DEBUG_EVENT:
		g_debug ("Unload DLL: base %p",debug_event->u.UnloadDll.lpBaseOfDll);
		break;
	// ------------------------------------------------------------------
	// OutputDebugString () occured
	// ------------------------------------------------------------------
	case OUTPUT_DEBUG_STRING_EVENT:
		g_debug ("OUTPUT_DEBUG_STRNG_EVENT\n");
		/* could be useful but left out for the moment */
		break;
	// ------------------------------------------------------------------
	// RIP occured
	// ------------------------------------------------------------------
	case RIP_EVENT:
		g_debug ("RIP:\n + %s%d\n + %s%d",
			 TEXT ("dwError: "), debug_event->u.RipInfo.dwError,
			 TEXT ("dwType: "), debug_event->u.RipInfo.dwType);
		break;
	// ------------------------------------------------------------------
	// unknown debug event occured
	// ------------------------------------------------------------------
	default:
		g_debug ("%s%X%s",
			 TEXT ("Debug Event:Unknown [0x"),
			 debug_event->dwDebugEventCode, "",
			 TEXT ("]"));
		break;
	}
    
}

static void
handle_debug_event (DEBUG_EVENT *de)
{
	g_message (G_STRLOC ": Got debug event: %d", de->dwDebugEventCode);

	show_debug_event (de);

	switch (de->dwDebugEventCode) {
	case EXCEPTION_DEBUG_EVENT: {
		DWORD exception_code;

		exception_code = de->u.Exception.ExceptionRecord.ExceptionCode;
		if (exception_code == EXCEPTION_BREAKPOINT || exception_code == EXCEPTION_SINGLE_STEP) {
			mdb_server_process_child_event (MESSAGE_CHILD_STOPPED, de->dwProcessId, 0, 0, 0, 0, NULL);
			ResetEvent (wait_event);
			return;
		}

		break;
	}

	default:
		break;
	}

	if (!ContinueDebugEvent (de->dwProcessId, de->dwThreadId, DBG_CONTINUE)) {
		format_windows_error_message (GetLastError ());
		fwprintf (stderr, windows_error_message);
	}
}

static DWORD WINAPI
debugging_thread_main (LPVOID dummy_arg)
{
	while (TRUE) {
		HANDLE wait_handles[2] = { command_event, wait_event };
		DWORD ret;

		ret = WaitForMultipleObjects (2, wait_handles, FALSE, INFINITE);

		g_message (G_STRLOC ": Main loop got event: %d", ret);

		if (ret == 0) { /* command_event */
			InferiorDelegate *delegate;

			delegate = inferior_delegate;
			inferior_delegate = NULL;

			delegate->func (delegate->user_data);

			g_message (G_STRLOC ": Command event done");

			SetEvent (ready_event);
		} else if (ret == 1) { /* wait_event */
			DEBUG_EVENT de;

			g_message (G_STRLOC ": waiting for debug event");

			if (WaitForDebugEvent (&de, DEBUG_EVENT_WAIT_TIMEOUT)) {
				handle_debug_event (&de);
			}
		} else {
			g_warning (G_STRLOC ": WaitForMultipleObjects() returned %d", ret);
		}
	}

	return 0;
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
	DEBUG_EVENT de;
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
	inferior->thread_handle = pi.hThread;
	inferior->process_id = pi.dwProcessId;
	inferior->thread_id = pi.dwThreadId;

	if (!WaitForDebugEvent (&de, 5000)) {
		format_windows_error_message (GetLastError ());
		fwprintf (stderr, windows_error_message);
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	if (de.dwDebugEventCode != CREATE_PROCESS_DEBUG_EVENT) {
		g_warning (G_STRLOC ": Got unknown debug event: %d", de.dwDebugEventCode);
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
win32_get_registers (InferiorHandle *inferior)
{
	memset (&inferior->current_context, 0, sizeof (inferior->current_context));
	inferior->current_context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS | CONTEXT_FLOATING_POINT | CONTEXT_EXTENDED_REGISTERS;

	if (!GetThreadContext (inferior->thread_handle, &inferior->current_context))
		return COMMAND_ERROR_UNKNOWN_ERROR;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_get_frame (ServerHandle *handle, StackFrame *frame)
{
	ServerCommandError result;

	result = win32_get_registers (handle->inferior);
	if (result)
		return result;

	frame->address = (guint32) INFERIOR_REG_EIP (handle->inferior->current_context);
	frame->stack_pointer = (guint32) INFERIOR_REG_ESP (handle->inferior->current_context);
	frame->frame_address = (guint32) INFERIOR_REG_EBP (handle->inferior->current_context);

	g_message (G_STRLOC ": %Lx - %Lx - %Lx", frame->address, frame->stack_pointer, frame->frame_address);

	return COMMAND_ERROR_NONE;
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

static BOOL
set_step_flag (InferiorHandle *inferior, BOOL on)
{
	CONTEXT context;

	context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS | CONTEXT_FLOATING_POINT;

	if (!GetThreadContext (inferior->thread_handle, &context)) {
		g_warning (G_STRLOC ": GetThreadContext() failed");
		return FALSE;
	}

        if (on)
		context.EFlags |= TF_BIT;
        else
		context.EFlags &= ~TF_BIT;

	if (!SetThreadContext (inferior->thread_handle, &context)) {
		g_warning (G_STRLOC ": SetThreadContext() failed");
		return FALSE;
	}

	return TRUE;
}

static ServerCommandError
server_win32_step (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	set_step_flag (inferior, TRUE);

	if (!ContinueDebugEvent (inferior->process_id, inferior->thread_id, DBG_CONTINUE)) {
		format_windows_error_message (GetLastError ());
		fwprintf (stderr, windows_error_message);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_continue (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	set_step_flag (inferior, FALSE);

	if (!ContinueDebugEvent (inferior->process_id, inferior->thread_id, DBG_CONTINUE)) {
		format_windows_error_message (GetLastError ());
		fwprintf (stderr, windows_error_message);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);

	return COMMAND_ERROR_NONE;
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
	*out_count = DEBUGGER_REG_LAST;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_get_registers (ServerHandle *handle, guint64 *values) 
{
	InferiorHandle *inferior = handle->inferior;

	values [DEBUGGER_REG_RBX] = (guint32) INFERIOR_REG_EBX (inferior->current_context);
	values [DEBUGGER_REG_RCX] = (guint32) INFERIOR_REG_ECX (inferior->current_context);
	values [DEBUGGER_REG_RDX] = (guint32) INFERIOR_REG_EDX (inferior->current_context);
	values [DEBUGGER_REG_RSI] = (guint32) INFERIOR_REG_ESI (inferior->current_context);
	values [DEBUGGER_REG_RDI] = (guint32) INFERIOR_REG_EDI (inferior->current_context);
	values [DEBUGGER_REG_RBP] = (guint32) INFERIOR_REG_EBP (inferior->current_context);
	values [DEBUGGER_REG_RAX] = (guint32) INFERIOR_REG_EAX (inferior->current_context);
	values [DEBUGGER_REG_DS] = (guint32) INFERIOR_REG_DS (inferior->current_context);
	values [DEBUGGER_REG_ES] = (guint32) INFERIOR_REG_ES (inferior->current_context);
	values [DEBUGGER_REG_FS] = (guint32) INFERIOR_REG_FS (inferior->current_context);
	values [DEBUGGER_REG_GS] = (guint32) INFERIOR_REG_GS (inferior->current_context);
	values [DEBUGGER_REG_RIP] = (guint32) INFERIOR_REG_EIP (inferior->current_context);
	values [DEBUGGER_REG_CS] = (guint32) INFERIOR_REG_CS (inferior->current_context);
	values [DEBUGGER_REG_EFLAGS] = (guint32) INFERIOR_REG_EFLAGS (inferior->current_context);
	values [DEBUGGER_REG_RSP] = (guint32) INFERIOR_REG_ESP (inferior->current_context);
	values [DEBUGGER_REG_SS] = (guint32) INFERIOR_REG_SS (inferior->current_context);

	return COMMAND_ERROR_NONE;
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

	sinfo->sigkill = 0xffff;
	sinfo->sigstop = 0xffff;
	sinfo->sigint = 0xffff;
	sinfo->sigchld = 0xffff;

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
	server_win32_continue,			/* continue */
	server_win32_step,			/* step */
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
