#ifndef __MONO_DEBUGGER_X86_64_PTRACE_H__
#define __MONO_DEBUGGER_X86_64_PTRACE_H__

#include <mdb-server.h>
#include <mdb-server-bfd.h>

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

	MdbDisassembler *disassembler;
	MdbExeReader *main_bfd;
};

#include "linux-ptrace.h"

static ServerCommandError
_server_ptrace_check_errno (ServerHandle *server);

static ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *server);

static void
_server_ptrace_finalize_inferior (ServerHandle *server);

#ifndef MDB_SERVER

static void
_server_ptrace_io_thread_main (IOThreadData *io_data, ChildOutputFunc func);

#endif

#endif
