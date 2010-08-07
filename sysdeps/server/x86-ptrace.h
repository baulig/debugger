#ifndef __MONO_DEBUGGER_X86_64_PTRACE_H__
#define __MONO_DEBUGGER_X86_64_PTRACE_H__

typedef struct OSData OSData;

struct InferiorHandle
{
	OSData os;

	guint32 pid;
	int stepping;
	int last_signal;
	int redirect_fds;
	int output_fd [2], error_fd [2];
	int is_thread;
};

#ifndef PTRACE_SETOPTIONS
#define PTRACE_SETOPTIONS	0x4200
#endif
#ifndef PTRACE_GETEVENTMSG
#define PTRACE_GETEVENTMSG	0x4201
#endif

#ifndef PTRACE_EVENT_FORK

/* options set using PTRACE_SETOPTIONS */
#define PTRACE_O_TRACESYSGOOD	0x00000001
#define PTRACE_O_TRACEFORK	0x00000002
#define PTRACE_O_TRACEVFORK	0x00000004
#define PTRACE_O_TRACECLONE	0x00000008
#define PTRACE_O_TRACEEXEC	0x00000010
#define PTRACE_O_TRACEVFORKDONE	0x00000020
#define PTRACE_O_TRACEEXIT	0x00000040

/* Wait extended result codes for the above trace options.  */
#define PTRACE_EVENT_FORK	1
#define PTRACE_EVENT_VFORK	2
#define PTRACE_EVENT_CLONE	3
#define PTRACE_EVENT_EXEC	4
#define PTRACE_EVENT_VFORKDONE	5
#define PTRACE_EVENT_EXIT	6

#endif /* PTRACE_EVENT_FORK */

static ServerCommandError
_server_ptrace_check_errno (InferiorHandle *);

static ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *handle);

static void
_server_ptrace_finalize_inferior (ServerHandle *handle);

static gboolean
_server_ptrace_wait_for_new_thread (ServerHandle *handle);

static void
_server_ptrace_io_thread_main (IOThreadData *io_data, ChildOutputFunc func);

#endif
