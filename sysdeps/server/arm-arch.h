#ifndef __ARM_ARCH_H__
#define __ARM_ARCH_H__ 1

#include <mdb-arch.h>

#include <sys/ptrace.h>
#include <signal.h>

typedef enum {
	DEBUGGER_REG_R0	= 0,
	DEBUGGER_REG_R1,
	DEBUGGER_REG_R2,
	DEBUGGER_REG_R3,
	DEBUGGER_REG_R4,
	DEBUGGER_REG_R5,
	DEBUGGER_REG_R6,
	DEBUGGER_REG_R7,
	DEBUGGER_REG_R8,
	DEBUGGER_REG_R9,
	DEBUGGER_REG_R10,

	DEBUGGER_REG_FP,
	DEBUGGER_REG_IP,
	DEBUGGER_REG_SP,
	DEBUGGER_REG_LR,
	DEBUGGER_REG_PC,

	DEBUGGER_REG_CPSR,
	DEBUGGER_REG_ORIG_R0,

	DEBUGGER_REG_LAST
} ARMRegisters;

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

	bool IsThumbMode (void)
	{
		return current_regs.regs.ARM_cpsr & 0x20;
	}

	InferiorRegs current_regs;
};

#endif
