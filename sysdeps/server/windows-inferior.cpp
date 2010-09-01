#include <mdb-server-windows.h>
#include <mdb-inferior.h>
#include <x86-arch.h>
#include <string.h>
#include <assert.h>
#include <tchar.h>
#include <Psapi.h>
#include <stdio.h>
#include <stdlib.h>
#include <glib.h>

class WindowsInferior;

class WindowsProcess : public MdbProcess
{
public:
	WindowsProcess (MdbServer *server) : MdbProcess (server)
	{
		process_handle = NULL;
		process_id = 0;
		argc = 0;
		argv = NULL;
		exe_path = NULL;
	}

	void InitializeProcess (MdbInferior *inferior);

private:
	HANDLE process_handle;
	DWORD process_id;
	guint32 argc;
	gchar **argv;
	gchar *exe_path;

	ErrorCode Spawn (const gchar *working_directory,
			 const gchar **argv, const gchar **envp,
			 MdbInferior **out_inferior, guint32 *out_thread_id,
			 gchar **out_error);

	friend class WindowsInferior;
};

class WindowsInferior : public MdbInferior
{
public:
	WindowsInferior (WindowsProcess *process)
		: MdbInferior (process->server)
	{
		this->process = process;
		thread_handle = NULL;
		thread_id = 0;
	}

	MdbProcess *GetProcess (void)
	{
		return process;
	}

	//
	// MdbInferior
	//

	ErrorCode GetSignalInfo (SignalInfo **sinfo);

	ErrorCode GetApplication (gchar **out_exe_file, gchar **out_cwd,
				  guint32 *out_nargs, gchar ***out_cmdline_args);

	ErrorCode Step (void);

	ErrorCode Continue (void);

	ErrorCode Resume (void);

	ErrorCode GetRegisterCount (guint32 *out_count);

	ErrorCode GetRegisters (guint64 *values);

	ErrorCode SetRegisters (const guint64 *values);

	ErrorCode ReadMemory (guint64 start, guint32 size, gpointer buffer);

	ErrorCode WriteMemory (guint64 start, guint32 size, gconstpointer data);

	ErrorCode GetPendingSignal (guint32 *out_signo);

	ErrorCode SetSignal (guint32 signo, gboolean send_it);

	ErrorCode GetRegisters (InferiorRegs *regs);

	ErrorCode SetRegisters (InferiorRegs *regs);

	//
	// Private API.
	//

	void HandleDebugEvent (DEBUG_EVENT *de);

protected:
	ErrorCode SetStepFlag (bool on);

private:
	WindowsProcess *process;

	HANDLE thread_handle;
	DWORD thread_id;

	friend class WindowsProcess;
};

MdbProcess *
mdb_process_new (MdbServer *server)
{
	return new WindowsProcess (server);
}

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
	ret = (gchar *) g_malloc0 (len + 1);

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

	tmp_buf = (gchar *) g_malloc0 (dw_rval + 1);
	wcstombs (tmp_buf, message, 2048);

	retval = g_strdup_printf ("WINDOWS ERROR (code=%x): %s", error_code, tmp_buf);
	g_free (tmp_buf);
	return retval;
}

void
MdbInferior::Initialize (void)
{
	g_assert (!command_event);

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

bool
MdbServer::InferiorCommand (InferiorDelegate *delegate)
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

ServerType
MdbInferior::GetServerType (void)
{
	return SERVER_TYPE_WIN32;
}

ServerCapabilities
MdbInferior::GetCapabilities (void)
{
	return SERVER_CAPABILITIES_NONE;
}

ArchType
MdbInferior::GetArchType (void)
{
	return ARCH_TYPE_I386;
}

ErrorCode
MdbInferior::GetTargetInfo (guint32 *target_int_size, guint32 *target_long_size,
			    guint32 *target_address_size, guint32 *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint64);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return ERR_NONE;
}

void
MdbServerWindows::HandleDebugEvent (DEBUG_EVENT *de)
{
	WindowsInferior *inferior;

	inferior = (WindowsInferior *) WindowsProcess::GetInferiorByThreadId (de->dwThreadId);
	if (!inferior) {
		g_warning (G_STRLOC ": Got debug event for unknown thread: %d/%d", de->dwProcessId, de->dwThreadId);
		if (!ContinueDebugEvent (de->dwProcessId, de->dwThreadId, DBG_CONTINUE)) {
			g_warning (G_STRLOC ": %s", get_last_error ());
		}
		return;
	}

	g_message (G_STRLOC ": Got debug event: %d - %d/%d - %p", de->dwDebugEventCode, de->dwProcessId, de->dwThreadId, inferior);

	show_debug_event (de);

	inferior->HandleDebugEvent (de);
}

void
WindowsInferior::HandleDebugEvent (DEBUG_EVENT *de)
{
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

			e = arch->ChildStopped (0);
			if (e) {
				server->SendEvent (e);
				g_free (e);
			} else
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

		if (!process->exe_path) {
			/*
			 * This fails until kernel32.dll is loaded.
			 */
			if (GetModuleFileNameEx (process->process_handle, NULL, path, sizeof (path) / sizeof (TCHAR))) {
				process->exe_path = tstring_to_string (path);
				process->OnMainModuleLoaded (process->exe_path);
			}
		}

		if (de->u.LoadDll.lpImageName) {
			char buf [1024];
			DWORD exc_code;

			if (ReadMemory ((gsize) de->u.LoadDll.lpImageName, 4, &exc_code))
				break;

			if (!exc_code)
				break;

			if (de->u.LoadDll.fUnicode) {
				wchar_t w_buf [1024];
				size_t ret;

				if (ReadMemory (exc_code, 300, w_buf))
					break;

				ret = wcstombs (buf, w_buf, 300);
			} else {
				if (ReadMemory (exc_code, 300, buf))
					break;
			}

			process->OnDllLoaded (buf);
			break;
		}
		break;
	}

	case EXIT_PROCESS_DEBUG_EVENT: {
		ServerEvent *e = g_new0 (ServerEvent, 1);

		e->sender = this;
		e->arg = de->u.ExitProcess.dwExitCode;
		e->type = SERVER_EVENT_EXITED;
		server->SendEvent (e);
		g_free (e);
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
				MdbServerWindows::HandleDebugEvent (&de);
			}
		} else {
			g_warning (G_STRLOC ": WaitForMultipleObjects() returned %d", ret);
		}
	}

	return 0;
}

ErrorCode
WindowsProcess::Spawn (const gchar *working_directory, const gchar **arg_argv, const gchar **arg_envp,
		       MdbInferior **out_inferior, guint32 *out_thread_id, gchar **out_error)
{
	WindowsInferior *inferior;
	wchar_t* utf16_argv = NULL;
	wchar_t* utf16_envp = NULL;
	wchar_t* utf16_working_directory = NULL;
	STARTUPINFO si = {0};
	PROCESS_INFORMATION pi = {0};
	SECURITY_ATTRIBUTES sa;
	SECURITY_DESCRIPTOR sd;
	LPSECURITY_ATTRIBUTES lpsa = NULL;
	DEBUG_EVENT de;
	BOOL b_ret;

	if (out_error)
		*out_error = NULL;
	*out_thread_id = 0;

	if (working_directory) {
		gunichar2* utf16_dir_tmp;
		guint len;

		len = strlen (working_directory);

		utf16_dir_tmp = g_utf8_to_utf16 (working_directory, -1, NULL, NULL, NULL);

		utf16_working_directory = (wchar_t *) g_malloc0 ((len+2)*sizeof (wchar_t));
		_snwprintf (utf16_working_directory, len, L"%s ", utf16_dir_tmp);
		g_free (utf16_dir_tmp);
	}

	if (arg_envp) {
		guint len = 0;
		const gchar** envp_temp = arg_envp;
		wchar_t* envp_concat;

		while (*envp_temp) {
			len += strlen (*envp_temp) + 1;
			envp_temp++;
		}
		len++; /* add one for double NULL at end */
		envp_concat = utf16_envp = (wchar_t *) g_malloc0 (len*sizeof (wchar_t));

		envp_temp = arg_envp;
		while (*envp_temp) {
			gunichar2* utf16_envp_temp = g_utf8_to_utf16 (*envp_temp, -1, NULL, NULL, NULL);
			int written = _snwprintf (envp_concat, len, L"%s%s", utf16_envp_temp, L"\0");
			g_free (utf16_envp_temp);
			envp_concat += written + 1;
			len -= written;
			envp_temp++;
		}
		_snwprintf (envp_concat, len, L"%s", L"\0"); /* double NULL at end */
	}

	if (arg_argv) {
		guint len = 0;
		gint index = 0;
		const gchar** argv_temp = arg_argv;
		wchar_t* argv_concat;

		while (*argv_temp) {
			len += strlen (*argv_temp) + 1;
			argv_temp++;
			argc++;
		}
		argv = (gchar **) g_malloc0 ( (argc+1) * sizeof (gpointer));
		argv_concat = utf16_argv = (wchar_t *) g_malloc0 (len*sizeof (wchar_t));

		argv_temp = arg_argv;
		while (*argv_temp) {
			gunichar2* utf16_argv_temp = g_utf8_to_utf16 (*argv_temp, -1, NULL, NULL, NULL);
			int written = _snwprintf (argv_concat, len, L"%s ", utf16_argv_temp);
			argv [index++] = g_strdup (*argv_temp);
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
		return ERR_CANNOT_START_TARGET;
	}

	if (!WaitForDebugEvent (&de, 5000)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return ERR_CANNOT_START_TARGET;
	}

	if (de.dwDebugEventCode != CREATE_PROCESS_DEBUG_EVENT) {
		g_warning (G_STRLOC ": Got unknown debug event: %d", de.dwDebugEventCode);
		return ERR_CANNOT_START_TARGET;
	}

	process_handle = pi.hProcess;
	process_id = pi.dwProcessId;

	inferior = new WindowsInferior (this);

	inferior->thread_handle = pi.hThread;
	inferior->thread_id = pi.dwThreadId;

	AddInferior (pi.dwThreadId, inferior);
	main_process = this;

	g_message (G_STRLOC ": SPAWN: %d/%d", pi.dwProcessId, pi.dwThreadId);

	*out_inferior = inferior;
	*out_thread_id = pi.dwThreadId;

	return ERR_NONE;
}

ErrorCode
WindowsInferior::GetRegisters (InferiorRegs *regs)
{
	memset (regs, 0, sizeof (regs));
	regs->context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS |
		CONTEXT_FLOATING_POINT | CONTEXT_EXTENDED_REGISTERS | CONTEXT_DEBUG_REGISTERS;

	if (!GetThreadContext (thread_handle, &regs->context)) {
		g_warning (G_STRLOC ": get_registers: %s", get_last_error ());
		return ERR_MEMORY_ACCESS;
	}

	regs->dr_status = regs->context.Dr6;
	regs->dr_control = regs->context.Dr7;
	regs->dr_regs[0] = regs->context.Dr0;
	regs->dr_regs[1] = regs->context.Dr1;
	regs->dr_regs[2] = regs->context.Dr2;
	regs->dr_regs[3] = regs->context.Dr3;

	return ERR_NONE;
}

ErrorCode
WindowsInferior::SetRegisters (InferiorRegs *regs)
{
	regs->context.Dr6 = regs->dr_status;
	regs->context.Dr7 = regs->dr_control;
	regs->context.Dr0 = regs->dr_regs[0];
	regs->context.Dr1 = regs->dr_regs[1];
	regs->context.Dr2 = regs->dr_regs[2];
	regs->context.Dr3 = regs->dr_regs[3];

	regs->context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS |
		CONTEXT_FLOATING_POINT | CONTEXT_EXTENDED_REGISTERS | CONTEXT_DEBUG_REGISTERS;

	if (!SetThreadContext (thread_handle, &regs->context))
		return ERR_MEMORY_ACCESS;

	return ERR_NONE;
}

ErrorCode
WindowsInferior::ReadMemory (guint64 start, guint32 size, gpointer buffer)
{
	SetLastError (0);
	if (!ReadProcessMemory (process->process_handle, GINT_TO_POINTER ((guint) start), buffer, size, NULL)) {
		g_warning (G_STRLOC ": %lx/%d - %s", (gsize) start, size, get_last_error ());
		return ERR_MEMORY_ACCESS;
	}

	return ERR_NONE;
}

ErrorCode
WindowsInferior::WriteMemory (guint64 start, guint32 size, gconstpointer buffer)
{
	SetLastError (0);
	if (!WriteProcessMemory (process->process_handle, GINT_TO_POINTER ((guint32) start), buffer, size, NULL)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return ERR_MEMORY_ACCESS;
	}

	if (!FlushInstructionCache (process->process_handle, GINT_TO_POINTER ((guint32) start), size)) {
		g_warning (G_STRLOC ": %s", get_last_error ());
		return ERR_MEMORY_ACCESS;
	}

	return ERR_NONE;
}

ErrorCode
WindowsInferior::SetStepFlag (bool on)
{
	CONTEXT context;

	context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS | CONTEXT_FLOATING_POINT;

	if (!GetThreadContext (thread_handle, &context)) {
		g_warning (G_STRLOC ": GetThreadContext() failed");
		return ERR_MEMORY_ACCESS;
	}

        if (on)
		context.EFlags |= TF_BIT;
        else
		context.EFlags &= ~TF_BIT;

	if (!SetThreadContext (thread_handle, &context)) {
		g_warning (G_STRLOC ": SetThreadContext() failed");
		return ERR_MEMORY_ACCESS;
	}

	return ERR_NONE;
}

ErrorCode
WindowsInferior::Step (void)
{
	ErrorCode result;

	result = SetStepFlag (TRUE);
	if (result)
		return result;

	if (!ContinueDebugEvent (process->process_id, thread_id, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": step (%d/%d): %s", process->process_id, thread_id, get_last_error ());
		return ERR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);
	return ERR_NONE;
}

ErrorCode
WindowsInferior::Continue (void)
{
	ErrorCode result;

	result = SetStepFlag (FALSE);
	if (result)
		return result;

	if (!ContinueDebugEvent (process->process_id, thread_id, DBG_CONTINUE)) {
		g_warning (G_STRLOC ": continue (%d/%d): %s", process->process_id, thread_id, get_last_error ());
		return ERR_UNKNOWN_ERROR;
	}

	SetEvent (wait_event);
	return ERR_NONE;
}

ErrorCode
WindowsInferior::GetApplication (gchar **out_exe_file, gchar **out_cwd,
				 guint32 *out_nargs, gchar ***out_cmdline_args)
{
	guint32 index = 0;
	GPtrArray *array;
	gchar **ptr;

	/* No supported way to get command line of a process
	   see http://blogs.msdn.com/oldnewthing/archive/2009/02/23/9440784.aspx */

/*	gunichar2 utf16_exe_file [1024];
	gunichar2 utf16_cmd_line [10240];
	gunichar2 utf16_env_vars [10240];
	BOOL ret;
	if (!GetModuleFileNameEx (handle->process_handle, NULL, utf16_exe_file, sizeof (utf16_exe_file)/sizeof (utf16_exe_file [0]))) {
		DWORD error = GetLastError ();
		return COMMAND_ERROR_INTERNAL_ERROR;
	}
	*/
	*out_exe_file = g_strdup (process->argv [0]);
	*out_nargs = process->argc;
	*out_cwd = NULL;

	array = g_ptr_array_new ();

	for (index = 0; index < process->argc; index++)
		g_ptr_array_add (array, process->argv [index]);

	*out_cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (index = 0; index < array->len; index++)
		ptr  [index] = (gchar *) g_ptr_array_index (array, index);

	g_ptr_array_free (array, FALSE);

	return ERR_NONE;
}

ErrorCode
WindowsInferior::GetSignalInfo (SignalInfo **sinfo)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
WindowsInferior::Resume (void)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
WindowsInferior::GetRegisterCount (guint32 *out_count)
{
	*out_count = arch->GetRegisterCount ();
	return ERR_NONE;
}

ErrorCode
WindowsInferior::GetRegisters (guint64 *values)
{
	return arch->GetRegisterValues (values);
}

ErrorCode
WindowsInferior::SetRegisters (const guint64 *values)
{
	return arch->SetRegisterValues (values);
}

ErrorCode
WindowsInferior::GetPendingSignal (guint32 *out_signo)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
WindowsInferior::SetSignal (guint32 signo, gboolean send_it)
{
	return ERR_NOT_IMPLEMENTED;
}

void
WindowsProcess::InitializeProcess (MdbInferior *inferior)
{
}
