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

#include "i386-arch.h"

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
};

static GHashTable *server_hash; // thread id -> ServerHandle *
static GHashTable *bfd_hash; // filename -> MdbExeReader *

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
mdb_server_global_init (void)
{
	g_assert (!server_hash);

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

	show_debug_event (de);

	switch (de->dwDebugEventCode) {
	case EXCEPTION_DEBUG_EVENT: {
		DWORD exception_code;
		PVOID exception_addr;

		exception_code = de->u.Exception.ExceptionRecord.ExceptionCode;
		exception_addr = de->u.Exception.ExceptionRecord.ExceptionAddress;

		g_message (G_STRLOC ": EXCEPTION (%d/%d): %x - %p - %d", de->dwProcessId, de->dwThreadId,
			   exception_code, exception_addr, de->u.Exception.dwFirstChance);

		if ((exception_code == EXCEPTION_BREAKPOINT) || (exception_code == EXCEPTION_SINGLE_STEP)) {
			ServerEvent *e;

			e = mdb_arch_child_stopped (server, 0);
			if (e)
				mdb_server_process_child_event (e);
			else
				g_warning (G_STRLOC ": mdb_arch_child_stopped() returned NULL.");

			ResetEvent (wait_event);
			return;
		}

		g_message (G_STRLOC ": resuming from exception (%x/%x)", de->dwProcessId, de->dwThreadId);

		if (!ContinueDebugEvent (de->dwProcessId, de->dwThreadId, DBG_EXCEPTION_NOT_HANDLED)) {
			g_warning (G_STRLOC ": resuming from exception (%x/%x): %s", de->dwProcessId, de->dwThreadId, get_last_error ());
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

			if (mdb_inferior_read_memory (inferior, GPOINTER_TO_UINT (de->u.LoadDll.lpImageName), 4, &exc_code))
				break;

			if (!exc_code)
				break;

			if (de->u.LoadDll.fUnicode) {
				wchar_t w_buf [1024];
				size_t ret;

				if (mdb_inferior_read_memory (server->inferior, exc_code, 300, w_buf))
					break;

				ret = wcstombs (buf, w_buf, 300);
			} else {
				if (mdb_inferior_read_memory (server->inferior, exc_code, 300, buf))
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

	g_message (G_STRLOC ": resuming from debug event (%x/%x)", de->dwProcessId, de->dwThreadId);
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

			if (WaitForDebugEvent (&de, DEBUG_EVENT_WAIT_TIMEOUT)) {
				handle_debug_event (&de);
			}
		} else {
			g_warning (G_STRLOC ": WaitForMultipleObjects() returned %d", ret);
		}
	}

	return 0;
}

ServerType
mdb_server_get_server_type (void)
{
	return SERVER_TYPE_WIN32;
}

ServerCapabilities
mdb_server_get_capabilities (void)
{
	return SERVER_CAPABILITIES_NONE;
}

static ServerHandle *
mdb_server_create_inferior (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = bpm;
	handle->arch = mdb_arch_initialize ();
	handle->inferior = g_new0 (InferiorHandle, 1);
	handle->inferior->process = g_new0 (ProcessHandle, 1);

	return handle;
}

static ServerCommandError
mdb_server_initialize_process (ServerHandle *handle)
{
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_spawn (ServerHandle *server, const gchar *working_directory,
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

ServerCommandError
mdb_inferior_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	memset (regs, 0, sizeof (regs));
	regs->context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS |
		CONTEXT_FLOATING_POINT | CONTEXT_EXTENDED_REGISTERS | CONTEXT_DEBUG_REGISTERS;

	if (!GetThreadContext (inferior->thread_handle, &regs->context)) {
		g_warning (G_STRLOC ": get_registers: %s", get_last_error ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	regs->dr_status = regs->context.Dr6;
	regs->dr_control = regs->context.Dr7;
	regs->dr_regs[0] = regs->context.Dr0;
	regs->dr_regs[1] = regs->context.Dr1;
	regs->dr_regs[2] = regs->context.Dr2;
	regs->dr_regs[3] = regs->context.Dr3;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	regs->context.Dr6 = regs->dr_status;
	regs->context.Dr7 = regs->dr_control;
	regs->context.Dr0 = regs->dr_regs[0];
	regs->context.Dr1 = regs->dr_regs[1];
	regs->context.Dr2 = regs->dr_regs[2];
	regs->context.Dr3 = regs->dr_regs[3];

	regs->context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS |
		CONTEXT_FLOATING_POINT | CONTEXT_EXTENDED_REGISTERS | CONTEXT_DEBUG_REGISTERS;

	if (!SetThreadContext (inferior->thread_handle, &regs->context))
		return COMMAND_ERROR_MEMORY_ACCESS;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
mdb_process_read_memory (ProcessHandle *process, guint64 start, guint32 size, gpointer buffer)
{
	SetLastError (0);
	if (!ReadProcessMemory (process->process_handle, GINT_TO_POINTER ((guint) start), buffer, size, NULL)) {
		g_warning (G_STRLOC ": %lx/%d - %s", (gsize) start, size, get_last_error ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	return COMMAND_ERROR_NONE;
}

extern ServerCommandError
mdb_inferior_read_memory (InferiorHandle *inferior, guint64 start, guint32 size, gpointer buffer)
{
	return mdb_process_read_memory (inferior->process, start, size, buffer);
}

extern ServerCommandError
mdb_inferior_write_memory (InferiorHandle *inferior, guint64 start, guint32 size, gconstpointer buffer)
{
	SetLastError (0);
	if (!WriteProcessMemory (inferior->process->process_handle, GINT_TO_POINTER ((guint32) start), buffer, size, NULL)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	if (!FlushInstructionCache (inferior->process->process_handle, GINT_TO_POINTER ((guint32) start), size)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	return COMMAND_ERROR_NONE;
}


ServerCommandError
mdb_server_read_memory (ServerHandle *server, guint64 start, guint32 size, gpointer buffer)
{
	if (mdb_inferior_read_memory (server->inferior, start, size, buffer))
		return COMMAND_ERROR_MEMORY_ACCESS;

	mdb_server_remove_breakpoints_from_target_memory (server, start, size, buffer);
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_write_memory (ServerHandle *server, guint64 start, guint32 size, gconstpointer buffer)
{
	if (mdb_inferior_write_memory (server->inferior, start, size, buffer));
		return COMMAND_ERROR_MEMORY_ACCESS;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_inferior_peek_word (InferiorHandle *inferior, guint64 start, guint64 *retval)
{
	return mdb_inferior_read_memory (inferior, start, sizeof (gsize), retval);
}

ServerCommandError
mdb_inferior_poke_word (InferiorHandle *inferior, guint64 start, gsize word)
{
	return mdb_inferior_write_memory (inferior, start, sizeof (gsize), &word);
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

ServerCommandError
mdb_server_step (ServerHandle *server)
{
	InferiorHandle *inferior = server->inferior;

	set_step_flag (inferior, TRUE);

	g_warning (G_STRLOC ": step (%d/%d)", inferior->process->process_id, inferior->thread_id);

	if (!ContinueDebugEvent (inferior->process->process_id, inferior->thread_id, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": step (%d/%d): %s", inferior->process->process_id, inferior->thread_id, get_last_error ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_continue (ServerHandle *server)
{
	InferiorHandle *inferior = server->inferior;

	set_step_flag (inferior, FALSE);

	g_warning (G_STRLOC ": continue (%d/%d)", inferior->process->process_id, inferior->thread_id);

	if (!ContinueDebugEvent (inferior->process->process_id, inferior->thread_id, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": continue (%d/%d): %s", inferior->process->process_id, inferior->thread_id, get_last_error ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_application (ServerHandle *server, gchar **exe_file, gchar **cwd,
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

ServerCommandError
mdb_inferior_make_memory_executable (InferiorHandle *inferior, guint64 start, guint32 size)
{
	return COMMAND_ERROR_NONE;
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
	ServerHandle *server = info->application_data;
	ProcessHandle *process = server->inferior->process;
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
init_disassembler (ServerHandle *server)
{
	ProcessHandle *process = server->inferior->process;
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

	info->read_memory_func = disasm_read_memory_func;
	info->print_address_func = disasm_print_address_func;
	info->fprintf_func = disasm_fprintf_func;
	info->application_data = server;
	info->stream = process;

	process->disassembler = info;
}

gchar *
mdb_server_disassemble_insn (ServerHandle *server, guint64 address, guint32 *out_insn_size)
{
	ProcessHandle *process = server->inferior->process;
	int ret;

	init_disassembler (server);

	memset (process->disasm_buffer, 0, 1024);

	ret = print_insn_i386 (address, process->disassembler);

	if (out_insn_size)
		*out_insn_size = ret;

	return g_strdup (process->disasm_buffer);
}

InferiorVTable i386_windows_inferior = {
	mdb_server_global_init,
	mdb_server_get_server_type,
	mdb_server_get_capabilities,
	mdb_server_get_arch_type,
	mdb_server_create_inferior,
	mdb_server_initialize_process,
	NULL,					/* initialize_thread */
	NULL,					/* set_runtime_info */
	NULL,					/* io_thread_main */
	mdb_server_spawn,			/* spawn */
	NULL,					/* attach */
	NULL,					/* detach */
	NULL,					/* finalize */
	NULL,					/* global_wait */
	NULL,					/* stop_and_wait, */
	NULL,					/* dispatch_event */
	NULL,					/* dispatch_simple */
	mdb_server_get_target_info,		/* get_target_info */
	mdb_server_continue,			/* continue */
	mdb_server_step,			/* step */
	NULL,					/* resume */
	mdb_server_get_frame,			/* get_frame */
	NULL,					/* current_insn_is_bpt */
	NULL,					/* peek_word */
	mdb_server_read_memory,			/* read_memory, */
	mdb_server_write_memory,		/* write_memory */
	NULL,					/* call_method */
	NULL,					/* call_method_1 */
	NULL,					/* call_method_2 */
	NULL,					/* call_method_3 */
	NULL,					/* call_method_invoke */
	NULL,					/* execute_instruction */
	NULL,					/* mark_rti_frame */
	NULL,					/* abort_invoke */
	mdb_server_insert_breakpoint,		/* insert_breakpoint */
	NULL,					/* insert_hw_breakpoint */
	mdb_server_remove_breakpoint,		/* remove_breakpoint */
	mdb_server_enable_breakpoint,		/* enable_breakpoint */
	mdb_server_disable_breakpoint,		/* disable_breakpoint */
	mdb_server_get_breakpoints,		/* get_breakpoints */
	mdb_server_count_registers,		/* count_registers */
	mdb_server_get_registers,		/* get_registers */
	mdb_server_set_registers,		/* set_registers */
	NULL,					/* stop */
	NULL,					/* set_signal */
	NULL,					/* server_ptrace_get_pending_signal */
	NULL,					/* kill */
	NULL,					/* get_signal_info */
	NULL,					/* get_threads */
	mdb_server_get_application,		/* get_application */
	NULL,					/* detach_after_fork */
	NULL,					/* push_registers */
	NULL,					/* pop_registers */
	NULL,					/* get_callback_frame */
	NULL,					/* server_ptrace_restart_notification */
	NULL,					/* get_registers_from_core_file */
	NULL,					/* get_current_pid */
	NULL					/* get_current_thread */
};
