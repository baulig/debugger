#include <mdb-server-linux.h>
#include <mdb-inferior.h>
#include <mdb-process.h>
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

#if defined(__i386__) || defined(__x86_64__)
#include <x86-arch.h>

#define struct_offsetof(s_name,n_name) (size_t)(char *)&(((s_name*)0)->n_name)
#elif defined(__arm__)
#include <arm-arch.h>
#else
#error "Unknown Architecture."
#endif

class PTraceInferior;

class PTraceProcess : public MdbProcess
{
public:
	PTraceProcess (MdbServer *server) : MdbProcess (server)
	{ }

	void InitializeProcess (MdbInferior *inferior);

private:
	bool Initialize (int pid);

	friend class PTraceInferior;
};

class PTraceInferior : public MdbInferior
{
public:
	PTraceInferior (MdbServer *server)
		: MdbInferior (server)
	{
		this->pid = -1;
	}

	PTraceInferior (MdbServer *server, PTraceProcess *process, int pid, bool stopped)
		: MdbInferior (server)
	{
		this->process = process;
		this->pid = pid;

		if (!stopped)
			WaitForNewThread ();

		SetupInferior ();
	}

	MdbProcess *GetProcess (void)
	{
		return process;
	}

	//
	// MdbInferior
	//

	ErrorCode Spawn (const gchar *working_directory,
			 const gchar **argv, const gchar **envp,
			 MdbProcess **out_process, guint32 *out_thread_id,
			 gchar **out_error);

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

	ServerEvent *HandleLinuxWaitEvent (int status);

	int GetPid (void)
	{
		return pid;
	}

protected:
	ErrorCode SetupInferior (void);

	ErrorCode InitializeProcess (void);

	bool WaitForNewThread (void);

	ErrorCode CheckErrno (void);

#if defined(__i386__) || defined(__x86_64__)
	ErrorCode GetDr (int regnum, gsize *value);
	ErrorCode SetDr (int regnum, gsize value);
#endif

private:
#if HAVE_OPEN64
	int mem_fd;
#endif

	PTraceProcess *process;

	int pid;
	bool stepping;
};

MdbInferior *
mdb_inferior_new (MdbServer *server)
{
	return new PTraceInferior (server);
}

void
MdbInferior::Initialize (void)
{ }

ServerType
MdbInferior::GetServerType (void)
{
	return SERVER_TYPE_LINUX_PTRACE;
}

ServerCapabilities
MdbInferior::GetCapabilities (void)
{
	return (ServerCapabilities) (SERVER_CAPABILITIES_THREAD_EVENTS | SERVER_CAPABILITIES_CAN_DETACH_ANY | SERVER_CAPABILIITES_HAS_SIGNALS);
}

ArchType
MdbInferior::GetArchType (void)
{
#if defined(__i386__)
	return ARCH_TYPE_I386;
#elif defined(__x86_64__)
	return ARCH_TYPE_X86_64;
#elif defined(__arm__)
	return ARCH_TYPE_ARM;
#else
#error "Unknown architecture"
#endif
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

ErrorCode
PTraceInferior::Spawn (const gchar *working_directory, const gchar **argv, const gchar **envp,
		       MdbProcess **out_process, guint32 *out_thread_id, gchar **out_error)
{
	int fd[2], ret, len, i;
	ErrorCode result;

	if (out_error)
		*out_error = NULL;
	*out_process = NULL;
	*out_thread_id = 0;

	pipe (fd);

	pid = fork ();
	if (pid == 0) {
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

		if (ptrace (PTRACE_TRACEME, getpid (), NULL, 0))
			g_error (G_STRLOC ": Can't PT_TRACEME: %s", g_strerror (errno));

		execve (argv [0], (char **) argv, (char **) envp);

		error_message = g_strdup_printf ("Cannot exec `%s': %s", argv [0], g_strerror (errno));
		len = strlen (error_message) + 1;
		write (fd [1], &len, sizeof (len));
		write (fd [1], error_message, len);
		_exit (1);
	} else if (pid < 0) {
		close (fd [0]);
		close (fd [1]);

		if (out_error)
			*out_error = g_strdup_printf ("fork() failed: %s", g_strerror (errno));
		return ERR_CANNOT_START_TARGET;
	}

	close (fd [1]);

	ret = read (fd [0], &len, sizeof (len));

	if (ret != 0) {
		g_assert (ret == 4);

		if (out_error) {
			*out_error = (gchar *) g_malloc0 (len);
			read (fd [0], *out_error, len);
		}
		close (fd [0]);
		return ERR_CANNOT_START_TARGET;
	}

	close (fd [0]);

	if (!WaitForNewThread ())
		return ERR_INTERNAL_ERROR;

	result = SetupInferior ();
	if (result)
		return result;

	process = new PTraceProcess (server);

	if (!process->Initialize (pid))
		return ERR_CANNOT_START_TARGET;

	*out_process = process;
	*out_thread_id = pid;

	return ERR_NONE;
}

ErrorCode
PTraceInferior::SetupInferior (void)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", pid);

	// mdb_server_remove_hardware_breakpoints (handle);

#if HAVE_OPEN64
	mem_fd = open64 (filename, O_RDONLY);

	if (mem_fd < 0) {
		if (errno == EACCES)
			return ERR_PERMISSION_DENIED;

		g_warning (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}
#endif

	g_free (filename);
	return ERR_NONE;
}

bool
PTraceInferior::WaitForNewThread (void)
{
	guint32 ret = 0;
	int status;

#if DEBUG_WAIT
	g_message (G_STRLOC ": WaitForNewThread(): %d", pid);
#endif

	ret = waitpid (pid, &status, WUNTRACED | __WALL | __WCLONE);

#if DEBUG_WAIT
	g_message (G_STRLOC ": WaitForNewThread(): %d - %d / %x", pid, ret, status);
#endif

	/*
	 * Safety check: make sure we got the correct event.
	 */

	if ((ret != pid) || !WIFSTOPPED (status) ||
	    ((WSTOPSIG (status) != SIGSTOP) && (WSTOPSIG (status) != SIGTRAP))) {
		g_warning (G_STRLOC ": Wait failed: %d, got pid %d, status %x", pid, ret, status);
		return false;
	}

	/*
	 * Just as an extra safety check.
	 */

	if (arch->GetRegisters ()) {
		g_warning (G_STRLOC ": Failed to get registers: %d", pid);
		return false;
	}

	return true;
}

ErrorCode
PTraceInferior::InitializeProcess (void)
{
	int flags = PTRACE_O_TRACECLONE | PTRACE_O_TRACEFORK | PTRACE_O_TRACEVFORK |
		PTRACE_O_TRACEEXEC;

	if (ptrace (PTRACE_SETOPTIONS, pid, 0, GINT_TO_POINTER (flags))) {
		g_warning (G_STRLOC ": Can't PTRACE_SETOPTIONS %d: %s", pid, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}

	return ERR_NONE;
}

ErrorCode
PTraceInferior::GetSignalInfo (SignalInfo **sinfo_out)
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

#if defined(__SIGRTMIN)
	/* __SIGRTMIN is the hard limit from the kernel, SIGRTMIN is the first
	 * user-visible real-time signal.  __SIGRTMIN and __SIGRTMIN+1 are used
	 * internally by glibc. */
	sinfo->kernel_sigrtmin = __SIGRTMIN;
#else
	sinfo->kernel_sigrtmin = -1;
#endif
	sinfo->mono_thread_abort = -1;

	*sinfo_out = sinfo;

	return ERR_NONE;
}

ErrorCode
PTraceInferior::GetApplication (gchar **out_exe_file, gchar **out_cwd,
				guint32 *out_nargs, gchar ***out_cmdline_args)
{
	gchar *exe_filename = g_strdup_printf ("/proc/%d/exe", pid);
	gchar *cwd_filename = g_strdup_printf ("/proc/%d/cwd", pid);
	gchar *cmdline_filename = g_strdup_printf ("/proc/%d/cmdline", pid);
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
			return ERR_PERMISSION_DENIED;

		g_warning (G_STRLOC ": Can't get exe file of %d: %s", pid, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}

	buffer [len] = 0;
	*out_exe_file = g_strdup (buffer);

	len = readlink (cwd_filename, buffer, BUFSIZ);
	if (len < 0) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get cwd of %d: %s", pid, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}

	buffer [len] = 0;
	*out_cwd = g_strdup (buffer);

	if (!g_file_get_contents (cmdline_filename, &cmdline, &len, NULL)) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get cmdline args of %d", pid);
		return ERR_UNKNOWN_ERROR;
	}

	array = g_ptr_array_new ();

	pos = 0;
	while (pos < len) {
		g_ptr_array_add (array, cmdline + pos);
		pos += strlen (cmdline + pos) + 1;
	}

	*out_nargs = array->len;
	*out_cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (i = 0; i < array->len; i++)
		ptr  [i] = (gchar *) g_ptr_array_index (array, i);

	g_free (cwd_filename);
	g_free (exe_filename);
	g_free (cmdline_filename);
	g_ptr_array_free (array, FALSE);
	return ERR_NONE;
}

ErrorCode
PTraceInferior::CheckErrno (void)
{
	gchar *filename;

	if (!errno)
		return ERR_NONE;
	else if (errno != ESRCH) {
		g_message (G_STRLOC ": %d - %s", pid, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}

	filename = g_strdup_printf ("/proc/%d/stat", pid);
	if (g_file_test (filename, G_FILE_TEST_EXISTS)) {
		g_free (filename);
		return ERR_NOT_STOPPED;
	}

	g_warning (G_STRLOC ": %d - %s - %d (%s)", pid, filename, errno, g_strerror (errno));
	g_free (filename);
	return ERR_NO_TARGET;
}

ErrorCode
PTraceInferior::Step (void)
{
	errno = 0;
	stepping = true;
	if (ptrace (PTRACE_SINGLESTEP, pid, (caddr_t) 1, GINT_TO_POINTER (last_signal)))
		return CheckErrno ();

	return ERR_NONE;
}

ErrorCode
PTraceInferior::Continue (void)
{
	errno = 0;
	stepping = false;
	if (ptrace (PTRACE_CONT, pid, (caddr_t) 1, GINT_TO_POINTER (last_signal)))
		return CheckErrno ();

	return ERR_NONE;
}

ErrorCode
PTraceInferior::Resume (void)
{
	if (stepping)
		return Step ();
	else
		return Continue ();
}

#if HAVE_OPEN64

ErrorCode
PTraceInferior::ReadMemory (guint64 start, guint32 size, gpointer buffer)
{
	guint8 *ptr = (guint8 *) buffer;

	while (size) {
		int ret = pread64 (mem_fd, ptr, size, start);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			else if (errno == ESRCH)
				return ERR_NOT_STOPPED;
			else if (errno == EIO)
				return ERR_MEMORY_ACCESS;
			return ERR_MEMORY_ACCESS;
		}

		size -= ret;
		ptr += ret;
	}

	return ERR_NONE;
}

#else

ErrorCode
PTraceInferior::ReadMemory (guint64 start, guint32 size, gpointer buffer)
{
	ErrorCode result;
	gsize *ptr = (gsize *) buffer;
	gsize addr = start;
	gsize temp;

	while (size >= sizeof (gsize)) {
		errno = 0;
		*ptr++ = ptrace (PTRACE_PEEKDATA, pid, GINT_TO_POINTER (addr), NULL);
		if (errno) {
			g_message (G_STRLOC ": peek failed!");
			return CheckErrno ();
		}

		addr += sizeof (gsize);
		size -= sizeof (gsize);
	}

	if (!size)
		return ERR_NONE;

	result = ReadMemory (addr, sizeof (gsize), &temp);
	if (result)
		return result;

	memcpy (&temp, ptr, size);

	return ERR_NONE;
}

#endif

ErrorCode
PTraceInferior::WriteMemory (guint64 start, guint32 size, gconstpointer buffer)
{
	ErrorCode result;
	const gsize *ptr = (const gsize *) buffer;
	guint64 addr = start;
	gsize temp;

	while (size >= sizeof (gsize)) {
		gsize word = *ptr++;

		errno = 0;
		if (ptrace (PTRACE_POKEDATA, pid, GUINT_TO_POINTER (addr), GUINT_TO_POINTER (word)) != 0)
			return CheckErrno ();

		addr += sizeof (gsize);
		size -= sizeof (gsize);
	}

	if (!size)
		return ERR_NONE;

	result = ReadMemory (addr, sizeof (gsize), &temp);
	if (result)
		return result;

	memcpy (&temp, ptr, size);

	return WriteMemory (addr, sizeof (gsize), &temp);
}

ErrorCode
PTraceInferior::SetSignal (guint32 sig, gboolean send_it)
{
	if (send_it)
		kill (pid, sig);
	else
		last_signal = sig;
	return ERR_NONE;
}

ErrorCode
PTraceInferior::GetPendingSignal (guint32 *out_signal)
{
	*out_signal = last_signal;
	return ERR_NONE;
}

#if defined(__i386__) || defined(__x86_64__)

ErrorCode
PTraceInferior::GetDr (int regnum, gsize *value)
{
	gsize ret;

	errno = 0;
	ret = ptrace (PTRACE_PEEKUSER, pid, struct_offsetof (struct user, u_debugreg [regnum]));
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", pid, regnum, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}

	*value = ret;
	return ERR_NONE;
}

ErrorCode
PTraceInferior::SetDr (int regnum, gsize value)
{
	errno = 0;
	ptrace (PTRACE_POKEUSER, pid, struct_offsetof (struct user, u_debugreg [regnum]), value);
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", pid, regnum, g_strerror (errno));
		return ERR_UNKNOWN_ERROR;
	}

	return ERR_NONE;
}

#endif

ErrorCode
PTraceInferior::GetRegisters (InferiorRegs *regs)
{
	ErrorCode result;
	int i;

	if (ptrace (PTRACE_GETREGS, pid, NULL, &regs->regs) != 0)
		return CheckErrno ();

#if defined(__i386__) || defined(__x86_64__)
	if (ptrace (PTRACE_GETFPREGS, pid, NULL, &regs->fpregs) != 0)
		return CheckErrno ();

	result = GetDr (DR_CONTROL, &regs->dr_control);
	if (result)
		return result;

	result = GetDr (DR_STATUS, &regs->dr_status);
	if (result)
		return result;

	for (i = 0; i < DR_NADDR; i++) {
		result = GetDr (i, &regs->dr_regs[i]);
		if (result)
			return result;
	}
#endif

	return ERR_NONE;
}

ErrorCode
PTraceInferior::SetRegisters (InferiorRegs *regs)
{
	ErrorCode result;
	int i;

	if (ptrace (PTRACE_SETREGS, pid, NULL, &regs->regs) != 0)
		return CheckErrno ();

#if defined(__i386__) || defined(__x86_64__)
	if (ptrace (PT_SETFPREGS, pid, NULL, &regs->fpregs) != 0)
		return CheckErrno ();

	result = SetDr (DR_CONTROL, regs->dr_control);
	if (result)
		return result;

	result = SetDr (DR_STATUS, regs->dr_status);
	if (result)
		return result;

	for (i = 0; i < DR_NADDR; i++) {
		result = SetDr (i, regs->dr_regs[i]);
		if (result)
			return result;
	}
#endif

	return ERR_NONE;
}

ErrorCode
PTraceInferior::GetRegisterCount (guint32 *out_count)
{
	*out_count = arch->GetRegisterCount ();
	return ERR_NONE;
}

ErrorCode
PTraceInferior::GetRegisters (guint64 *values)
{
	return arch->GetRegisterValues (values);
}

ErrorCode
PTraceInferior::SetRegisters (const guint64 *values)
{
	return arch->SetRegisterValues (values);
}

ServerEvent *
PTraceInferior::HandleLinuxWaitEvent (int status)
{
	ServerEvent *e;

	if (WIFSTOPPED (status)) {
		int stopsig;

		stopsig = WSTOPSIG (status);
		if (stopsig == SIGCONT)
			stopsig = 0;

		if (stopsig == SIGSTOP) {
			e = g_new0 (ServerEvent, 1);
			e->sender = this;
			e->type = SERVER_EVENT_INTERRUPTED;
			return e;
		}

		return arch->ChildStopped (stopsig);
	} else if (WIFEXITED (status)) {
		e = g_new0 (ServerEvent, 1);
		e->sender = this;

		e->type = SERVER_EVENT_EXITED;
		e->arg = WEXITSTATUS (status);
		return e;
	} else if (WIFSIGNALED (status)) {
		e = g_new0 (ServerEvent, 1);
		e->sender = this;

		if ((WTERMSIG (status) == SIGTRAP) || (WTERMSIG (status) == SIGKILL)) {
			e->type = SERVER_EVENT_EXITED;
			e->arg = 0;
			return e;
		} else {
			e->type = SERVER_EVENT_SIGNALED;
			e->arg = WTERMSIG (status);
			return e;
		}
	}

	g_warning (G_STRLOC ": Got unknown waitpid() result: %x", status);

	e = g_new0 (ServerEvent, 1);
	e->sender = this;

	e->type = SERVER_EVENT_UNKNOWN_ERROR;
	e->arg = status;
	return e;
}

bool
PTraceProcess::Initialize (int pid)
{
	char buffer [BUFSIZ+1];
	gchar *exe_filename;
	gsize len;

	int flags = PTRACE_O_TRACECLONE | PTRACE_O_TRACEFORK | PTRACE_O_TRACEVFORK |
		PTRACE_O_TRACEEXEC;

	if (ptrace (PTRACE_SETOPTIONS, pid, 0, GINT_TO_POINTER (flags))) {
		g_warning (G_STRLOC ": Can't PTRACE_SETOPTIONS %d: %s", pid, g_strerror (errno));
		return false;
	}

	exe_filename = g_strdup_printf ("/proc/%d/exe", pid);

	len = readlink (exe_filename, buffer, BUFSIZ);
	if (len < 0) {
		g_free (exe_filename);
		return false;
	}

	buffer [len] = 0;

	return OnMainModuleLoaded (buffer) != NULL;
}

void
PTraceProcess::InitializeProcess (MdbInferior *inferior)
{
	MdbExeReader *reader = GetMainReader ();

	if (reader)
		reader->ReadDynamicInfo (inferior);
}

MdbInferior *
MdbServerLinux::CreateThread (MdbProcess *process, int pid, bool stopped)
{
	PTraceInferior *inferior = new PTraceInferior (this, (PTraceProcess *) process, pid, stopped);

	AddInferior (pid, inferior);

	return inferior;
}
