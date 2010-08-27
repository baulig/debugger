#ifndef __MDB_SERVER_H__
#define __MDB_SERVER_H__

#include <config.h>
#include <glib.h>

typedef enum {
	ERR_NONE = 0,

	ERR_UNKNOWN_ERROR,
	ERR_INTERNAL_ERROR,
	ERR_NO_TARGET,
	ERR_ALREADY_HAVE_TARGET,
	ERR_CANNOT_START_TARGET,
	ERR_NOT_STOPPED,
	ERR_ALREADY_STOPPED,
	ERR_RECURSIVE_CALL,
	ERR_NO_SUCH_BREAKPOINT,
	ERR_NO_SUCH_REGISTER,
	ERR_DR_OCCUPIED,
	ERR_MEMORY_ACCESS,
	ERR_NOT_IMPLEMENTED = 0x1002,
	ERR_IO_ERROR,
	ERR_NO_CALLBACK_FRAME,
	ERR_PERMISSION_DENIED,

	ERR_TARGET_ERROR_MASK = 0x0fff,

	ERR_NO_SUCH_INFERIOR = 0x1001,
	ERR_NO_SUCH_BPM = 0x1002,
	ERR_NO_SUCH_EXE_READER = 0x1003,
	ERR_CANNOT_OPEN_EXE = 0x1004,
} ErrorCode;

typedef enum {
	SERVER_CAPABILITIES_NONE		= 0,
	SERVER_CAPABILITIES_THREAD_EVENTS	= 1,
	SERVER_CAPABILITIES_CAN_DETACH_ANY	= 2,
	SERVER_CAPABILIITES_HAS_SIGNALS		= 4
} ServerCapabilities;

typedef enum {
	SERVER_TYPE_UNKNOWN			= 0,
	SERVER_TYPE_LINUX_PTRACE		= 1,
	SERVER_TYPE_DARWIN			= 2,
	SERVER_TYPE_WIN32			= 3
} ServerType;

typedef enum {
	ARCH_TYPE_UNKNOWN			= 0,
	ARCH_TYPE_I386				= 1,
	ARCH_TYPE_X86_64			= 2,
	ARCH_TYPE_ARM				= 3
} ArchType;

typedef enum {
	EVENT_KIND_TARGET_EVENT = 1
} EventKind;

typedef struct {
	void (* func) (gpointer user_data);
	gpointer user_data;
} InferiorDelegate;

typedef enum {
	SERVER_EVENT_NONE,
	SERVER_EVENT_UNKNOWN_ERROR = 1,
	SERVER_EVENT_CHILD_EXITED = 2,
	SERVER_EVENT_CHILD_STOPPED,
	SERVER_EVENT_CHILD_SIGNALED,
	SERVER_EVENT_CHILD_CALLBACK,
	SERVER_EVENT_CHILD_CALLBACK_COMPLETED,
	SERVER_EVENT_CHILD_HIT_BREAKPOINT,
	SERVER_EVENT_CHILD_MEMORY_CHANGED,
	SERVER_EVENT_CHILD_CREATED_THREAD,
	SERVER_EVENT_CHILD_FORKED,
	SERVER_EVENT_CHILD_EXECD,
	SERVER_EVENT_CHILD_CALLED_EXIT,
	SERVER_EVENT_CHILD_NOTIFICATION,
	SERVER_EVENT_CHILD_INTERRUPTED,
	SERVER_EVENT_RUNTIME_INVOKE_DONE,
	SERVER_EVENT_INTERNAL_ERROR
} ServerEventType;

typedef struct {
	ServerEventType type;
	guint32 sender_iid;
	guint32 opt_arg_iid;
	guint64 arg;
	guint64 data1;
	guint64 data2;
	guint32 opt_data_size;
	gpointer opt_data;
} ServerEvent;

typedef struct {
	guint64 address;
	guint64 stack_pointer;
	guint64 frame_address;
} StackFrame;

typedef struct {
	int sigkill;
	int sigstop;
	int sigint;
	int sigchld;
	int sigfpe;
	int sigquit;
	int sigabrt;
	int sigsegv;
	int sigill;
	int sigbus;
	int sigwinch;
	int kernel_sigrtmin;
	int mono_thread_abort;
} SignalInfo;

class MdbInferior;
class MdbExeReader;
class MdbDisassembler;

class MdbServer
{
public:
	static gboolean Initialize (void);

	MdbExeReader *GetExeReader (const char *filename);

	MdbDisassembler *GetDisassembler (MdbInferior *inferior);

	void ProcessChildEvent (ServerEvent *e);

protected:
	MdbExeReader *main_reader;
	GHashTable *exe_file_hash;

	void MainLoop (void);
	gboolean MainLoopIteration (void);
	gboolean InferiorCommand (InferiorDelegate *delegate);

	MdbInferior *GetInferiorByPid (int pid);

#if defined(__linux__)
	ServerEvent *HandleLinuxWaitEvent (void);
#endif

private:
	int conn_fd;

	MdbServer (int conn_fd)
	{
		this->conn_fd = conn_fd;
		exe_file_hash = g_hash_table_new (NULL, NULL);
	}

	friend int main (int argc, char *argv []);
};

#endif
