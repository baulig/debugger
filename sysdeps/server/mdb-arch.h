#ifndef __MDB_ARCH_H__
#define __MDB_ARCH_H__ 1

#include <mdb-server.h>
#include <breakpoints.h>
#include <mdb-inferior.h>

class MdbArch
{
public:
	virtual ServerEvent *ChildStopped (int stopsig) = 0;

	virtual ErrorCode GetFrame (StackFrame *out_frame) = 0;

	BreakpointInfo *LookupBreakpoint (guint32 idx, BreakpointManager **out_bpm);

	virtual ErrorCode EnableBreakpoint (BreakpointInfo *breakpoint) = 0;
	virtual ErrorCode DisableBreakpoint (BreakpointInfo *breakpoint) = 0;

	virtual void RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer) = 0;

	virtual int GetRegisterCount (void) = 0;
	virtual ErrorCode GetRegisterValues (guint64 *values) = 0;
	virtual ErrorCode SetRegisterValues (const guint64 *values) = 0;

protected:
	MdbArch (MdbInferior *inferior)
	{
		this->inferior = inferior;
	}

	MdbInferior *inferior;
	BreakpointManager *hw_bpm;

	gboolean CheckBreakpoint (guint64 address, guint64 *retval);
};

extern MdbArch *mdb_arch_new (MdbInferior *inferior);

#endif
