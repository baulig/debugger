#ifndef __MONO_DEBUGGER_X86_64_PTRACE_H__
#define __MONO_DEBUGGER_X86_64_PTRACE_H__

#include <bfd.h>
#include <dis-asm.h>

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

	struct disassemble_info *disassembler;
	char disasm_buffer [1024];
};

#include "linux-ptrace.h"

static ServerCommandError
_server_ptrace_check_errno (InferiorHandle *);

static ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *handle);

static void
_server_ptrace_finalize_inferior (ServerHandle *handle);

static gboolean
_server_ptrace_wait_for_new_thread (ServerHandle *handle);

#ifndef MDB_SERVER

static void
_server_ptrace_io_thread_main (IOThreadData *io_data, ChildOutputFunc func);

#endif

#endif
