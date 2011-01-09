#ifndef __ARM_ARCH_H__
#define __ARM_ARCH_H__ 1

#include <mdb-arch.h>

#include <sys/ptrace.h>
#include <signal.h>

struct _InferiorRegs {
	struct pt_regs regs;
};

class ArmArch : public MdbArch
{
public:
	ArmArch (MdbInferior *inferior) : MdbArch (inferior)
	{ }

	ErrorCode EnableBreakpoint (BreakpointInfo *breakpoint);
	ErrorCode DisableBreakpoint (BreakpointInfo *breakpoint);

	ServerEvent *ChildStopped (int stopsig, bool *out_remain_stopped);
	ErrorCode GetFrame (StackFrame *out_frame);

	void RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer);

	int GetRegisterCount (void);
	ErrorCode GetRegisterValues (guint64 *values);
	ErrorCode SetRegisterValues (const guint64 *values);

	ErrorCode CallMethod (InvocationData *invocation);

	ErrorCode ExecuteInstruction (MdbInferior *inferior, gsize code_address, int insn_size,
				      bool update_ip, InferiorCallback *callback);

protected:
	ErrorCode
	GetRegisters (void)
	{
		return inferior->GetRegisters (&current_regs);
	}

	ErrorCode SetRegisters (void)
	{
		return inferior->SetRegisters (&current_regs);
	}

	InferiorRegs current_regs;
};

#endif
