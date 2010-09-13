#ifndef __MDB_ARCH_H__
#define __MDB_ARCH_H__ 1

#include <mdb-server.h>
#include <breakpoints.h>
#include <mdb-inferior.h>

class MdbArch
{
public:
	virtual ServerEvent *ChildStopped (int stopsig, bool *out_remain_stopped) = 0;

	virtual ErrorCode GetFrame (StackFrame *out_frame) = 0;

	BreakpointInfo *LookupBreakpoint (guint32 idx, BreakpointManager **out_bpm);

	virtual ErrorCode EnableBreakpoint (BreakpointInfo *breakpoint) = 0;
	virtual ErrorCode DisableBreakpoint (BreakpointInfo *breakpoint) = 0;

	virtual void RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer) = 0;

	virtual int GetRegisterCount (void) = 0;
	virtual ErrorCode GetRegisterValues (guint64 *values) = 0;
	virtual ErrorCode SetRegisterValues (const guint64 *values) = 0;

	virtual ErrorCode GetRegisters (void) = 0;
	virtual ErrorCode SetRegisters (void) = 0;

	virtual ErrorCode CallMethod (InvocationData *data) = 0;

	virtual ErrorCode ExecuteInstruction (MdbInferior *inferior, gsize code_address, int insn_size,
					      bool update_ip, InferiorCallback *callback) = 0;

protected:
	MdbArch (MdbInferior *inferior)
	{
		this->inferior = inferior;
		this->hw_bpm = NULL;
	}

	MdbInferior *inferior;
	BreakpointManager *hw_bpm;
};

extern MdbArch *mdb_arch_new (MdbInferior *inferior);

#endif
