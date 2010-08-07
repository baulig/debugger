#include <mdb-server.h>
#include "x86-arch.h"
#include <string.h>
#include <assert.h>
#include <windows.h>
#include <tchar.h>
#include <Psapi.h>
#include <stdio.h>
#include <stdlib.h>
#include <glib.h>
#include <bfd.h>
#include <dis-asm.h>

typedef struct
{
	HANDLE process_handle;
	DWORD process_id;
	gint argc;
	gchar **argv;
	gchar *exe_path;
	struct disassemble_info *disassembler;
	char disasm_buffer [1024];
	MdbExeReader *main_bfd;
} ProcessHandle;

struct InferiorHandle
{
	ProcessHandle *process;
	HANDLE thread_handle;
	DWORD thread_id;
	CONTEXT current_context;
};

static GHashTable *server_hash; // thread id -> ServerHandle *
static GHashTable *bfd_hash; // filename -> MdbExeReader *

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

static BOOL win32_get_registers (InferiorHandle *inferior);
static BOOL win32_set_registers (InferiorHandle *inferior);
static BOOL read_from_debuggee (ProcessHandle *process, LPVOID start, LPVOID buf, DWORD size, PDWORD read_bytes);

static BOOL check_breakpoint (ServerHandle *server, guint64 address, guint64 *retval);
static BreakpointInfo *lookup_breakpoint (ServerHandle *server, guint32 idx, BreakpointManager **out_bpm);

static gchar *
tstring_to_string (const TCHAR *string)
{
	int len;
	gchar *ret;

	len = _tcslen (string);
	ret = g_malloc0 (len + 1);

	if (sizeof (TCHAR) == sizeof (wchar_t))
		wcstombs (ret, string, len);
	else
		strncpy (ret, (const char *) string, len);
	return ret;
}

/* Format a more readable error message on failures which set ErrorCode */
static gchar *
get_last_error (void)
{
	DWORD error_code, dw_rval;
	wchar_t message [2048];
	gchar *retval, *tmp_buf;

	error_code = GetLastError ();

	dw_rval = FormatMessage (FORMAT_MESSAGE_FROM_SYSTEM, NULL,
				 error_code, 0, message, 2048, NULL);

	if (dw_rval == 0)
		return g_strdup_printf ("Could not get error message (0x%x) from windows", error_code);

	tmp_buf = g_malloc0 (dw_rval + 1);
	wcstombs (tmp_buf, message, 2048);

	retval = g_strdup_printf ("WINDOWS ERROR (code=%x): %s", error_code, tmp_buf);
	g_free (tmp_buf);
	return retval;
}

static void
server_win32_global_init (void)
{
	server_hash = g_hash_table_new (NULL, NULL);
	bfd_hash = g_hash_table_new (NULL, NULL);

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

static MdbExeReader *
load_dll (const char *filename)
{
	MdbExeReader *reader;

	reader = mdb_server_create_exe_reader (filename);
	g_message (G_STRLOC ": LOAD DLL: %s -> %p", filename, reader);
	if (reader)
		g_hash_table_insert (bfd_hash, g_strdup (filename), reader);

	return reader;
}

static void
handle_debug_event (DEBUG_EVENT *de)
{
	ServerHandle *server;
	InferiorHandle *inferior;
	ProcessHandle *process;

	server = g_hash_table_lookup (server_hash, GINT_TO_POINTER (de->dwThreadId));
	if (!server) {
		g_warning (G_STRLOC ": Got debug event for unknown thread: %d/%d", de->dwProcessId, de->dwThreadId);
		if (!ContinueDebugEvent (de->dwProcessId, de->dwThreadId, DBG_CONTINUE)) {
			g_warning (G_STRLOC ": %s", get_last_error ());
		}
		return;
	}

	inferior = server->inferior;
	process = server->inferior->process;

	g_message (G_STRLOC ": Got debug event: %d - %d/%d - %p", de->dwDebugEventCode, de->dwProcessId, de->dwThreadId, server);

	// show_debug_event (de);

	switch (de->dwDebugEventCode) {
	case EXCEPTION_DEBUG_EVENT: {
		DWORD exception_code;
		PVOID exception_addr;

		exception_code = de->u.Exception.ExceptionRecord.ExceptionCode;
		exception_addr = de->u.Exception.ExceptionRecord.ExceptionAddress;

		g_message (G_STRLOC ": EXCEPTION (%d/%d): %x - %p - %d", de->dwProcessId, de->dwThreadId,
			   exception_code, exception_addr, de->u.Exception.dwFirstChance);

		if (exception_code == EXCEPTION_BREAKPOINT) {
			guint64 arg = 0;

			win32_get_registers (inferior);

			if (check_breakpoint (server, INFERIOR_REG_EIP (inferior->current_context) - 1, &arg)) {
				INFERIOR_REG_EIP (inferior->current_context)--;
				win32_set_registers (inferior);
				mdb_server_process_child_event (MESSAGE_CHILD_HIT_BREAKPOINT, de->dwProcessId, arg, 0, 0, 0, NULL);
			} else {
				mdb_server_process_child_event (MESSAGE_CHILD_STOPPED, de->dwProcessId, 0, 0, 0, 0, NULL);
			}

			ResetEvent (wait_event);
			return;
		} else if (exception_code == EXCEPTION_SINGLE_STEP) {
			mdb_server_process_child_event (MESSAGE_CHILD_STOPPED, de->dwProcessId, 0, 0, 0, 0, NULL);
			ResetEvent (wait_event);
			return;
		}

		if (!ContinueDebugEvent (de->dwProcessId, de->dwThreadId, DBG_EXCEPTION_NOT_HANDLED)) {
			g_warning (G_STRLOC ": %s", get_last_error ());
		}

		return;
	}

	case LOAD_DLL_DEBUG_EVENT: {
		TCHAR path [MAX_PATH];

		g_message (G_STRLOC ": load dll: %p - %p - %d", de->u.LoadDll.lpImageName, de->u.LoadDll.hFile, de->u.LoadDll.fUnicode);

		if (!process->exe_path) {
			/*
			 * This fails until kernel32.dll is loaded.
			 */
			if (GetModuleFileNameEx (process->process_handle, NULL, path, sizeof (path) / sizeof (TCHAR))) {
				process->exe_path = tstring_to_string (path);
				process->main_bfd = load_dll (process->exe_path);
			}
		}

#if 0
		if (de->u.LoadDll.hFile) {

			if (GetModuleFileNameEx (process->process_handle, de->u.LoadDll.hFile, path, sizeof (path) / sizeof (TCHAR))) {
				_tprintf (TEXT ("MODULE: %s\n"), path);
			} else {
				g_warning (G_STRLOC ": %s", get_last_error ());
			}

			if (GetModuleFileNameEx (process->process_handle, NULL, path, sizeof (path) / sizeof (TCHAR))) {
				_tprintf (TEXT ("PROCESS MODULE: %s\n"), path);
			} else {
				g_warning (G_STRLOC ": %s", get_last_error ());
			}

			{
				HMODULE hMods [1024];
				DWORD cbNeeded;
				int ret, i;

				if (EnumProcessModules (process->process_handle, hMods, sizeof (hMods), &cbNeeded)) {
					g_message (G_STRLOC ": ENUM MODULES !");

					for (i = 0; i < (cbNeeded / sizeof(HMODULE)); i++) {
						// Get the full path to the module's file.
						if (GetModuleFileNameEx (process->process_handle, hMods[i], path, sizeof (path) / sizeof (TCHAR))) {
							_tprintf (TEXT ("MODULE: |%s|\n"), path);
						}
					}
				}
			}
		}
#endif

		if (de->u.LoadDll.lpImageName) {
			char buf [1024];
			DWORD exc_code;

			if (!read_from_debuggee (process, de->u.LoadDll.lpImageName, &exc_code, 4, NULL) || !exc_code)
				break;

			if (de->u.LoadDll.fUnicode) {
				wchar_t w_buf [1024];
				size_t ret;

				if (!read_from_debuggee (server->inferior->process, (LPVOID) exc_code, w_buf, 300, NULL))
					break;

				ret = wcstombs (buf, w_buf, 300);
			} else {
				if (!read_from_debuggee (server->inferior->process, (LPVOID) exc_code, buf, 300, NULL))
					break;
			}

			g_message (G_STRLOC ": DLL LOADED: %s", buf);
			load_dll (buf);
			break;
		}
		break;
	}

	default:
		break;
	}

	if (!ContinueDebugEvent (de->dwProcessId, de->dwThreadId, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
	}
}

static DWORD WINAPI
debugging_thread_main (LPVOID dummy_arg)
{
	while (TRUE) {
		HANDLE wait_handles[2] = { command_event, wait_event };
		DWORD ret;

		ret = WaitForMultipleObjects (2, wait_handles, FALSE, INFINITE);

		if (ret == 0) { /* command_event */
			InferiorDelegate *delegate;

			delegate = inferior_delegate;
			inferior_delegate = NULL;

			delegate->func (delegate->user_data);

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
	handle->inferior->process = g_new0 (ProcessHandle, 1);

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
server_win32_spawn (ServerHandle *server, const gchar *working_directory,
		    const gchar **argv, const gchar **envp, gboolean redirect_fds,
		    gint *child_pid, IOThreadData **io_data, gchar **error)
{
	gunichar2* utf16_argv = NULL;
	gunichar2* utf16_envp = NULL;
	gunichar2* utf16_working_directory = NULL;
	InferiorHandle *inferior = server->inferior;
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
		inferior->process->argc = argc;
		inferior->process->argv = g_malloc0 ( (argc+1) * sizeof (gpointer));
		argv_concat = utf16_argv = g_malloc (len*sizeof (gunichar2));

		argv_temp = argv;
		while (*argv_temp) {
			gunichar2* utf16_argv_temp = g_utf8_to_utf16 (*argv_temp, -1, NULL, NULL, NULL);
			int written = snwprintf (argv_concat, len, L"%s ", utf16_argv_temp);
			inferior->process->argv [index++] = g_strdup (*argv_temp);
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
		g_warning (G_STRLOC ": %s", get_last_error ());
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	if (!WaitForDebugEvent (&de, 5000)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	if (de.dwDebugEventCode != CREATE_PROCESS_DEBUG_EVENT) {
		g_warning (G_STRLOC ": Got unknown debug event: %d", de.dwDebugEventCode);
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	g_message (G_STRLOC ": SPAWN: %d/%d", pi.dwProcessId, pi.dwThreadId);

	*child_pid = pi.dwProcessId;
	inferior->process->process_handle = pi.hProcess;
	inferior->process->process_id = pi.dwProcessId;

	inferior->thread_handle = pi.hThread;
	inferior->thread_id = pi.dwThreadId;

	g_hash_table_insert (server_hash, GINT_TO_POINTER (pi.dwThreadId), server);

	return COMMAND_ERROR_NONE;
}

static BOOL
win32_get_registers (InferiorHandle *inferior)
{
	memset (&inferior->current_context, 0, sizeof (inferior->current_context));
	inferior->current_context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS | CONTEXT_FLOATING_POINT | CONTEXT_EXTENDED_REGISTERS;

	return GetThreadContext (inferior->thread_handle, &inferior->current_context);
}

static BOOL
win32_set_registers (InferiorHandle *inferior)
{
	return SetThreadContext (inferior->thread_handle, &inferior->current_context);
}

static ServerCommandError
server_win32_get_frame (ServerHandle *handle, StackFrame *frame)
{
	if (!win32_get_registers (handle->inferior))
		return COMMAND_ERROR_UNKNOWN_ERROR;

	frame->address = (guint32) INFERIOR_REG_EIP (handle->inferior->current_context);
	frame->stack_pointer = (guint32) INFERIOR_REG_ESP (handle->inferior->current_context);
	frame->frame_address = (guint32) INFERIOR_REG_EBP (handle->inferior->current_context);

	g_message (G_STRLOC ": %Lx - %Lx - %Lx", frame->address, frame->stack_pointer, frame->frame_address);

	return COMMAND_ERROR_NONE;
}

static BOOL
read_from_debuggee (ProcessHandle *process, LPVOID start, LPVOID buf, DWORD size, PDWORD read_bytes)
{
	SetLastError (0);
	if (!ReadProcessMemory (process->process_handle, start, buf, size, read_bytes)) {
		g_warning (G_STRLOC ": %p/%d - %s", start, size, get_last_error ());
		return FALSE;
	}

	return TRUE;
}

static BOOL
write_to_debuggee (ProcessHandle *process, LPVOID start, LPVOID buf, DWORD size, PDWORD read_bytes)
{
	SetLastError (0);
	if (!WriteProcessMemory (process->process_handle, start, buf, size, read_bytes)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return FALSE;
	}

	if (!FlushInstructionCache (process->process_handle, start, size)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return FALSE;
	}

	return TRUE;
}


static ServerCommandError
server_win32_read_memory (ServerHandle *server, guint64 start, guint32 size, gpointer buffer)
{
	if (!read_from_debuggee (server->inferior->process, GINT_TO_POINTER (start), buffer, size, NULL))
		return COMMAND_ERROR_MEMORY_ACCESS;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_write_memory (ServerHandle *server, guint64 start, guint32 size, gconstpointer buffer)
{
	if (!write_to_debuggee (server->inferior->process, GINT_TO_POINTER (start), buffer, size, NULL))
		return COMMAND_ERROR_MEMORY_ACCESS;

	return COMMAND_ERROR_NONE;
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
server_win32_step (ServerHandle *server)
{
	InferiorHandle *inferior = server->inferior;

	set_step_flag (inferior, TRUE);

	if (!ContinueDebugEvent (inferior->process->process_id, inferior->thread_id, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": continue (%d/%d): %s", inferior->process->process_id, inferior->thread_id, get_last_error ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_continue (ServerHandle *server)
{
	InferiorHandle *inferior = server->inferior;

	set_step_flag (inferior, FALSE);

	if (!ContinueDebugEvent (inferior->process->process_id, inferior->thread_id, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);

	return COMMAND_ERROR_NONE;
}

static BOOL
check_breakpoint (ServerHandle *server, guint64 address, guint64 *retval)
{
	BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock ();
	info = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (server->bpm, address);
	if (!info || !info->enabled) {
		mono_debugger_breakpoint_manager_unlock ();
		return FALSE;
	}

	*retval = info->id;
	mono_debugger_breakpoint_manager_unlock ();
	return TRUE;
}

static BreakpointInfo *
lookup_breakpoint (ServerHandle *server, guint32 idx, BreakpointManager **out_bpm)
{
	BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock ();
	info = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (server->bpm, idx);
	if (info) {
		if (out_bpm)
			*out_bpm = server->bpm;
		mono_debugger_breakpoint_manager_unlock ();
		return info;
	}

	if (out_bpm)
		*out_bpm = NULL;

	mono_debugger_breakpoint_manager_unlock ();
	return info;
}

static ServerCommandError
x86_arch_enable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	char bopcode = 0xcc;
	guint32 address;

	if (breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->address;

	result = server_win32_read_memory (server, address, 1, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_win32_write_memory (server, address, 1, &bopcode);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
x86_arch_disable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	guint32 address;

	if (!breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->address;

	result = server_win32_write_memory (server, address, 1, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_insert_breakpoint (ServerHandle *server, guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	g_message (G_STRLOC ": insert breakpoint: %Lx", address);

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (server->bpm, address);
	if (breakpoint) {
		/*
		 * You cannot have a hardware breakpoint and a normal breakpoint on the same
		 * instruction.
		 */
		if (breakpoint->is_hardware_bpt) {
			mono_debugger_breakpoint_manager_unlock ();
			return COMMAND_ERROR_DR_OCCUPIED;
		}

		breakpoint->refcount++;
		goto done;
	}

	breakpoint = g_new0 (BreakpointInfo, 1);

	breakpoint->refcount = 1;
	breakpoint->address = address;
	breakpoint->is_hardware_bpt = FALSE;
	breakpoint->id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->dr_index = -1;

	result = x86_arch_enable_breakpoint (server, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_insert (server->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_remove_breakpoint (ServerHandle *server, guint32 idx)
{
	BreakpointManager *bpm;
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = lookup_breakpoint (server, idx, &bpm);
	if (!breakpoint) {
		result = COMMAND_ERROR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	if (--breakpoint->refcount > 0) {
		result = COMMAND_ERROR_NONE;
		goto out;
	}

	result = x86_arch_disable_breakpoint (server, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	breakpoint->enabled = FALSE;
	mono_debugger_breakpoint_manager_remove (bpm, breakpoint);

 out:
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static ServerCommandError
server_win32_enable_breakpoint (ServerHandle *server, guint32 idx)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = lookup_breakpoint (server, idx, NULL);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = x86_arch_enable_breakpoint (server, breakpoint);
	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static ServerCommandError
server_win32_disable_breakpoint (ServerHandle *server, guint32 idx)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = lookup_breakpoint (server, idx, NULL);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = x86_arch_disable_breakpoint (server, breakpoint);
	breakpoint->enabled = FALSE;
	mono_debugger_breakpoint_manager_unlock ();
	return result;
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
server_win32_count_registers (ServerHandle *server, guint32 *out_count)
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
server_win32_get_application (ServerHandle *server, gchar **exe_file, gchar **cwd,
			      guint32 *nargs, gchar ***cmdline_args)
{
	ProcessHandle *process = server->inferior->process;
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
	*exe_file = g_strdup (process->argv [0]);
	*nargs = process->argc;
	*cwd = NULL;

	array = g_ptr_array_new ();

	for (index = 0; index < process->argc; index++)
		g_ptr_array_add (array, process->argv [index]);

	*cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (index = 0; index < array->len; index++)
		ptr  [index] = g_ptr_array_index (array, index);

	g_ptr_array_free (array, FALSE);

	return COMMAND_ERROR_NONE;
}

static int
disasm_read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	ProcessHandle *process = info->application_data;

	return read_from_debuggee (process, GUINT_TO_POINTER (memaddr), myaddr, length, NULL) ? 0 : 1;
}

static int
disasm_fprintf_func (gpointer stream, const char *message, ...)
{
	ProcessHandle *process = stream;
	va_list args;
	char *start;
	int len, max, retval;

	len = strlen (process->disasm_buffer);
	start = process->disasm_buffer + len;
	max = 1024 - len;

	va_start (args, message);
	retval = vsnprintf (start, max, message, args);
	va_end (args);

	return retval;
}

static void
disasm_print_address_func (bfd_vma addr, struct disassemble_info *info)
{
	ProcessHandle *process = info->application_data;
	const gchar *sym;
	char buf[30];

	sprintf_vma (buf, addr);

	if (process->main_bfd) {
		sym = mdb_exe_reader_lookup_symbol_by_addr (process->main_bfd, addr);
		if (sym) {
			(*info->fprintf_func) (info->stream, "%s(0x%s)", sym, buf);
			return;
		}
	}

	(*info->fprintf_func) (info->stream, "0x%s", buf);
}

static void
init_disassembler (ProcessHandle *process)
{
	struct disassemble_info *info;

	if (process->disassembler)
		return;

	info = g_new0 (struct disassemble_info, 1);
	INIT_DISASSEMBLE_INFO (*info, stderr, fprintf);
	info->flavour = bfd_target_coff_flavour;
	info->arch = bfd_arch_i386;
	info->mach = bfd_mach_i386_i386;
	info->octets_per_byte = 1;
	info->display_endian = info->endian = BFD_ENDIAN_LITTLE;

	info->application_data = process;
	info->read_memory_func = disasm_read_memory_func;
	info->print_address_func = disasm_print_address_func;
	info->fprintf_func = disasm_fprintf_func;
	info->stream = process;

	process->disassembler = info;
}

gchar *
mdb_server_disassemble_insn (ServerHandle *server, guint64 address, guint32 *out_insn_size)
{
	ProcessHandle *process = server->inferior->process;
	int ret;

	init_disassembler (process);

	memset (process->disasm_buffer, 0, 1024);

	ret = print_insn_i386 (address, process->disassembler);

	if (out_insn_size)
		*out_insn_size = ret;

	return g_strdup (process->disasm_buffer);
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
	server_win32_enable_breakpoint,		/* enable_breakpoint */
	server_win32_disable_breakpoint,	/* disable_breakpoint */
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
