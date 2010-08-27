#ifndef __MDB_INFERIOR_H__
#define __MDB_INFERIOR_H__

#include <mdb-server.h>
#include <breakpoints.h>

typedef struct _InferiorRegs InferiorRegs;

class MdbArch;
class MdbDisassembler;

class MdbInferior
{
public:
	int GetID (void) { return iid; }
	MdbServer *GetServer (void) { return server; }
	BreakpointManager *GetBreakpointManager (void) { return bpm; }
	MdbArch *GetArch (void) { return arch; }

	static void Initialize (void);

	static ServerType GetServerType (void);
	static ServerCapabilities GetCapabilities (void);
	static ArchType GetArchType (void);

	static ErrorCode GetTargetInfo (guint32 *out_target_int_size, guint32 *out_target_long_size,
					guint32 *out_target_address_size, guint32 *out_is_bigendian);

	virtual ErrorCode Spawn (const gchar *working_directory,
				 const gchar **argv, const gchar **envp,
				 gint *out_child_pid, gchar **out_error) = 0;

	virtual ErrorCode InitializeProcess (void) = 0;

	virtual ErrorCode GetSignalInfo (SignalInfo **sinfo) = 0;

	virtual ErrorCode GetApplication (gchar **out_exe_file, gchar **out_cwd,
					  guint32 *out_nargs, gchar ***out_cmdline_args) = 0;

	virtual ErrorCode GetFrame (StackFrame *frame);

	virtual ErrorCode Step (void) = 0;

	virtual ErrorCode Continue (void) = 0;

	virtual ErrorCode Resume (void) = 0;

	ErrorCode InsertBreakpoint (guint64 address, guint32 *out_idx);

	ErrorCode EnableBreakpoint (guint32 idx);

	ErrorCode DisableBreakpoint (guint32 idx);
	
	ErrorCode RemoveBreakpoint (guint32 idx);

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

	virtual ErrorCode GetPendingSignal (guint32 *out_signo) = 0;

	virtual ErrorCode SetSignal (guint32 signo, gboolean send_it) = 0;

	gchar *DisassembleInstruction (guint64 address, guint32 *out_insn_size);

	virtual ErrorCode GetRegisters (InferiorRegs *regs) = 0;

	virtual ErrorCode SetRegisters (InferiorRegs *regs) = 0;

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

protected:
	MdbInferior (MdbServer *server, BreakpointManager *bpm)
	{
		this->iid = ++next_iid;
		this->bpm = bpm;
		this->server = server;

		last_signal = 0;

		disassembler = NULL;

		arch = mdb_arch_new (this);
	}

	int iid;
	static int next_iid;

#if defined(__linux__) || defined(__FreeBSD__)
	int last_signal;
#endif

	BreakpointManager *bpm;

	MdbServer *server;
	MdbArch *arch;

	MdbDisassembler *disassembler;

	friend MdbArch *mdb_arch_new (MdbInferior *inferior);
};

extern MdbInferior *mdb_inferior_new (MdbServer *server, BreakpointManager *bpm);

#endif