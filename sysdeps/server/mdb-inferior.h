#ifndef __MDB_INFERIOR_H__
#define __MDB_INFERIOR_H__

#include <mdb-server.h>
#include <mdb-process.h>
#include <breakpoints.h>

typedef struct _InferiorRegs InferiorRegs;

class MdbArch;
class MdbDisassembler;
class MdbProcess;

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

	MdbExeReader *InitializeProcess (void);

	static ErrorCode GetTargetInfo (guint32 *out_target_int_size, guint32 *out_target_long_size,
					guint32 *out_target_address_size, guint32 *out_is_bigendian);

	virtual ErrorCode GetSignalInfo (SignalInfo **sinfo) = 0;

	virtual ErrorCode GetApplication (gchar **out_exe_file, gchar **out_cwd,
					  guint32 *out_nargs, gchar ***out_cmdline_args) = 0;

	virtual ErrorCode GetFrame (StackFrame *frame);

	virtual ErrorCode Step (void) = 0;

	virtual ErrorCode Continue (void) = 0;

	virtual ErrorCode Resume (void) = 0;

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

#if defined(__linux__) || defined(__FreeBSD__)
	int GetLastSignal (void)
	{
		return last_signal;
	}

	void SetLastSignal (int last_signal)
	{
		this->last_signal = last_signal;
	}

	virtual ServerEvent *HandleLinuxWaitEvent (int status) = 0;
#endif

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

protected:
	MdbInferior (MdbServer *server)
		: ServerObject (SERVER_OBJECT_KIND_INFERIOR)
	{
		this->server = server;

#if defined(__linux__) || defined(__FreeBSD__)
		last_signal = 0;
#endif

		disassembler = NULL;

		arch = mdb_arch_new (this);
	}

#if defined(__linux__) || defined(__FreeBSD__)
	int last_signal;
#endif

	MdbServer *server;
	MdbArch *arch;

	MdbDisassembler *disassembler;

	friend MdbArch *mdb_arch_new (MdbInferior *inferior);
};

#endif
