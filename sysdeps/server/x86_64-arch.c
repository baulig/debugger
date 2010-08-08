#if !defined(X86_ARCH_C) || !defined(__x86_64__)
#error "This file must not be used directly."
#endif

#include "x86_64-arch.h"

#if defined(__linux__) || defined(__FreeBSD__)
#include <signal.h>
#endif

struct _ArchInfo
{
	INFERIOR_REGS_TYPE current_regs;
	GPtrArray *callback_stack;
	CodeBufferData *code_buffer;
	guint64 dr_control, dr_status;
	guint64 pushed_regs_rsp;
	BreakpointManager *hw_bpm;
	int dr_index [DR_NADDR];
};

typedef struct
{
	INFERIOR_REGS_TYPE saved_regs;
	guint64 callback_argument;
	guint64 call_address;
	guint64 stack_pointer;
	guint64 rti_frame;
	guint64 exc_address;
	int saved_signal;
	guint64 pushed_registers;
	guint64 data_pointer;
	guint32 data_size;
	gboolean debug;
	gboolean is_rti;
} CallbackData;

ArchType
mdb_server_get_arch_type (void)
{
	return ARCH_TYPE_X86_64;
}

ArchInfo *
mdb_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);

	arch->callback_stack = g_ptr_array_new ();
	arch->hw_bpm = mono_debugger_breakpoint_manager_new ();

	return arch;
}

void
mdb_arch_finalize (ArchInfo *arch)
{
	g_ptr_array_free (arch->callback_stack, TRUE);
	mono_debugger_breakpoint_manager_free (arch->hw_bpm);
	g_free (arch);
}

static CallbackData *
get_callback_data (ArchInfo *arch)
{
	if (!arch->callback_stack->len)
		return NULL;

	return g_ptr_array_index (arch->callback_stack, arch->callback_stack->len - 1);
}

ServerEvent *
mdb_arch_child_stopped (ServerHandle *server, int stopsig)
{
	ArchInfo *arch = server->arch;
	InferiorHandle *inferior = server->inferior;
	CodeBufferData *cbuffer = NULL;
	CallbackData *cdata;
	ServerEvent *e;
	guint64 code;
	int i;

	mdb_arch_get_registers (server);

	e = g_new0 (ServerEvent, 1);
	e->sender_iid = server->iid;
	e->type = SERVER_EVENT_CHILD_STOPPED;

	/*
	 * By default, when the NX-flag is set in the BIOS, we stop at the `cdata->call_address'
	 * (which contains an `int 3' instruction) with a SIGSEGV.
	 *
	 * When the NX-flag is turned off, that `int 3' instruction is actually executed and we
	 * stop normally.
	 */

	cdata = get_callback_data (arch);
	if (cdata &&
	    (((stopsig == SIGSEGV) && (cdata->call_address == INFERIOR_REG_RIP (arch->current_regs))) ||
	     (cdata->call_address == INFERIOR_REG_RIP (arch->current_regs) - 1))) {
		if (cdata->pushed_registers) {
			guint64 pushed_regs [13];

			if (mdb_inferior_read_memory (inferior, cdata->pushed_registers, 104, &pushed_regs))
				g_error (G_STRLOC ": Can't restore registers after returning from a call");

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
		}

		if (mdb_inferior_set_registers (inferior, &cdata->saved_regs) != COMMAND_ERROR_NONE)
			g_error (G_STRLOC ": Can't restore registers after returning from a call");

		e->arg = cdata->callback_argument;
		e->data1 = INFERIOR_REG_RAX (arch->current_regs);

		if (cdata->data_pointer) {
			e->opt_data_size = cdata->data_size;
			e->opt_data = g_malloc0 (cdata->data_size);

			if (mdb_inferior_read_memory (inferior, cdata->data_pointer, cdata->data_size, e->opt_data))
				g_error (G_STRLOC ": Can't read data buffer after returning from a call");
		}

		if (cdata->exc_address &&
		    (mdb_inferior_peek_word (inferior, cdata->exc_address, &e->data2) != COMMAND_ERROR_NONE))
			g_error (G_STRLOC ": Can't get exc object");

		_mdb_inferior_set_last_signal (inferior, cdata->saved_signal);
		g_ptr_array_remove (arch->callback_stack, cdata);

		mdb_arch_get_registers (server);

		if (cdata->is_rti) {
			g_free (cdata);
			e->type = SERVER_EVENT_RUNTIME_INVOKE_DONE;
			return e;
		}

		if (cdata->debug) {
			e->data1 = 0;
			g_free (cdata);
			e->type = SERVER_EVENT_CHILD_CALLBACK_COMPLETED;
			return e;
		}

		g_free (cdata);
		e->type = SERVER_EVENT_CHILD_CALLBACK;
		return e;
	}

	if (stopsig != SIGTRAP) {
		_mdb_inferior_set_last_signal (server->inferior, stopsig);
		e->arg = stopsig;
		return e;
	}

	if (server->mono_runtime &&
	    (INFERIOR_REG_RIP (arch->current_regs) - 1 == server->mono_runtime->notification_address)) {
		e->arg = INFERIOR_REG_RDI (arch->current_regs);
		e->data1 = INFERIOR_REG_RSI (arch->current_regs);
		e->data2 = INFERIOR_REG_RDX (arch->current_regs);
		e->type = SERVER_EVENT_CHILD_NOTIFICATION;
		return e;
	}

	for (i = 0; i < DR_NADDR; i++) {
		if (X86_DR_WATCH_HIT (arch->current_regs, i)) {
			INFERIOR_REG_DR_STATUS (arch->current_regs) = 0;
			mdb_inferior_set_registers (inferior, &arch->current_regs);
			e->arg = arch->dr_index [i];
			e->type = SERVER_EVENT_CHILD_HIT_BREAKPOINT;
			return e;
		}
	}

	if (mdb_arch_check_breakpoint (server, INFERIOR_REG_RIP (arch->current_regs) - 1, &e->arg)) {
		INFERIOR_REG_RIP (arch->current_regs)--;
		mdb_inferior_set_registers (inferior, &arch->current_regs);
		e->arg = 0;
		e->type = SERVER_EVENT_CHILD_HIT_BREAKPOINT;
		return e;
	}

	cbuffer = arch->code_buffer;
	if (cbuffer) {
		server->mono_runtime->executable_code_bitfield [cbuffer->slot] = 0;

		if (cbuffer->code_address + cbuffer->insn_size != INFERIOR_REG_RIP (arch->current_regs)) {
			char buffer [1024];

			g_warning (G_STRLOC ": %d - %lx,%d - %lx - %lx", cbuffer->slot,
				   (long) cbuffer->code_address, cbuffer->insn_size,
				   (long) cbuffer->code_address + cbuffer->insn_size,
				   INFERIOR_REG_RIP (arch->current_regs));

			mdb_inferior_read_memory (inferior, cbuffer->code_address, 8, buffer);
			g_warning (G_STRLOC ": %02hhx %02hhx %02hhx %02hhx %02hhx %02hhx %02hhx %02hhx",
				   buffer [0], buffer [1], buffer [2], buffer [3], buffer [4],
				   buffer [5], buffer [6], buffer [7]);

			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		INFERIOR_REG_RIP (arch->current_regs) = cbuffer->original_rip;
		if (cbuffer->update_ip)
			INFERIOR_REG_RIP (arch->current_regs) += cbuffer->insn_size;
		if (mdb_inferior_set_registers (inferior, &arch->current_regs) != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC ": Can't restore registers");
			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		g_free (cbuffer);
		arch->code_buffer = NULL;
		e->type = SERVER_EVENT_CHILD_STOPPED;
		return e;
	}

	if (mdb_inferior_peek_word (inferior, GPOINTER_TO_UINT(INFERIOR_REG_RIP (arch->current_regs) - 1), &code) != COMMAND_ERROR_NONE)
		return e;

	if ((code & 0xff) == 0xcc) {
		e->arg = 0;
		e->type = SERVER_EVENT_CHILD_HIT_BREAKPOINT;
		return e;
	}

	return e;
}

ServerCommandError
mdb_server_get_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	values [DEBUGGER_REG_R15] = (guint64) INFERIOR_REG_R15 (arch->current_regs);
	values [DEBUGGER_REG_R14] = (guint64) INFERIOR_REG_R14 (arch->current_regs);
	values [DEBUGGER_REG_R13] = (guint64) INFERIOR_REG_R13 (arch->current_regs);
	values [DEBUGGER_REG_R12] = (guint64) INFERIOR_REG_R12 (arch->current_regs);
	values [DEBUGGER_REG_RBP] = (guint64) INFERIOR_REG_RBP (arch->current_regs);
	values [DEBUGGER_REG_RBX] = (guint64) INFERIOR_REG_RBX (arch->current_regs);
	values [DEBUGGER_REG_R11] = (guint64) INFERIOR_REG_R11 (arch->current_regs);
	values [DEBUGGER_REG_R10] = (guint64) INFERIOR_REG_R10 (arch->current_regs);
	values [DEBUGGER_REG_R9] = (guint64) INFERIOR_REG_R9 (arch->current_regs);
	values [DEBUGGER_REG_R8] = (guint64) INFERIOR_REG_R8 (arch->current_regs);
	values [DEBUGGER_REG_RAX] = (guint64) INFERIOR_REG_RAX (arch->current_regs);
	values [DEBUGGER_REG_RCX] = (guint64) INFERIOR_REG_RCX (arch->current_regs);
	values [DEBUGGER_REG_RDX] = (guint64) INFERIOR_REG_RDX (arch->current_regs);
	values [DEBUGGER_REG_RSI] = (guint64) INFERIOR_REG_RSI (arch->current_regs);
	values [DEBUGGER_REG_RDI] = (guint64) INFERIOR_REG_RDI (arch->current_regs);
	values [DEBUGGER_REG_ORIG_RAX] = (guint64) INFERIOR_REG_ORIG_RAX (arch->current_regs);
	values [DEBUGGER_REG_RIP] = (guint64) INFERIOR_REG_RIP (arch->current_regs);
	values [DEBUGGER_REG_CS] = (guint64) INFERIOR_REG_CS (arch->current_regs);
	values [DEBUGGER_REG_EFLAGS] = (guint64) INFERIOR_REG_EFLAGS (arch->current_regs);
	values [DEBUGGER_REG_RSP] = (guint64) INFERIOR_REG_RSP (arch->current_regs);
	values [DEBUGGER_REG_SS] = (guint64) INFERIOR_REG_SS (arch->current_regs);
	values [DEBUGGER_REG_FS_BASE] = (guint64) INFERIOR_REG_FS_BASE (arch->current_regs);
	values [DEBUGGER_REG_GS_BASE] = (guint64) INFERIOR_REG_GS_BASE (arch->current_regs);
	values [DEBUGGER_REG_DS] = (guint64) INFERIOR_REG_DS (arch->current_regs);
	values [DEBUGGER_REG_ES] = (guint64) INFERIOR_REG_ES (arch->current_regs);
	values [DEBUGGER_REG_FS] = (guint64) INFERIOR_REG_FS (arch->current_regs);
	values [DEBUGGER_REG_GS] = (guint64) INFERIOR_REG_GS (arch->current_regs);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_set_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	INFERIOR_REG_R15 (arch->current_regs) = values [DEBUGGER_REG_R15];
	INFERIOR_REG_R14 (arch->current_regs) = values [DEBUGGER_REG_R14];
	INFERIOR_REG_R13 (arch->current_regs) = values [DEBUGGER_REG_R13];
	INFERIOR_REG_R12 (arch->current_regs) = values [DEBUGGER_REG_R12];
	INFERIOR_REG_RBP (arch->current_regs) = values [DEBUGGER_REG_RBP];
	INFERIOR_REG_RBX (arch->current_regs) = values [DEBUGGER_REG_RBX];
	INFERIOR_REG_R11 (arch->current_regs) = values [DEBUGGER_REG_R11];
	INFERIOR_REG_R10 (arch->current_regs) = values [DEBUGGER_REG_R10];
	INFERIOR_REG_R9 (arch->current_regs) = values [DEBUGGER_REG_R9];
	INFERIOR_REG_R8 (arch->current_regs) = values [DEBUGGER_REG_R8];
	INFERIOR_REG_RAX (arch->current_regs) = values [DEBUGGER_REG_RAX];
	INFERIOR_REG_RCX (arch->current_regs) = values [DEBUGGER_REG_RCX];
	INFERIOR_REG_RDX (arch->current_regs) = values [DEBUGGER_REG_RDX];
	INFERIOR_REG_RSI (arch->current_regs) = values [DEBUGGER_REG_RSI];
	INFERIOR_REG_RDI (arch->current_regs) = values [DEBUGGER_REG_RDI];
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = values [DEBUGGER_REG_ORIG_RAX];
	INFERIOR_REG_RIP (arch->current_regs) = values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (arch->current_regs) = values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (arch->current_regs) = values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_RSP (arch->current_regs) = values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (arch->current_regs) = values [DEBUGGER_REG_SS];
	INFERIOR_REG_FS_BASE (arch->current_regs) = values [DEBUGGER_REG_FS_BASE];
	INFERIOR_REG_GS_BASE (arch->current_regs) = values [DEBUGGER_REG_GS_BASE];
	INFERIOR_REG_DS (arch->current_regs) = values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (arch->current_regs) = values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (arch->current_regs) = values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (arch->current_regs) = values [DEBUGGER_REG_GS];

	return mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
}

void
mdb_server_get_registers_from_core_file (guint64 *values, const guint8 *buffer)
{
	INFERIOR_REGS_TYPE regs = * (INFERIOR_REGS_TYPE *) buffer;

	values [DEBUGGER_REG_R15] = (guint64) INFERIOR_REG_R15 (regs);
	values [DEBUGGER_REG_R14] = (guint64) INFERIOR_REG_R14 (regs);
	values [DEBUGGER_REG_R13] = (guint64) INFERIOR_REG_R13 (regs);
	values [DEBUGGER_REG_R12] = (guint64) INFERIOR_REG_R12 (regs);
	values [DEBUGGER_REG_RBP] = (guint64) INFERIOR_REG_RBP (regs);
	values [DEBUGGER_REG_RBX] = (guint64) INFERIOR_REG_RBX (regs);
	values [DEBUGGER_REG_R11] = (guint64) INFERIOR_REG_R11 (regs);
	values [DEBUGGER_REG_R10] = (guint64) INFERIOR_REG_R10 (regs);
	values [DEBUGGER_REG_R9] = (guint64) INFERIOR_REG_R9 (regs);
	values [DEBUGGER_REG_R8] = (guint64) INFERIOR_REG_R8 (regs);
	values [DEBUGGER_REG_RAX] = (guint64) INFERIOR_REG_RAX (regs);
	values [DEBUGGER_REG_RCX] = (guint64) INFERIOR_REG_RCX (regs);
	values [DEBUGGER_REG_RDX] = (guint64) INFERIOR_REG_RDX (regs);
	values [DEBUGGER_REG_RSI] = (guint64) INFERIOR_REG_RSI (regs);
	values [DEBUGGER_REG_RDI] = (guint64) INFERIOR_REG_RDI (regs);
	values [DEBUGGER_REG_ORIG_RAX] = (guint64) INFERIOR_REG_ORIG_RAX (regs);
	values [DEBUGGER_REG_RIP] = (guint64) INFERIOR_REG_RIP (regs);
	values [DEBUGGER_REG_CS] = (guint64) INFERIOR_REG_CS (regs);
	values [DEBUGGER_REG_EFLAGS] = (guint64) INFERIOR_REG_EFLAGS (regs);
	values [DEBUGGER_REG_RSP] = (guint64) INFERIOR_REG_RSP (regs);
	values [DEBUGGER_REG_SS] = (guint64) INFERIOR_REG_SS (regs);
	values [DEBUGGER_REG_FS_BASE] = (guint64) INFERIOR_REG_FS_BASE (regs);
	values [DEBUGGER_REG_GS_BASE] = (guint64) INFERIOR_REG_GS_BASE (regs);
	values [DEBUGGER_REG_DS] = (guint64) INFERIOR_REG_DS (regs);
	values [DEBUGGER_REG_ES] = (guint64) INFERIOR_REG_ES (regs);
	values [DEBUGGER_REG_FS] = (guint64) INFERIOR_REG_FS (regs);
	values [DEBUGGER_REG_GS] = (guint64) INFERIOR_REG_GS (regs);
}

ServerCommandError
mdb_server_call_method (ServerHandle *handle, guint64 method_address,
			guint64 method_argument1, guint64 method_argument2,
			guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	long new_rsp;

	guint8 code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			  0xcc, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
	int size = sizeof (code);

	cdata = g_new0 (CallbackData, 1);

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - AMD64_RED_ZONE_SIZE - size - 16;
	new_rsp &= 0xfffffffffffffff0L;

	*((guint64 *) code) = new_rsp + 16;
	*((guint64 *) (code+8)) = callback_argument;

	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_rsp + 16;
	cdata->stack_pointer = new_rsp + 8;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);

	result = mdb_inferior_write_memory (handle->inferior, (unsigned long) new_rsp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
	INFERIOR_REG_RIP (arch->current_regs) = method_address;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument1;
	INFERIOR_REG_RSI (arch->current_regs) = method_argument2;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_continue (handle);
}

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

ServerCommandError
mdb_server_call_method_1 (ServerHandle *handle, guint64 method_address,
			  guint64 method_argument, guint64 data_argument,
			  guint64 data_argument2, const gchar *string_argument,
			  guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	long new_rsp;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
	int static_size = sizeof (static_code);
	int size = static_size + strlen (string_argument) + 1;
	guint8 *code = g_malloc0 (size);
	memcpy (code, static_code, static_size);
	memcpy (code + static_size, string_argument, strlen (string_argument)+1);

	cdata = g_new0 (CallbackData, 1);

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - AMD64_RED_ZONE_SIZE - size - 16;
	new_rsp &= 0xfffffffffffffff0L;

	*((guint64 *) code) = new_rsp + 16;
	*((guint64 *) (code+8)) = callback_argument;

	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_rsp + 16;
	cdata->stack_pointer = new_rsp + 8;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);

	result = mdb_inferior_write_memory (handle->inferior, (unsigned long) new_rsp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
	INFERIOR_REG_RIP (arch->current_regs) = method_address;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument;
	INFERIOR_REG_RSI (arch->current_regs) = data_argument;
	INFERIOR_REG_RDX (arch->current_regs) = data_argument2;
	INFERIOR_REG_RCX (arch->current_regs) = new_rsp + static_size;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_continue (handle);
}

ServerCommandError
mdb_server_call_method_2 (ServerHandle *handle, guint64 method_address,
			  guint32 data_size, gconstpointer data_buffer,
			  guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	long new_rsp;

	int size = 113 + data_size;
	guint8 *code = g_malloc0 (size);

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - AMD64_RED_ZONE_SIZE - size - 16;
	new_rsp &= 0xfffffffffffffff0L;

	*((guint64 *) code) = new_rsp + size - 1;
	*((guint64 *) (code+8)) = INFERIOR_REG_RAX (arch->current_regs);
	*((guint64 *) (code+16)) = INFERIOR_REG_RBX (arch->current_regs);
	*((guint64 *) (code+24)) = INFERIOR_REG_RCX (arch->current_regs);
	*((guint64 *) (code+32)) = INFERIOR_REG_RDX (arch->current_regs);
	*((guint64 *) (code+40)) = INFERIOR_REG_RBP (arch->current_regs);
	*((guint64 *) (code+48)) = INFERIOR_REG_RSP (arch->current_regs);
	*((guint64 *) (code+56)) = INFERIOR_REG_RSI (arch->current_regs);
	*((guint64 *) (code+64)) = INFERIOR_REG_RDI (arch->current_regs);
	*((guint64 *) (code+72)) = INFERIOR_REG_RIP (arch->current_regs);
	*((guint64 *) (code+80)) = INFERIOR_REG_R12 (arch->current_regs);
	*((guint64 *) (code+88)) = INFERIOR_REG_R13 (arch->current_regs);
	*((guint64 *) (code+96)) = INFERIOR_REG_R14 (arch->current_regs);
	*((guint64 *) (code+104)) = INFERIOR_REG_R15 (arch->current_regs);
	*((guint8 *) (code+data_size+112)) = 0xcc;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_rsp + size - 1;
	cdata->stack_pointer = new_rsp + 8;
	cdata->exc_address = 0;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	cdata->pushed_registers = new_rsp + 8;
	_mdb_inferior_set_last_signal (handle->inferior, 0);

	if (data_size > 0) {
		memcpy (code+112, data_buffer, data_size);
		cdata->data_pointer = new_rsp + 112;
		cdata->data_size = data_size;
	}

	result = mdb_inferior_write_memory (handle->inferior, (unsigned long) new_rsp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
	INFERIOR_REG_RIP (arch->current_regs) = method_address;
	INFERIOR_REG_RDI (arch->current_regs) = new_rsp + 8;
	INFERIOR_REG_RSI (arch->current_regs) = new_rsp + 112;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_continue (handle);
}

ServerCommandError
mdb_server_call_method_3 (ServerHandle *handle, guint64 method_address,
			  guint64 method_argument, guint64 address_argument,
			  guint32 blob_size, gconstpointer blob_data,
			  guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	guint64 new_rsp;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
	int static_size = sizeof (static_code);
	int size = static_size + blob_size;
	guint8 *code = g_malloc0 (size);
	guint64 effective_address;
	guint64 blob_start;
	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - AMD64_RED_ZONE_SIZE - size - 16;
	new_rsp &= 0xfffffffffffffff0L;

	blob_start = new_rsp + static_size;

	*((guint64 *) code) = new_rsp + static_size - 1;
	*((guint64 *) (code+8)) = callback_argument;

	effective_address = address_argument ? address_argument : blob_start;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_rsp + static_size - 1;
	cdata->stack_pointer = new_rsp + 8;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);

	result = mdb_inferior_write_memory (handle->inferior, (unsigned long) new_rsp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
	INFERIOR_REG_RIP (arch->current_regs) = method_address;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument;
	INFERIOR_REG_RSI (arch->current_regs) = effective_address;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_continue (handle);
}

ServerCommandError
mdb_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
			       guint64 method_argument, guint32 num_params,
			       guint32 blob_size, guint64 *param_data,
			       gint32 *offset_data, gconstpointer blob_data,
			       guint64 callback_argument, gboolean debug)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	guint64 new_rsp;
	int i;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
	int static_size = sizeof (static_code);
	int size = static_size + (num_params + 3) * 8 + blob_size;
	guint8 *code = g_malloc0 (size);
	guint64 *ptr = (guint64 *) (code + static_size + blob_size);
	guint64 blob_start;
	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - AMD64_RED_ZONE_SIZE - size - 16;
	new_rsp &= 0xfffffffffffffff0L;

	blob_start = new_rsp + static_size;

	for (i = 0; i < num_params; i++) {
		if (offset_data [i] >= 0)
			ptr [i] = blob_start + offset_data [i];
		else
			ptr [i] = param_data [i];
	}

	*((guint64 *) code) = new_rsp + 24;
	*((guint64 *) (code+8)) = callback_argument;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_rsp + 24;
	cdata->stack_pointer = new_rsp + 8;
	cdata->exc_address = new_rsp + 16;
	cdata->callback_argument = callback_argument;
	cdata->debug = debug;
	cdata->is_rti = TRUE;
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);

	result = mdb_inferior_write_memory (handle->inferior, (unsigned long) new_rsp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
	INFERIOR_REG_RIP (arch->current_regs) = invoke_method;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument;
	INFERIOR_REG_RSI (arch->current_regs) = ptr [0];
	INFERIOR_REG_RDX (arch->current_regs) = new_rsp + static_size + blob_size + 8;
	INFERIOR_REG_RCX (arch->current_regs) = new_rsp + 16;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_continue (handle);
}

ServerCommandError
mdb_server_mark_rti_frame (ServerHandle *handle)
{
	CallbackData *cdata;

	cdata = get_callback_data (handle->arch);
	if (!cdata)
		return COMMAND_ERROR_NO_CALLBACK_FRAME;

	cdata->rti_frame = INFERIOR_REG_RSP (handle->arch->current_regs) + 8;
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_abort_invoke (ServerHandle *handle, guint64 rti_id)
{
	CallbackData *cdata;

	cdata = get_callback_data (handle->arch);
	if (!cdata || !cdata->is_rti || (cdata->callback_argument != rti_id))
		return COMMAND_ERROR_NO_CALLBACK_FRAME;

	if (mdb_inferior_set_registers (handle->inferior, &cdata->saved_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore registers after returning from a call");

	_mdb_inferior_set_last_signal (handle->inferior, cdata->saved_signal);
	g_ptr_array_remove (handle->arch->callback_stack, cdata);

	mdb_arch_get_registers (handle);
	g_free (cdata);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_callback_frame (ServerHandle *handle, guint64 stack_pointer,
			       gboolean exact_match, CallbackInfo *info)
{
	int i;

	for (i = 0; i < handle->arch->callback_stack->len; i++) {
		CallbackData *cdata = g_ptr_array_index (handle->arch->callback_stack, i);

		if (cdata->rti_frame) {
			if (exact_match) {
				if (cdata->rti_frame != stack_pointer)
					continue;
			} else {
				if (cdata->rti_frame < stack_pointer)
					continue;
			}
			info->is_rti_frame = 1;
			info->is_exact_match = cdata->rti_frame == stack_pointer;
			info->stack_pointer = cdata->rti_frame;
		} else {
			if (exact_match) {
				if (cdata->stack_pointer != stack_pointer)
					continue;
			} else {
				if (cdata->stack_pointer < stack_pointer)
					continue;
			}
			info->is_rti_frame = 0;
			info->is_exact_match = cdata->stack_pointer == stack_pointer;
			info->stack_pointer = cdata->stack_pointer;
		}

		info->callback_argument = cdata->callback_argument;
		info->call_address = cdata->call_address;

		info->saved_registers [DEBUGGER_REG_R15] = (guint64) INFERIOR_REG_R15 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R14] = (guint64) INFERIOR_REG_R14 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R13] = (guint64) INFERIOR_REG_R13 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R12] = (guint64) INFERIOR_REG_R12 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RBP] = (guint64) INFERIOR_REG_RBP (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RBX] = (guint64) INFERIOR_REG_RBX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R11] = (guint64) INFERIOR_REG_R11 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R10] = (guint64) INFERIOR_REG_R10 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R9] = (guint64) INFERIOR_REG_R9 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_R8] = (guint64) INFERIOR_REG_R8 (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RAX] = (guint64) INFERIOR_REG_RAX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RCX] = (guint64) INFERIOR_REG_RCX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RDX] = (guint64) INFERIOR_REG_RDX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RSI] = (guint64) INFERIOR_REG_RSI (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RDI] = (guint64) INFERIOR_REG_RDI (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_ORIG_RAX] = (guint64) INFERIOR_REG_ORIG_RAX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RIP] = (guint64) INFERIOR_REG_RIP (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_CS] = (guint64) INFERIOR_REG_CS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_EFLAGS] = (guint64) INFERIOR_REG_EFLAGS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RSP] = (guint64) INFERIOR_REG_RSP (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_SS] = (guint64) INFERIOR_REG_SS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_FS_BASE] = (guint64) INFERIOR_REG_FS_BASE (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_GS_BASE] = (guint64) INFERIOR_REG_GS_BASE (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_DS] = (guint64) INFERIOR_REG_DS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_ES] = (guint64) INFERIOR_REG_ES (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_FS] = (guint64) INFERIOR_REG_FS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_GS] = (guint64) INFERIOR_REG_GS (cdata->saved_regs);

		return COMMAND_ERROR_NONE;
	}

	return COMMAND_ERROR_NO_CALLBACK_FRAME;
}

ServerCommandError
mdb_server_restart_notification (ServerHandle *handle)
{
	if (!handle->mono_runtime ||
	    (INFERIOR_REG_RIP (handle->arch->current_regs) - 1 != handle->mono_runtime->notification_address))
		return COMMAND_ERROR_INTERNAL_ERROR;

	INFERIOR_REG_RIP (handle->arch->current_regs)--;
	return mdb_inferior_set_registers (handle->inferior, &handle->arch->current_regs);
}
