#include <x86-arch.h>

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
};

MdbArch *
mdb_arch_new (MdbInferior *inferior)
{
	return new X86Arch (inferior);
}

ErrorCode
X86Arch::EnableBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;
	char bopcode = (char) 0xcc;
	gsize address;

	if (breakpoint->enabled)
		return ERR_NONE;

	address = breakpoint->address;

	if (breakpoint->dr_index >= 0) {
#if defined(__x86_64__)
		if (breakpoint->type == HARDWARE_BREAKPOINT_READ)
			X86_DR_SET_RW_LEN (current_regs, breakpoint->dr_index, DR_RW_READ | DR_LEN_8);
		else if (breakpoint->type == HARDWARE_BREAKPOINT_WRITE)
			X86_DR_SET_RW_LEN (current_regs, breakpoint->dr_index, DR_RW_WRITE | DR_LEN_8);
		else
			X86_DR_SET_RW_LEN (current_regs, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
#else
		if (breakpoint->type == HARDWARE_BREAKPOINT_READ)
			X86_DR_SET_RW_LEN (current_regs, breakpoint->dr_index, DR_RW_READ | DR_LEN_4);
		else if (breakpoint->type == HARDWARE_BREAKPOINT_WRITE)
			X86_DR_SET_RW_LEN (current_regs, breakpoint->dr_index, DR_RW_WRITE | DR_LEN_4);
		else
			X86_DR_SET_RW_LEN (current_regs, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
#endif
		X86_DR_LOCAL_ENABLE (current_regs, breakpoint->dr_index);
		INFERIOR_REG_DR_N (current_regs, breakpoint->dr_index) = address;

		result = inferior->SetRegisters (&current_regs);
		if (result) {
			g_warning (G_STRLOC);
			return result;
		}

		INFERIOR_DR_INDEX (current_regs, breakpoint->dr_index) = breakpoint->id;
	} else {
		result = inferior->ReadMemory (address, 1, &breakpoint->saved_insn);
		if (result)
			return result;

#if 0
		if (server->mono_runtime) {
			result = runtime_info_enable_breakpoint (server, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}
#endif

		return inferior->WriteMemory (address, 1, &bopcode);
	}

	return ERR_NONE;
}

ErrorCode
X86Arch::DisableBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;
	gsize address;

	if (!breakpoint->enabled)
		return ERR_NONE;

	address = breakpoint->address;

	if (breakpoint->dr_index >= 0) {
		X86_DR_DISABLE (current_regs, breakpoint->dr_index);
		INFERIOR_REG_DR_N (current_regs, breakpoint->dr_index) = 0L;

		result = inferior->SetRegisters (&current_regs);
		if (result) {
			g_warning (G_STRLOC);
			return result;
		}

		INFERIOR_DR_INDEX (current_regs, breakpoint->dr_index) = 0;
	} else {
		result = inferior->WriteMemory (address, 1, &breakpoint->saved_insn);
		if (result)
			return result;

#if 0
		if (server->mono_runtime) {
			result = runtime_info_disable_breakpoint (server, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}
#endif
	}

	return ERR_NONE;
}

void
X86Arch::RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer)
{
	GPtrArray *breakpoints;
	guint8 *ptr = (guint8 *) buffer;
	guint32 i;

	breakpoints = inferior->GetBreakpointManager ()->GetBreakpoints ();

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = (BreakpointInfo *) g_ptr_array_index (breakpoints, i);
		guint32 offset;

		if (info->is_hardware_bpt || !info->enabled)
			continue;
		if ((info->address < start) || (info->address >= start+size))
			continue;

		offset = (gsize) (info->address - start);
		ptr [offset] = info->saved_insn;
	}
}

#define __X86_ARCH_CPP__ 1

#if defined(__i386__)
#include "i386-arch.cpp"
#elif defined(__x86_64__)
#include "x86_64-arch.cpp"
#else
#error "Unknown Architecture."
#endif

ErrorCode
X86Arch::GetFrame (StackFrame *out_frame)
{
	ErrorCode result;

	result = GetRegisters ();
	if (result)
		return result;

	out_frame->address = (gsize) INFERIOR_REG_RIP (current_regs);
	out_frame->stack_pointer = (gsize) INFERIOR_REG_RSP (current_regs);
	out_frame->frame_address = (gsize) INFERIOR_REG_RBP (current_regs);

	return ERR_NONE;
}

ServerEvent *
X86Arch::ChildStopped (int stopsig)
{
	CodeBufferData *cbuffer = NULL;
	CallbackData *cdata;
	BreakpointInfo *breakpoint;
	bool is_callback;
	ServerEvent *e;
	gsize code;
	int i;

	if (GetRegisters ())
		return NULL;

	e = g_new0 (ServerEvent, 1);
	e->sender = inferior;
	e->type = SERVER_EVENT_STOPPED;

	/*
	 * By default, when the NX-flag is set in the BIOS, we stop at the `cdata->call_address'
	 * (which contains an `int 3' instruction) with a SIGSEGV.
	 *
	 * When the NX-flag is turned off, that `int 3' instruction is actually executed and we
	 * stop normally.
	 */

	cdata = GetCallbackData ();
	if (!cdata)
		is_callback = false;
	else {
#if (defined(__linux__) || defined(__FreeBSD__)) && defined(__x86_64__)
		is_callback = ((stopsig == SIGSEGV) && (cdata->call_address == INFERIOR_REG_RIP (current_regs))) ||
			(cdata->call_address == INFERIOR_REG_RIP (current_regs) - 1);
#else
		is_callback = (cdata->call_address == INFERIOR_REG_RIP (current_regs) - 1);
#endif
	}

	if (is_callback) {
		gsize exc_object;

		if (cdata->pushed_registers) {
			guint32 pushed_regs [9];

			if (inferior->ReadMemory (cdata->pushed_registers, 36, &pushed_regs)) {
				g_warning (G_STRLOC ": Can't restore registers after returning from a call");

				e->type = SERVER_EVENT_INTERNAL_ERROR;
				return e;
			}

#if defined(__i386__)
			INFERIOR_REG_RAX (cdata->saved_regs) = pushed_regs [0];
			INFERIOR_REG_RBX (cdata->saved_regs) = pushed_regs [1];
			INFERIOR_REG_RCX (cdata->saved_regs) = pushed_regs [2];
			INFERIOR_REG_RDX (cdata->saved_regs) = pushed_regs [3];
			INFERIOR_REG_RBP (cdata->saved_regs) = pushed_regs [4];
			INFERIOR_REG_RSP (cdata->saved_regs) = pushed_regs [5];
			INFERIOR_REG_RSI (cdata->saved_regs) = pushed_regs [6];
			INFERIOR_REG_RDI (cdata->saved_regs) = pushed_regs [7];
			INFERIOR_REG_RIP (cdata->saved_regs) = pushed_regs [8];
#else
			INFERIOR_REG_RAX (cdata->saved_regs) = pushed_regs [0];
			INFERIOR_REG_RBX (cdata->saved_regs) = pushed_regs [1];
			INFERIOR_REG_RCX (cdata->saved_regs) = pushed_regs [2];
			INFERIOR_REG_RDX (cdata->saved_regs) = pushed_regs [3];
			INFERIOR_REG_RBP (cdata->saved_regs) = pushed_regs [4];
			INFERIOR_REG_RSP (cdata->saved_regs) = pushed_regs [5];
			INFERIOR_REG_RSI (cdata->saved_regs) = pushed_regs [6];
			INFERIOR_REG_RDI (cdata->saved_regs) = pushed_regs [7];
			INFERIOR_REG_RIP (cdata->saved_regs) = pushed_regs [8];
			INFERIOR_REG_R12 (cdata->saved_regs) = pushed_regs [9];
			INFERIOR_REG_R13 (cdata->saved_regs) = pushed_regs [10];
			INFERIOR_REG_R14 (cdata->saved_regs) = pushed_regs [11];
			INFERIOR_REG_R15 (cdata->saved_regs) = pushed_regs [12];
#endif
		}

		if (inferior->SetRegisters (&cdata->saved_regs)) {
			g_warning (G_STRLOC ": Can't restore registers after returning from a call");
			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		e->arg = cdata->callback_argument;
		e->data1 = (gsize) INFERIOR_REG_RAX (current_regs);

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

#if defined(__linux__) || defined(__FreeBSD__)
		inferior->SetLastSignal (cdata->saved_signal);
#endif

		g_ptr_array_remove (callback_stack, cdata);

		GetRegisters ();

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

#if defined(__linux__) || defined(__FreeBSD__)
	if (stopsig == SIGSTOP) {
		e->type = SERVER_EVENT_INTERRUPTED;
		return e;
	}

	if (stopsig != SIGTRAP) {
		e->arg = stopsig;
		return e;
	}
#endif

#if 0
	if (server->mono_runtime &&
	    ((gsize) INFERIOR_REG_RIP (current_regs) - 1 == server->mono_runtime->notification_address)) {
		guint32 addr = (gsize) INFERIOR_REG_RSP (current_regs) + 4;
		guint64 data [3];

		if (mdb_inferior_read_memory (server, addr, 24, &data))
			return e;

		e->arg = data [0];
		e->data1 = data [1];
		e->data2 = data [2];
		e->type = SERVER_EVENT_NOTIFICATION;
		return e;
	}
#endif

	for (i = 0; i < DR_NADDR; i++) {
		if (X86_DR_WATCH_HIT (current_regs, i)) {
			INFERIOR_REG_DR_STATUS (current_regs) = 0;
			SetRegisters ();
			e->arg = INFERIOR_DR_INDEX (current_regs, i);
			e->type = SERVER_EVENT_BREAKPOINT;
			return e;
		}
	}

	breakpoint = inferior->GetBreakpointManager()->Lookup (INFERIOR_REG_RIP (current_regs) - 1);
	if (breakpoint && breakpoint->enabled) {
		if (breakpoint->handler && breakpoint->handler (inferior, breakpoint)) {
			if (!inferior->Resume ()) {
				g_free (e);
				return NULL;
			}
		}

		INFERIOR_REG_RIP (current_regs)--;
		SetRegisters ();
		e->type = SERVER_EVENT_BREAKPOINT;
		e->arg = breakpoint->id;
		return e;
	}

#if 0
	cbuffer = code_buffer;
	if (cbuffer) {
		server->mono_runtime->executable_code_bitfield [cbuffer->slot] = 0;

		if (cbuffer->code_address + cbuffer->insn_size != INFERIOR_REG_RIP (current_regs)) {
			g_warning (G_STRLOC ": %x - %x,%d - %x - %x", cbuffer->original_rip,
				   cbuffer->code_address, cbuffer->insn_size,
				   cbuffer->code_address + cbuffer->insn_size,
				   INFERIOR_REG_RIP (current_regs));
			return e;
		}

		INFERIOR_REG_RIP (current_regs) = cbuffer->original_rip;
		if (cbuffer->update_ip)
			INFERIOR_REG_RIP (current_regs) += cbuffer->insn_size;
		if (mdb_inferior_set_registers (server, &current_regs) != COMMAND_ERROR_NONE) {
			g_error (G_STRLOC ": Can't restore registers");
		}

		g_free (cbuffer);
		code_buffer = NULL;
		return e;
	}
#endif

	if (inferior->PeekWord (GPOINTER_TO_INT (INFERIOR_REG_RIP (current_regs) - 1), &code))
		return e;

	if ((code & 0xff) == 0xcc) {
		e->arg = 0;
		e->type = SERVER_EVENT_BREAKPOINT;
		return e;
	}

	return e;
}

int
X86Arch::GetRegisterCount (void)
{
	return DEBUGGER_REG_LAST;
}

