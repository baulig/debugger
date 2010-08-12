#if !defined(X86_ARCH_C) || !defined(__i386__)
#error "This file must not be used directly."
#endif

#include "i386-arch.h"

#if defined(__linux__) || defined(__FreeBSD__)
#include <signal.h>
#endif

struct _ArchInfo
{
	INFERIOR_REGS_TYPE current_regs;
	GPtrArray *callback_stack;
	CodeBufferData *code_buffer;
	BreakpointManager *hw_bpm;
	guint32 pushed_regs_rsp;
	int dr_index [DR_NADDR];
};

typedef struct
{
	INFERIOR_REGS_TYPE saved_regs;
	guint64 callback_argument;
	guint32 call_address;
	guint32 stack_pointer;
	guint32 rti_frame;
	guint32 exc_address;
	guint32 pushed_registers;
	guint32 data_pointer;
	guint32 data_size;
	int saved_signal;
	gboolean debug;
	gboolean is_rti;
} CallbackData;

ArchType
mdb_server_get_arch_type (void)
{
	return ARCH_TYPE_I386;
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

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

ServerCommandError
mdb_server_call_method (ServerHandle *handle, guint64 method_address,
			guint64 method_argument1, guint64 method_argument2,
			guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	guint32 new_esp, call_disp;
	CallbackData *cdata;

	guint8 code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
			  0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68,
			  0x00, 0x00, 0x00, 0x00, 0xe8, 0x00, 0x00, 0x00,
			  0x00, 0xcc };
	int size = sizeof (code);

	cdata = g_new0 (CallbackData, 1);

	new_esp = (gsize) INFERIOR_REG_RSP (arch->current_regs) - size;

	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_esp + 26;
	cdata->stack_pointer = new_esp - 16;
	cdata->callback_argument = callback_argument;

#if defined(__linux__) || defined(__FreeBSD__)
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);
#endif

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = method_argument2 >> 32;
	*((guint32 *) (code+6)) = method_argument2 & 0xffffffff;
	*((guint32 *) (code+11)) = method_argument1 >> 32;
	*((guint32 *) (code+16)) = method_argument1 & 0xffffffff;
	*((guint32 *) (code+21)) = call_disp - 25;

	result = mdb_inferior_write_memory (handle->inferior, (gsize) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_make_memory_executable (handle->inferior, (gsize) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if defined(__linux__)
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
#endif
	INFERIOR_REG_RSP (arch->current_regs) = INFERIOR_REG_RIP (arch->current_regs) = new_esp;

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
	guint32 new_esp, call_disp;
	CallbackData *cdata;

	static guint8 static_code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
					0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68,
					0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00, 0x00,
					0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00,
					0x00, 0x00, 0x00, 0xe8, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + strlen (string_argument) + 1;
	guint8 *code = g_malloc0 (size);
	memcpy (code, static_code, static_size);
	strcpy ((char *) (code + static_size), string_argument);

	cdata = g_new0 (CallbackData, 1);

	new_esp = (gsize) INFERIOR_REG_RSP (arch->current_regs) - size;

	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_esp + static_size;
	cdata->stack_pointer = new_esp - 28;
	cdata->callback_argument = callback_argument;

#if defined(__linux__) || defined(__FreeBSD__)
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);
#endif

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = new_esp + static_size;
	*((guint32 *) (code+6)) = data_argument2 >> 32;
	*((guint32 *) (code+11)) = data_argument2 & 0xffffffff;
	*((guint32 *) (code+16)) = data_argument >> 32;
	*((guint32 *) (code+21)) = data_argument & 0xffffffff;
	*((guint32 *) (code+26)) = method_argument >> 32;
	*((guint32 *) (code+31)) = method_argument & 0xffffffff;
	*((guint32 *) (code+36)) = call_disp - 40;

	result = mdb_inferior_write_memory (handle->inferior, (gsize) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_make_memory_executable (handle->inferior, (gsize) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if defined(__linux__)
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
#endif
	INFERIOR_REG_RSP (arch->current_regs) = INFERIOR_REG_RIP (arch->current_regs) = new_esp;

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
	guint32 new_esp;

	int size = 57 + data_size;
	guint8 *code = g_malloc0 (size);

	new_esp = INFERIOR_REG_RSP (arch->current_regs) - size;

	*((guint32 *) code) = new_esp + size - 1;
	*((guint64 *) (code+4)) = new_esp + 20;
	*((guint64 *) (code+12)) = new_esp + 56;
	*((guint32 *) (code+20)) = INFERIOR_REG_RAX (arch->current_regs);
	*((guint32 *) (code+24)) = INFERIOR_REG_RBX (arch->current_regs);
	*((guint32 *) (code+28)) = INFERIOR_REG_RCX (arch->current_regs);
	*((guint32 *) (code+32)) = INFERIOR_REG_RDX (arch->current_regs);
	*((guint32 *) (code+36)) = INFERIOR_REG_RBP (arch->current_regs);
	*((guint32 *) (code+40)) = INFERIOR_REG_RSP (arch->current_regs);
	*((guint32 *) (code+44)) = INFERIOR_REG_RSI (arch->current_regs);
	*((guint32 *) (code+48)) = INFERIOR_REG_RDI (arch->current_regs);
	*((guint32 *) (code+52)) = INFERIOR_REG_RIP (arch->current_regs);
	*((guint8 *) (code+data_size+56)) = 0xcc;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_esp + size;
	cdata->stack_pointer = new_esp + 4;
	cdata->exc_address = 0;
	cdata->callback_argument = callback_argument;
	cdata->pushed_registers = new_esp + 4;

#if defined(__linux__) || defined(__FreeBSD__)
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);
#endif

	if (data_size > 0) {
		memcpy (code+56, data_buffer, data_size);
		cdata->data_pointer = new_esp + 56;
		cdata->data_size = data_size;
	}

	result = mdb_inferior_write_memory (handle->inferior, (gsize) new_esp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_make_memory_executable (handle->inferior, (gsize) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if defined(__linux__)
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
#endif
	INFERIOR_REG_RIP (arch->current_regs) = (gsize) method_address;
	INFERIOR_REG_RSP (arch->current_regs) = new_esp;

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
	guint32 new_esp;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + blob_size;
	guint8 *code = g_malloc0 (size);
	guint32 effective_address;
	guint32 blob_start;
	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	new_esp = INFERIOR_REG_RSP (arch->current_regs) - size;

	blob_start = new_esp + static_size;
	effective_address = (gsize) (address_argument ? address_argument : blob_start);

	*((guint32 *) code) = new_esp + static_size - 1;
	*((guint32 *) (code+4)) = (gsize) method_argument;
	*((guint32 *) (code+12)) = effective_address;
	*((guint32 *) (code+20)) = new_esp;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_esp + static_size;
	cdata->stack_pointer = new_esp + 4;
	cdata->callback_argument = callback_argument;

#if defined(__linux__) || defined(__FreeBSD__)
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);
#endif

	result = mdb_inferior_write_memory (handle->inferior, (unsigned long) new_esp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_make_memory_executable (handle->inferior, (gsize) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if defined(__linux__)
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
#endif
	INFERIOR_REG_RIP (arch->current_regs) = (gsize) method_address;
	INFERIOR_REG_RSP (arch->current_regs) = new_esp;
	INFERIOR_REG_RBP (arch->current_regs) = 0;

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
	guint32 new_esp;
	guint32 i;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + (num_params + 3) * 4 + blob_size;
	guint8 *code = g_malloc0 (size);
	guint32 *ptr = (guint32 *) (code + static_size + blob_size);
	guint64 blob_start;

	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	new_esp = (gsize) INFERIOR_REG_RSP (arch->current_regs) - size;
	blob_start = new_esp + static_size;

	for (i = 0; i < num_params; i++) {
		if (offset_data [i] >= 0)
			ptr [i] = (gsize) blob_start + offset_data [i];
		else
			ptr [i] = (gsize) param_data [i];
	}

	*((guint32 *) code) = new_esp + static_size - 1;
	*((guint32 *) (code+4)) = (gsize) method_argument;
	*((guint32 *) (code+8)) = ptr [0];
	*((guint32 *) (code+12)) = new_esp + static_size + blob_size + 4;
	*((guint32 *) (code+16)) = new_esp + 20;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	cdata->call_address = new_esp + static_size;
	cdata->stack_pointer = new_esp + 4;
	cdata->exc_address = new_esp + 20;
	cdata->callback_argument = callback_argument;
	cdata->debug = debug;
	cdata->is_rti = TRUE;

#if defined(__linux__) || defined(__FreeBSD__)
	cdata->saved_signal = _mdb_inferior_get_last_signal (handle->inferior);
	_mdb_inferior_set_last_signal (handle->inferior, 0);
#endif

	result = mdb_inferior_write_memory (handle->inferior, (gsize) new_esp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_make_memory_executable (handle->inferior, (gsize) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if defined(__linux__)
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = -1;
#endif
	INFERIOR_REG_RIP (arch->current_regs) = (gsize) invoke_method;
	INFERIOR_REG_RSP (arch->current_regs) = new_esp;
	INFERIOR_REG_RBP (arch->current_regs) = 0;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_continue (handle);
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

#if defined(__linux__) || defined(__FreeBSD__)
	if (stopsig == SIGSTOP) {
		e->type = SERVER_EVENT_CHILD_INTERRUPTED;
		return e;
	}

	if (stopsig != SIGTRAP) {
		e->arg = stopsig;
		return e;
	}
#endif

	if (server->mono_runtime &&
	    ((gsize) INFERIOR_REG_RIP (arch->current_regs) - 1 == server->mono_runtime->notification_address)) {
		guint32 addr = (gsize) INFERIOR_REG_RSP (arch->current_regs) + 4;
		guint64 data [3];

		if (mdb_inferior_read_memory (inferior, addr, 24, &data))
			return e;

		e->arg = data [0];
		e->data1 = data [1];
		e->data2 = data [2];
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

	if (mdb_arch_check_breakpoint (server, (gsize) INFERIOR_REG_RIP (arch->current_regs) - 1, &e->arg)) {
		INFERIOR_REG_RIP (arch->current_regs)--;
		mdb_inferior_set_registers (inferior, &arch->current_regs);
		e->type = SERVER_EVENT_CHILD_HIT_BREAKPOINT;
		return e;
	}

	cdata = get_callback_data (arch);
	if (cdata && (cdata->call_address == INFERIOR_REG_RIP (arch->current_regs))) {
		guint64 exc_object;

		if (cdata->pushed_registers) {
			guint32 pushed_regs [9];

			if (mdb_inferior_read_memory (inferior, cdata->pushed_registers, 36, &pushed_regs)) {
				g_warning (G_STRLOC ": Can't restore registers after returning from a call");

				e->type = SERVER_EVENT_INTERNAL_ERROR;
				return e;
			}

			INFERIOR_REG_RAX (cdata->saved_regs) = pushed_regs [0];
			INFERIOR_REG_RBX (cdata->saved_regs) = pushed_regs [1];
			INFERIOR_REG_RCX (cdata->saved_regs) = pushed_regs [2];
			INFERIOR_REG_RDX (cdata->saved_regs) = pushed_regs [3];
			INFERIOR_REG_RBP (cdata->saved_regs) = pushed_regs [4];
			INFERIOR_REG_RSP (cdata->saved_regs) = pushed_regs [5];
			INFERIOR_REG_RSI (cdata->saved_regs) = pushed_regs [6];
			INFERIOR_REG_RDI (cdata->saved_regs) = pushed_regs [7];
			INFERIOR_REG_RIP (cdata->saved_regs) = pushed_regs [8];
		}

		if (mdb_inferior_set_registers (inferior, &cdata->saved_regs) != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC ": Can't restore registers after returning from a call");
			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		e->arg = cdata->callback_argument;
		e->data1 = (gsize) INFERIOR_REG_RAX (arch->current_regs);

		if (cdata->data_pointer) {
			e->opt_data_size = cdata->data_size;
			e->opt_data = g_malloc0 (cdata->data_size);

			if (mdb_inferior_read_memory (server->inferior, cdata->data_pointer, cdata->data_size, e->opt_data)) {
				e->type = SERVER_EVENT_INTERNAL_ERROR;
				return e;
			}
		}


		if (cdata->exc_address &&
		    (mdb_inferior_peek_word (server->inferior, cdata->exc_address, &exc_object) != COMMAND_ERROR_NONE)) {
			g_warning (G_STRLOC ": Can't get exc object");
			e->type = SERVER_EVENT_INTERNAL_ERROR;
			return e;
		}

		e->data2 = (gsize) exc_object;

#if defined(__linux__) || defined(__FreeBSD__)
		_mdb_inferior_set_last_signal (inferior, cdata->saved_signal);
#endif

		g_ptr_array_remove (arch->callback_stack, cdata);

		mdb_arch_get_registers (server);

		if (cdata->is_rti) {
			g_free (cdata);
			e->type = SERVER_EVENT_RUNTIME_INVOKE_DONE;
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

	cbuffer = arch->code_buffer;
	if (cbuffer) {
		server->mono_runtime->executable_code_bitfield [cbuffer->slot] = 0;

		if (cbuffer->code_address + cbuffer->insn_size != INFERIOR_REG_RIP (arch->current_regs)) {
			g_warning (G_STRLOC ": %x - %x,%d - %x - %x", cbuffer->original_rip,
				   cbuffer->code_address, cbuffer->insn_size,
				   cbuffer->code_address + cbuffer->insn_size,
				   INFERIOR_REG_RIP (arch->current_regs));
			return e;
		}

		INFERIOR_REG_RIP (arch->current_regs) = cbuffer->original_rip;
		if (cbuffer->update_ip)
			INFERIOR_REG_RIP (arch->current_regs) += cbuffer->insn_size;
		if (mdb_inferior_set_registers (inferior, &arch->current_regs) != COMMAND_ERROR_NONE) {
			g_error (G_STRLOC ": Can't restore registers");
		}

		g_free (cbuffer);
		arch->code_buffer = NULL;
		return e;
	}

	if (mdb_inferior_peek_word (server->inferior, GPOINTER_TO_INT (INFERIOR_REG_RIP (arch->current_regs) - 1), &code) != COMMAND_ERROR_NONE)
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

	values [DEBUGGER_REG_RBX] = (gsize) INFERIOR_REG_RBX (arch->current_regs);
	values [DEBUGGER_REG_RCX] = (gsize) INFERIOR_REG_RCX (arch->current_regs);
	values [DEBUGGER_REG_RDX] = (gsize) INFERIOR_REG_RDX (arch->current_regs);
	values [DEBUGGER_REG_RSI] = (gsize) INFERIOR_REG_RSI (arch->current_regs);
	values [DEBUGGER_REG_RDI] = (gsize) INFERIOR_REG_RDI (arch->current_regs);
	values [DEBUGGER_REG_RBP] = (gsize) INFERIOR_REG_RBP (arch->current_regs);
	values [DEBUGGER_REG_RAX] = (gsize) INFERIOR_REG_RAX (arch->current_regs);
	values [DEBUGGER_REG_DS] = (gsize) INFERIOR_REG_DS (arch->current_regs);
	values [DEBUGGER_REG_ES] = (gsize) INFERIOR_REG_ES (arch->current_regs);
	values [DEBUGGER_REG_FS] = (gsize) INFERIOR_REG_FS (arch->current_regs);
	values [DEBUGGER_REG_GS] = (gsize) INFERIOR_REG_GS (arch->current_regs);
	values [DEBUGGER_REG_RIP] = (gsize) INFERIOR_REG_RIP (arch->current_regs);
	values [DEBUGGER_REG_CS] = (gsize) INFERIOR_REG_CS (arch->current_regs);
	values [DEBUGGER_REG_EFLAGS] = (gsize) INFERIOR_REG_EFLAGS (arch->current_regs);
	values [DEBUGGER_REG_RSP] = (gsize) INFERIOR_REG_RSP (arch->current_regs);
	values [DEBUGGER_REG_SS] = (gsize) INFERIOR_REG_SS (arch->current_regs);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_set_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	INFERIOR_REG_RBX (arch->current_regs) = (gsize) values [DEBUGGER_REG_RBX];
	INFERIOR_REG_RCX (arch->current_regs) = (gsize) values [DEBUGGER_REG_RCX];
	INFERIOR_REG_RDX (arch->current_regs) = (gsize) values [DEBUGGER_REG_RDX];
	INFERIOR_REG_RSI (arch->current_regs) = (gsize) values [DEBUGGER_REG_RSI];
	INFERIOR_REG_RDI (arch->current_regs) = (gsize) values [DEBUGGER_REG_RDI];
	INFERIOR_REG_RBP (arch->current_regs) = (gsize) values [DEBUGGER_REG_RBP];
	INFERIOR_REG_RAX (arch->current_regs) = (gsize) values [DEBUGGER_REG_RAX];
	INFERIOR_REG_DS (arch->current_regs) = (gsize) values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (arch->current_regs) = (gsize) values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (arch->current_regs) = (gsize) values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (arch->current_regs) = (gsize) values [DEBUGGER_REG_GS];
	INFERIOR_REG_RIP (arch->current_regs) = (gsize) values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (arch->current_regs) = (gsize) values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (arch->current_regs) = (gsize) values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_RSP (arch->current_regs) = (gsize) values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (arch->current_regs) = (gsize) values [DEBUGGER_REG_SS];

	return mdb_inferior_set_registers (handle->inferior, &arch->current_regs);
}

void
mdb_server_get_registers_from_core_file (guint64 *values, const guint8 *buffer)
{
	INFERIOR_REGS_TYPE regs = * (INFERIOR_REGS_TYPE *) buffer;

	values [DEBUGGER_REG_RBX] = (gsize) INFERIOR_REG_RBX (regs);
	values [DEBUGGER_REG_RCX] = (gsize) INFERIOR_REG_RCX (regs);
	values [DEBUGGER_REG_RDX] = (gsize) INFERIOR_REG_RDX (regs);
	values [DEBUGGER_REG_RSI] = (gsize) INFERIOR_REG_RSI (regs);
	values [DEBUGGER_REG_RDI] = (gsize) INFERIOR_REG_RDI (regs);
	values [DEBUGGER_REG_RBP] = (gsize) INFERIOR_REG_RBP (regs);
	values [DEBUGGER_REG_RAX] = (gsize) INFERIOR_REG_RAX (regs);
	values [DEBUGGER_REG_DS] = (gsize) INFERIOR_REG_DS (regs);
	values [DEBUGGER_REG_ES] = (gsize) INFERIOR_REG_ES (regs);
	values [DEBUGGER_REG_FS] = (gsize) INFERIOR_REG_FS (regs);
	values [DEBUGGER_REG_GS] = (gsize) INFERIOR_REG_GS (regs);
	values [DEBUGGER_REG_RIP] = (gsize) INFERIOR_REG_RIP (regs);
	values [DEBUGGER_REG_CS] = (gsize) INFERIOR_REG_CS (regs);
	values [DEBUGGER_REG_EFLAGS] = (gsize) INFERIOR_REG_EFLAGS (regs);
	values [DEBUGGER_REG_RSP] = (gsize) INFERIOR_REG_RSP (regs);
	values [DEBUGGER_REG_SS] = (gsize) INFERIOR_REG_SS (regs);
}

ServerCommandError
mdb_server_mark_rti_frame (ServerHandle *handle)
{
	CallbackData *cdata;

	cdata = get_callback_data (handle->arch);
	if (!cdata)
		return COMMAND_ERROR_NO_CALLBACK_FRAME;

	cdata->rti_frame = INFERIOR_REG_RSP (handle->arch->current_regs) + 4;
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

#if defined(__linux__) || defined(__FreeBSD__)
	_mdb_inferior_set_last_signal (handle->inferior, cdata->saved_signal);
#endif

	g_ptr_array_remove (handle->arch->callback_stack, cdata);

	mdb_arch_get_registers (handle);
	g_free (cdata);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_callback_frame (ServerHandle *handle, guint64 stack_pointer,
			       gboolean exact_match, CallbackInfo *info)
{
	guint32 i;

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

		info->saved_registers [DEBUGGER_REG_RBX] = (gsize) INFERIOR_REG_RBX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RCX] = (gsize) INFERIOR_REG_RCX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RDX] = (gsize) INFERIOR_REG_RDX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RSI] = (gsize) INFERIOR_REG_RSI (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RDI] = (gsize) INFERIOR_REG_RDI (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RBP] = (gsize) INFERIOR_REG_RBP (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RAX] = (gsize) INFERIOR_REG_RAX (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_DS] = (gsize) INFERIOR_REG_DS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_ES] = (gsize) INFERIOR_REG_ES (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_FS] = (gsize) INFERIOR_REG_FS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_GS] = (gsize) INFERIOR_REG_GS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RIP] = (gsize) INFERIOR_REG_RIP (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_CS] = (gsize) INFERIOR_REG_CS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_EFLAGS] = (gsize) INFERIOR_REG_EFLAGS (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_RSP] = (gsize) INFERIOR_REG_RSP (cdata->saved_regs);
		info->saved_registers [DEBUGGER_REG_SS] = (gsize) INFERIOR_REG_SS (cdata->saved_regs);

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
