#include <arm-arch.h>

#define INFERIOR_REG_R0(r)		r.regs.ARM_r0
#define INFERIOR_REG_R1(r)		r.regs.ARM_r1
#define INFERIOR_REG_R2(r)		r.regs.ARM_r2
#define INFERIOR_REG_R3(r)		r.regs.ARM_r3
#define INFERIOR_REG_R4(r)		r.regs.ARM_r4
#define INFERIOR_REG_R5(r)		r.regs.ARM_r5
#define INFERIOR_REG_R6(r)		r.regs.ARM_r6
#define INFERIOR_REG_R7(r)		r.regs.ARM_r7
#define INFERIOR_REG_R8(r)		r.regs.ARM_r8
#define INFERIOR_REG_R9(r)		r.regs.ARM_r9
#define INFERIOR_REG_R10(r)		r.regs.ARM_r10
#define INFERIOR_REG_FP(r)		r.regs.ARM_fp
#define INFERIOR_REG_IP(r)		r.regs.ARM_ip
#define INFERIOR_REG_SP(r)		r.regs.ARM_sp
#define INFERIOR_REG_LR(r)		r.regs.ARM_lr
#define INFERIOR_REG_PC(r)		r.regs.ARM_pc
#define INFERIOR_REG_CPSR(r)		r.regs.ARM_cpsr
#define INFERIOR_REG_ORIG_R0(r)		r.regs.ARM_ORIG_r0

MdbArch *
mdb_arch_new (MdbInferior *inferior)
{
	return new ArmArch (inferior);
}

ErrorCode
ArmArch::EnableBreakpoint (BreakpointInfo *breakpoint)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
ArmArch::DisableBreakpoint (BreakpointInfo *breakpoint)
{
	return ERR_NOT_IMPLEMENTED;
}

ServerEvent *
ArmArch::ChildStopped (int stopsig, bool *out_remain_stopped)
{
	return NULL;
}

ErrorCode
ArmArch::GetFrame (StackFrame *out_frame)
{
	ErrorCode result;

	result = GetRegisters ();
	if (result)
		return result;

	out_frame->address = (gsize) INFERIOR_REG_PC (current_regs);
	out_frame->stack_pointer = (gsize) INFERIOR_REG_SP (current_regs);
	out_frame->frame_address = (gsize) INFERIOR_REG_FP (current_regs);

	return ERR_NONE;
}

void
ArmArch::RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer)
{ }

int
ArmArch::GetRegisterCount (void)
{
	return DEBUGGER_REG_LAST;
}

ErrorCode
ArmArch::GetRegisterValues (guint64 *values)
{
	ErrorCode result;

	result = GetRegisters ();
	if (result)
		return result;

	values [DEBUGGER_REG_R0] = INFERIOR_REG_R0 (current_regs);
	values [DEBUGGER_REG_R1] = INFERIOR_REG_R1 (current_regs);
	values [DEBUGGER_REG_R2] = INFERIOR_REG_R2 (current_regs);
	values [DEBUGGER_REG_R3] = INFERIOR_REG_R3 (current_regs);
	values [DEBUGGER_REG_R4] = INFERIOR_REG_R4 (current_regs);
	values [DEBUGGER_REG_R5] = INFERIOR_REG_R5 (current_regs);
	values [DEBUGGER_REG_R6] = INFERIOR_REG_R6 (current_regs);
	values [DEBUGGER_REG_R7] = INFERIOR_REG_R7 (current_regs);
	values [DEBUGGER_REG_R8] = INFERIOR_REG_R8 (current_regs);
	values [DEBUGGER_REG_R9] = INFERIOR_REG_R9 (current_regs);
	values [DEBUGGER_REG_R10] = INFERIOR_REG_R10 (current_regs);
	values [DEBUGGER_REG_FP] = INFERIOR_REG_FP (current_regs);
	values [DEBUGGER_REG_IP] = INFERIOR_REG_IP (current_regs);
	values [DEBUGGER_REG_SP] = INFERIOR_REG_SP (current_regs);
	values [DEBUGGER_REG_LR] = INFERIOR_REG_LR (current_regs);
	values [DEBUGGER_REG_PC] = INFERIOR_REG_PC (current_regs);
	values [DEBUGGER_REG_CPSR] = INFERIOR_REG_CPSR (current_regs);
	values [DEBUGGER_REG_ORIG_R0] = INFERIOR_REG_ORIG_R0 (current_regs);

	return ERR_NONE;
}

ErrorCode
ArmArch::SetRegisterValues (const guint64 *values)
{
	INFERIOR_REG_R0 (current_regs) = values [DEBUGGER_REG_R0];
	INFERIOR_REG_R1 (current_regs) = values [DEBUGGER_REG_R1];
	INFERIOR_REG_R2 (current_regs) = values [DEBUGGER_REG_R2];
	INFERIOR_REG_R3 (current_regs) = values [DEBUGGER_REG_R3];
	INFERIOR_REG_R4 (current_regs) = values [DEBUGGER_REG_R4];
	INFERIOR_REG_R5 (current_regs) = values [DEBUGGER_REG_R5];
	INFERIOR_REG_R6 (current_regs) = values [DEBUGGER_REG_R6];
	INFERIOR_REG_R7 (current_regs) = values [DEBUGGER_REG_R7];
	INFERIOR_REG_R8 (current_regs) = values [DEBUGGER_REG_R8];
	INFERIOR_REG_R9 (current_regs) = values [DEBUGGER_REG_R9];
	INFERIOR_REG_R10 (current_regs) = values [DEBUGGER_REG_R10];
	INFERIOR_REG_FP (current_regs) = values [DEBUGGER_REG_FP];
	INFERIOR_REG_IP (current_regs) = values [DEBUGGER_REG_IP];
	INFERIOR_REG_SP (current_regs) = values [DEBUGGER_REG_SP];
	INFERIOR_REG_LR (current_regs) = values [DEBUGGER_REG_LR];
	INFERIOR_REG_PC (current_regs) = values [DEBUGGER_REG_PC];
	INFERIOR_REG_CPSR (current_regs) = values [DEBUGGER_REG_CPSR];
	INFERIOR_REG_ORIG_R0 (current_regs) = values [DEBUGGER_REG_ORIG_R0];

	return SetRegisters ();
}

ErrorCode
ArmArch::CallMethod (InvocationData *invocation)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
ArmArch::ExecuteInstruction (MdbInferior *inferior, gsize code_address, int insn_size,
			     bool update_ip, InferiorCallback *callback)
{
	return ERR_NOT_IMPLEMENTED;
}


