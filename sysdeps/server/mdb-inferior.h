#ifndef __MDB_INFERIOR_H__
#define __MDB_INFERIOR_H__

#include <mdb-server.h>
#include <mdb-process.h>
#include <breakpoints.h>

typedef struct _InferiorRegs InferiorRegs;

class MdbArch;
class MdbDisassembler;
class MdbProcess;

class InferiorCallback {
public:
	void Invoke (MdbInferior *inferior, gsize arg1, gsize arg2)
	{
		func (inferior, arg1, arg2, user_data);
	}

	InferiorCallback (void (*func) (MdbInferior *, gsize, gsize, gpointer), gpointer user_data)
	{
		this->func = func;
		this->user_data = user_data;
	}
protected:
	gpointer user_data;
	void (* func) (MdbInferior *, gsize, gsize, gpointer);
};

enum InvocationType {
	INVOCATION_TYPE_LONG_LONG = 1,
	INVOCATION_TYPE_LONG_LONG_LONG_STRING,
	INVOCATION_TYPE_DATA,
	INVOCATION_TYPE_LONG_LONG_DATA,
	INVOCATION_TYPE_CONTEXT		= 8,
	INVOCATION_TYPE_RUNTIME_INVOKE
};

typedef struct {
	InvocationType type;
	guint64 method_address;
	guint64 callback_id;
	guint64 arg1;
	guint64 arg2;
	guint64 arg3;
	gchar *string_arg;
	int data_size;
	const guint8* data;
	InferiorCallback *callback;
} InvocationData;

class MdbInferior : public ServerObject
{
public:
	MdbServer *GetServer (void) { return server; }
	MdbArch *GetArch (void) { return arch; }
	virtual MdbProcess *GetProcess (void) = 0;

	static void Initialize (void);

	static ServerType GetServerType (void);
	static ServerCapabilities GetCapabilities (void);
	static ArchType GetArchType (void);

	static ErrorCode GetTargetInfo (guint32 *out_target_int_size, guint32 *out_target_long_size,
					guint32 *out_target_address_size, guint32 *out_is_bigendian);

	virtual ErrorCode GetSignalInfo (SignalInfo **sinfo) = 0;

	virtual ErrorCode GetApplication (gchar **out_exe_file, gchar **out_cwd,
					  guint32 *out_nargs, gchar ***out_cmdline_args) = 0;

	virtual ErrorCode GetFrame (StackFrame *frame);

	virtual ErrorCode Step (void) = 0;

	virtual ErrorCode Continue (void) = 0;

	virtual ErrorCode ResumeStepping (void) = 0;

	ErrorCode InsertBreakpoint (guint64 address, BreakpointInfo **out_breakpoint);

	BreakpointInfo *LookupBreakpointById (guint32 idx);

	ErrorCode EnableBreakpoint (BreakpointInfo *breakpoint);

	ErrorCode DisableBreakpoint (BreakpointInfo *breakpoint);
	
	ErrorCode RemoveBreakpoint (BreakpointInfo *breakpoint);

	virtual ErrorCode GetRegisterCount (guint32 *out_count) = 0;

	virtual ErrorCode GetRegisters (guint64 *values) = 0;

	virtual ErrorCode SetRegisters (const guint64 *values) = 0;

	virtual ErrorCode ReadMemory (guint64 start, guint32 size, gpointer buffer) = 0;

	virtual ErrorCode WriteMemory (guint64 start, guint32 size, gconstpointer data) = 0;

	virtual ErrorCode PeekWord (guint64 address, gsize *word)
	{
		return ReadMemory (address, sizeof (gsize), word);
	}

	virtual ErrorCode PokeWord (guint64 address, gsize word)
	{
		return WriteMemory (address, sizeof (gsize), &word);
	}

	gchar *ReadString (guint64 address);

	virtual ErrorCode GetPendingSignal (guint32 *out_signo) = 0;

	virtual ErrorCode SetSignal (guint32 signo, gboolean send_it) = 0;

	gchar *DisassembleInstruction (guint64 address, guint32 *out_insn_size);

	virtual ErrorCode GetRegisters (InferiorRegs *regs) = 0;

	virtual ErrorCode SetRegisters (InferiorRegs *regs) = 0;

	virtual ErrorCode Stop (void) = 0;

	virtual ErrorCode SuspendThread (void) = 0;

	virtual ErrorCode ResumeThread (void) = 0;

#if defined(__linux__) || defined(__FreeBSD__)
	int GetLastSignal (void)
	{
		return last_signal;
	}

	void SetLastSignal (int last_signal)
	{
		this->last_signal = last_signal;
	}

	virtual ServerEvent *HandleLinuxWaitEvent (int status, bool *out_remain_stopped) = 0;
#endif

	virtual ErrorCode CallMethod (InvocationData *data) = 0;

	int GetPid (void)
	{
		return pid;
	}

	gsize GetTid (void)
	{
		return tid;
	}

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

protected:
	MdbInferior (MdbServer *server, int pid, gsize tid)
		: ServerObject (SERVER_OBJECT_KIND_INFERIOR)
	{
		this->server = server;
		this->pid = pid;
		this->tid = tid;

#if defined(__linux__) || defined(__FreeBSD__)
		last_signal = 0;
#endif

		disassembler = NULL;

		arch = mdb_arch_new (this);
	}

#if defined(__linux__) || defined(__FreeBSD__)
	int last_signal;
#endif

	int pid;
	gsize tid;

	MdbServer *server;
	MdbArch *arch;

	MdbDisassembler *disassembler;

	friend MdbArch *mdb_arch_new (MdbInferior *inferior);
};

#endif
