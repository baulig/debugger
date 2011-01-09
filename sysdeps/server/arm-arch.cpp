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

struct _CallbackData
{
	InferiorRegs saved_regs;
	guint64 callback_argument;
	gsize call_address;
	gsize stack_pointer;
	gsize rti_frame;
	gsize exc_address;
	gsize pushed_registers;
	gsize data_pointer;
	guint32 data_size;
	int saved_signal;
	gboolean debug;
	gboolean is_rti;

	InferiorCallback *callback;
};

static const char arm_le_breakpoint[] = { 0xFE, 0xDE, 0xFF, 0xE7 };

MdbArch *
mdb_arch_new (MdbInferior *inferior)
{
	return new ArmArch (inferior);
}

ErrorCode
ArmArch::EnableBreakpoint (BreakpointInfo *breakpoint)
{
	MonoRuntime *runtime;
	ErrorCode result;

	if (IsThumbMode ())
		return ERR_NOT_IMPLEMENTED;

	result = inferior->ReadMemory (breakpoint->address, 4, &breakpoint->saved_insn);
	if (result)
		return result;

	runtime = inferior->GetProcess ()->GetMonoRuntime ();
	if (runtime) {
		result = runtime->EnableBreakpoint (inferior, breakpoint);
		if (result)
			return result;
	}

	return inferior->WriteMemory (breakpoint->address, 4, &arm_le_breakpoint);
}

ErrorCode
ArmArch::DisableBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;
	MonoRuntime *runtime;

	if (IsThumbMode ())
		return ERR_NOT_IMPLEMENTED;

	result = inferior->WriteMemory (breakpoint->address, 4, &breakpoint->saved_insn);
	if (result)
		return result;

	runtime = inferior->GetProcess ()->GetMonoRuntime ();
	if (runtime) {
		result = runtime->DisableBreakpoint (inferior, breakpoint);
		if (result)
			return result;
	}

	return ERR_NONE;
}

ServerEvent *
ArmArch::ChildStopped (int stopsig, bool *out_remain_stopped)
{
	MonoRuntime *mono_runtime;
	BreakpointManager *bpm;
	BreakpointInfo *breakpoint;
	gsize exc_object = 0;
	CallbackData *cdata;
	ServerEvent *e;
	guint32 insn;

	*out_remain_stopped = true;

	if (GetRegisters ())
		return NULL;

	g_message (G_STRLOC ": %p - %d", INFERIOR_REG_PC (current_regs), stopsig);

	e = g_new0 (ServerEvent, 1);
	e->sender = inferior;
	e->type = SERVER_EVENT_STOPPED;

	if (stopsig == SIGSTOP) {
		e->type = SERVER_EVENT_INTERRUPTED;
		return e;
	}

	if (stopsig == SIGTRAP)
		return e;

	if (stopsig != SIGILL) {
		e->arg = stopsig;
		return e;
	}

	mono_runtime = inferior->GetProcess ()->GetMonoRuntime ();
	bpm = inferior->GetServer ()->GetBreakpointManager();

	if (inferior->PeekWord (INFERIOR_REG_PC (current_regs), &insn)) {
		g_warning (G_STRLOC ": Can't read instruction at %p", INFERIOR_REG_PC (current_regs));
		e->type = SERVER_EVENT_INTERNAL_ERROR;
		return e;
	}

	if (memcmp (&arm_le_breakpoint, &insn, 4))
		return e;

	if (mono_runtime &&
	    (INFERIOR_REG_PC (current_regs) == mono_runtime->GetNotificationAddress ())) {
		NotificationType type;
		gsize arg1, arg2, lr;

		type = (NotificationType) INFERIOR_REG_R0 (current_regs);
		arg1 = INFERIOR_REG_R1 (current_regs);
		arg2 = INFERIOR_REG_R2 (current_regs);

		lr = INFERIOR_REG_LR (current_regs);

		INFERIOR_REG_PC (current_regs) = INFERIOR_REG_PC (current_regs) + 4;
		SetRegisters ();

		mono_runtime->HandleNotification (inferior, type, arg1, arg2);

		e->type = SERVER_EVENT_NOTIFICATION;

		e->arg = type;
		e->data1 = arg1;
		e->data2 = arg2;
		return e;
	}

	cdata = GetCallbackData ();
	if (cdata && (cdata->call_address == INFERIOR_REG_PC (current_regs))) {
		if (inferior->SetRegisters (&cdata->saved_regs)) {
			g_warning (G_STRLOC ": Can't restore registers after returning from a call");
			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		e->arg = cdata->callback_argument;
		e->data1 = INFERIOR_REG_R0 (current_regs);

		if (cdata->data_pointer) {
			e->opt_data_size = cdata->data_size;
			e->opt_data = g_malloc0 (cdata->data_size);

			if (inferior->ReadMemory (cdata->data_pointer, cdata->data_size, e->opt_data)) {
				e->type = SERVER_EVENT_INTERNAL_ERROR;
				return e;
			}
		}

		if (cdata->exc_address && inferior->PeekWord (cdata->exc_address, &exc_object)) {
			g_warning (G_STRLOC ": Can't get exc object");
			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		e->data2 = (guint64) exc_object;

		inferior->SetLastSignal (cdata->saved_signal);

		g_ptr_array_remove (callback_stack, cdata);

		GetRegisters ();

		if (cdata->callback) {
			cdata->callback->Invoke (inferior, INFERIOR_REG_R0 (current_regs), exc_object);
			delete cdata->callback;
			g_free (e);
			return NULL;
		}

		if (cdata->is_rti) {
			g_free (cdata);
			e->type = SERVER_EVENT_RUNTIME_INVOKE_DONE;
		}

		if (cdata->debug) {
			e->data1 = 0;
			g_free (cdata);
			e->type = SERVER_EVENT_CALLBACK_COMPLETED;
			return e;
		}

		g_free (cdata);
		e->type = SERVER_EVENT_CALLBACK;
		return e;
	}

	breakpoint = bpm->Lookup (INFERIOR_REG_PC (current_regs));

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

	e->arg = stopsig;
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
	CallbackData *cdata;
	ErrorCode result;

	g_message (G_STRLOC ": CallMethod(): %d", invocation->type);

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &current_regs, sizeof (InferiorRegs));
	cdata->callback_argument = invocation->callback_id;

	cdata->saved_signal = inferior->GetLastSignal ();
	inferior->SetLastSignal (0);

	if (!Marshal_Generic (invocation, cdata)) {
		g_free (cdata);
		return ERR_UNKNOWN_ERROR;
	}

	result = SetRegisters ();
	if (result) {
		g_free (cdata);
		return result;
	}

	g_ptr_array_add (callback_stack, cdata);

	return inferior->Continue ();
}

ErrorCode
ArmArch::ExecuteInstruction (MdbInferior *inferior, gsize code_address, int insn_size,
			     bool update_ip, InferiorCallback *callback)
{
	return ERR_NOT_IMPLEMENTED;
}


bool
ArmArch::Marshal_Generic (InvocationData *invocation, CallbackData *cdata)
{
	MonoRuntime *runtime = inferior->GetProcess ()->GetMonoRuntime ();
	GenericInvocationData data;
	gsize invocation_func, new_sp;
	gconstpointer data_ptr = NULL;
	int data_size = 0;

	memset (&data, 0, sizeof (data));
	data.invocation_type = invocation->type;
	data.callback_id = (guint32) invocation->callback_id;
	data.method_address = invocation->method_address;
	data.arg1 = invocation->arg1;
	data.arg2 = invocation->arg2;
	data.arg3 = invocation->arg3;

	if (invocation->type == INVOCATION_TYPE_LONG_LONG_LONG_STRING) {
		data_ptr = invocation->string_arg;
		data_size = strlen (invocation->string_arg) + 1;
	} else {
		data_ptr = invocation->data;
		data_size = invocation->data_size;
	}

	invocation_func = runtime->GetGenericInvocationFunc ();

	new_sp = INFERIOR_REG_SP (current_regs) - sizeof (data) - data_size - 4;

	g_message (G_STRLOC ": %p - %p - %p", invocation_func, (gsize) invocation->method_address, new_sp);

	if (data_ptr) {
		data.data_size = data_size;
		data.data_arg_ptr = new_sp + sizeof (data) + 4;

		if (inferior->WriteMemory (data.data_arg_ptr, data_size, data_ptr))
			return false;
	}

	cdata->call_address = new_sp;
	cdata->stack_pointer = new_sp;

	cdata->callback = invocation->callback;

	if (inferior->PokeWord (new_sp, 0xE7FFDEFE))
		return false;
	if (inferior->WriteMemory (new_sp + 4, sizeof (data), &data))
		return false;

	if (invocation->type == INVOCATION_TYPE_RUNTIME_INVOKE) {
		cdata->exc_address = new_sp + 24;
	}

	INFERIOR_REG_R0 (current_regs) = new_sp + 4;
	INFERIOR_REG_PC (current_regs) = runtime->GetGenericInvocationFunc ();
	INFERIOR_REG_LR (current_regs) = new_sp;
	INFERIOR_REG_SP (current_regs) = new_sp;

	return true;
}
