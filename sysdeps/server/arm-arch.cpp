#include <arm-arch.h>
#include <mono-runtime.h>

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
	static const char arm_le_breakpoint[] = { 0xFE, 0xDE, 0xFF, 0xE7 };
	ErrorCode result;

	if (IsThumbMode ())
		return ERR_NOT_IMPLEMENTED;

	g_message (G_STRLOC ": %p - %d", breakpoint->address, IsThumbMode ());

	result = inferior->ReadMemory (breakpoint->address, 4, &breakpoint->saved_insn);
	if (result)
		return result;

	return inferior->WriteMemory (breakpoint->address, 4, &arm_le_breakpoint);
}

ErrorCode
ArmArch::DisableBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;

	if (IsThumbMode ())
		return ERR_NOT_IMPLEMENTED;

	result = inferior->WriteMemory (breakpoint->address, 4, &breakpoint->saved_insn);
	if (result)
		return result;

	return ERR_NONE;
}

ServerEvent *
ArmArch::ChildStopped (int stopsig, bool *out_remain_stopped)
{
	MonoRuntime *mono_runtime;
	BreakpointManager *bpm;
	ServerEvent *e;

	*out_remain_stopped = true;

	if (GetRegisters ())
		return NULL;

	g_message (G_STRLOC ": %p - %d", INFERIOR_REG_PC (current_regs), stopsig);

	e = g_new0 (ServerEvent, 1);
	e->sender = inferior;
	e->type = SERVER_EVENT_STOPPED;

	mono_runtime = inferior->GetProcess ()->GetMonoRuntime ();
	bpm = inferior->GetServer ()->GetBreakpointManager();

	if (stopsig == SIGILL) {
		BreakpointInfo *breakpoint;

		if (mono_runtime &&
		    (INFERIOR_REG_PC (current_regs) == mono_runtime->GetNotificationAddress ())) {
			NotificationType type;
			gsize arg1, arg2, lr;

			g_message (G_STRLOC);

			type = (NotificationType) INFERIOR_REG_R0 (current_regs);
			arg1 = INFERIOR_REG_R1 (current_regs);
			arg2 = INFERIOR_REG_R2 (current_regs);

			lr = INFERIOR_REG_LR (current_regs);

			g_message (G_STRLOC ": %p - %p - %p - %p", lr, type, arg1, arg2);

			INFERIOR_REG_PC (current_regs) = INFERIOR_REG_PC (current_regs) + 4;
			SetRegisters ();

			mono_runtime->HandleNotification (inferior, type, arg1, arg2);

			e->type = SERVER_EVENT_NOTIFICATION;

			e->arg = type;
			e->data1 = arg1;
			e->data2 = arg2;
			return e;
		}

		breakpoint = bpm->Lookup (INFERIOR_REG_PC (current_regs));

		g_message (G_STRLOC ": %p - %p", INFERIOR_REG_PC (current_regs), breakpoint);

		if (breakpoint && breakpoint->enabled) {
			if (breakpoint->handler && breakpoint->handler (inferior, breakpoint)) {
				inferior->DisableBreakpoint (breakpoint);
				*out_remain_stopped = false;
				g_free (e);
				return NULL;
			}

			e->type = SERVER_EVENT_BREAKPOINT;
			e->arg = breakpoint->id;
			return e;
		}
	}

	if (stopsig == SIGSTOP) {
		e->type = SERVER_EVENT_INTERRUPTED;
		return e;
	}

	if (stopsig != SIGTRAP) {
		e->arg = stopsig;
		return e;
	}

	return e;
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
{
	GPtrArray *breakpoints;
	guint8 *ptr = (guint8 *) buffer;
	guint32 i;

	breakpoints = inferior->GetServer ()->GetBreakpointManager ()->GetBreakpoints ();

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = (BreakpointInfo *) g_ptr_array_index (breakpoints, i);
		guint32 offset;

		if (info->is_hardware_bpt || !info->enabled)
			continue;
		if ((info->address < start) || (info->address >= start+size+4))
			continue;

		offset = (gsize) (info->address - start);
		ptr [offset] = info->saved_insn[0];
		ptr [offset+1] = info->saved_insn[1];
		ptr [offset+2] = info->saved_insn[2];
		ptr [offset+3] = info->saved_insn[3];
	}
}

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


