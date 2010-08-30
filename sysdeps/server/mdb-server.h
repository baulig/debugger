#ifndef __MDB_SERVER_H__
#define __MDB_SERVER_H__

#include <config.h>
#include <glib.h>

#include <server-object.h>
#include <breakpoints.h>
#include <connection.h>

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

typedef struct {
	void (* func) (gpointer user_data);
	gpointer user_data;
} InferiorDelegate;

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

class MdbServer : public ServerObject
{
public:
	static gboolean Initialize (void);

	MdbExeReader *GetExeReader (const char *filename);

	MdbDisassembler *GetDisassembler (MdbInferior *inferior);

	void SendEvent (ServerEvent *e);

	MdbInferior *GetInferiorByPid (int pid);

#if WINDOWS
	bool InferiorCommand (InferiorDelegate *delegate);
#endif

	ErrorCode Spawn (const gchar *working_directory,
			 const gchar **argv, const gchar **envp,
			 MdbInferior **out_inferior, int *out_child_pid,
			 gchar **out_error);

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

protected:
	MdbExeReader *main_reader;
	GHashTable *exe_file_hash;

	BreakpointManager *bpm;

	void MainLoop (int conn_fd);
	gboolean MainLoopIteration (void);

#if defined(__linux__)
	ServerEvent *HandleLinuxWaitEvent (void);
#endif

private:
	Connection *connection;

	MdbServer (Connection *connection) : ServerObject (SERVER_OBJECT_KIND_SERVER)
	{
		this->connection = connection;
		exe_file_hash = g_hash_table_new (NULL, NULL);
		main_reader = NULL;

		bpm = new BreakpointManager ();
	}

	friend int main (int argc, char *argv []);
};

#endif
