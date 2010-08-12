#define X86_ARCH_C 1
#include <server.h>
#include <breakpoints.h>
#if HAVE_UNISTD_H
#include <unistd.h>
#endif
#include <string.h>
#include <fcntl.h>
#include <errno.h>

#include "x86-arch.h"

typedef struct
{
	int slot;
	int insn_size;
	gboolean update_ip;
	long code_address;
	long original_rip;
} CodeBufferData;

/* DR7 Debug Control register fields.  */

/* How many bits to skip in DR7 to get to R/W and LEN fields.  */
#define DR_CONTROL_SHIFT	16
/* How many bits in DR7 per R/W and LEN field for each watchpoint.  */
#define DR_CONTROL_SIZE		4

/* Watchpoint/breakpoint read/write fields in DR7.  */
#define DR_RW_EXECUTE		(0x0) /* break on instruction execution */
#define DR_RW_WRITE		(0x1) /* break on data writes */
#define DR_RW_READ		(0x3) /* break on data reads or writes */

/* This is here for completeness.  No platform supports this
   functionality yet (as of Mar-2001).  Note that the DE flag in the
   CR4 register needs to be set to support this.  */
#ifndef DR_RW_IORW
#define DR_RW_IORW		(0x2) /* break on I/O reads or writes */
#endif

/* Watchpoint/breakpoint length fields in DR7.  The 2-bit left shift
   is so we could OR this with the read/write field defined above.  */
#define DR_LEN_1		(0x0 << 2) /* 1-byte region watch or breakpt */
#define DR_LEN_2		(0x1 << 2) /* 2-byte region watch */
#define DR_LEN_4		(0x3 << 2) /* 4-byte region watch */
#define DR_LEN_8		(0x2 << 2) /* 8-byte region watch (x86-64) */

/* Local and Global Enable flags in DR7. */
#define DR_LOCAL_ENABLE_SHIFT	0   /* extra shift to the local enable bit */
#define DR_GLOBAL_ENABLE_SHIFT	1   /* extra shift to the global enable bit */
#define DR_ENABLE_SIZE		2   /* 2 enable bits per debug register */

/* The I'th debug register is vacant if its Local and Global Enable
   bits are reset in the Debug Control register.  */
#define X86_DR_VACANT(r,i) \
  ((INFERIOR_REG_DR_CONTROL(r) & (3 << (DR_ENABLE_SIZE * (i)))) == 0)

/* Locally enable the break/watchpoint in the I'th debug register.  */
#define X86_DR_LOCAL_ENABLE(r,i) \
  INFERIOR_REG_DR_CONTROL(r) |= (1 << (DR_LOCAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Globally enable the break/watchpoint in the I'th debug register.  */
#define X86_DR_GLOBAL_ENABLE(r,i) \
  INFERIOR_REG_DR_CONTROL(r) |= (1 << (DR_GLOBAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Disable the break/watchpoint in the I'th debug register.  */
#define X86_DR_DISABLE(r,i) \
  INFERIOR_REG_DR_CONTROL(r) &= ~(3 << (DR_ENABLE_SIZE * (i)))

/* Set in DR7 the RW and LEN fields for the I'th debug register.  */
#define X86_DR_SET_RW_LEN(r,i,rwlen) \
  do { \
    INFERIOR_REG_DR_CONTROL(r) &= ~(0x0f << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i))); \
    INFERIOR_REG_DR_CONTROL(r) |= ((rwlen) << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i))); \
  } while (0)

/* Get from DR7 the RW and LEN fields for the I'th debug register.  */
#define X86_DR_GET_RW_LEN(r,i) \
  ((INFERIOR_REG_DR_CONTROL(r) >> (DR_CONTROL_SHIFT + DR_CONTROL_SIZE * (i))) & 0x0f)

/* Did the watchpoint whose address is in the I'th register break?  */
#define X86_DR_WATCH_HIT(r,i) \
  (INFERIOR_REG_DR_STATUS(r) & (1 << (i)))

#define AMD64_RED_ZONE_SIZE 128

#if defined(__x86_64__)
#include "x86_64-arch.c"
#elif defined(__i386__)
#include "i386-arch.c"
#else
#error "Unknown architecture."
#endif

ServerCommandError
mdb_server_current_insn_is_bpt (ServerHandle *server, guint32 *is_breakpoint)
{
	mono_debugger_breakpoint_manager_lock ();
	if (mono_debugger_breakpoint_manager_lookup (server->arch->hw_bpm, INFERIOR_REG_RIP (server->arch->current_regs)) ||
	    mono_debugger_breakpoint_manager_lookup (server->bpm, INFERIOR_REG_RIP (server->arch->current_regs)))
		*is_breakpoint = TRUE;
	else
		*is_breakpoint = FALSE;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

void
mdb_server_remove_breakpoints_from_target_memory (ServerHandle *server, guint64 start,
						  guint32 size, gpointer buffer)
{
	GPtrArray *breakpoints;
	guint8 *ptr = buffer;
	guint32 i;

	mono_debugger_breakpoint_manager_lock ();

	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (server->bpm);
	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);
		guint32 offset;

		if (info->is_hardware_bpt || !info->enabled)
			continue;
		if ((info->address < start) || (info->address >= start+size))
			continue;

		offset = (gsize) (info->address - start);
		ptr [offset] = info->saved_insn;
	}

	mono_debugger_breakpoint_manager_unlock ();
}

ServerCommandError
mdb_server_get_frame (ServerHandle *server, StackFrame *frame)
{
	ServerCommandError result;

	result = mdb_arch_get_registers (server);
	if (result != COMMAND_ERROR_NONE)
		return result;

	frame->address = (gsize) INFERIOR_REG_RIP (server->arch->current_regs);
	frame->stack_pointer = (gsize) INFERIOR_REG_RSP (server->arch->current_regs);
	frame->frame_address = (gsize) INFERIOR_REG_RBP (server->arch->current_regs);
	return COMMAND_ERROR_NONE;
}

gboolean
mdb_arch_check_breakpoint (ServerHandle *server, guint64 address, guint64 *retval)
{
	BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock ();
	info = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (server->bpm, address);
	if (!info || !info->enabled) {
		mono_debugger_breakpoint_manager_unlock ();
		return FALSE;
	}

	*retval = info->id;
	mono_debugger_breakpoint_manager_unlock ();
	return TRUE;
}

BreakpointInfo *
mdb_arch_lookup_breakpoint (ServerHandle *server, guint32 idx, BreakpointManager **out_bpm)
{
	BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock ();
	info = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (server->arch->hw_bpm, idx);
	if (info) {
		if (out_bpm)
			*out_bpm = server->arch->hw_bpm;
		mono_debugger_breakpoint_manager_unlock ();
		return info;
	}

	info = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (server->bpm, idx);
	if (info) {
		if (out_bpm)
			*out_bpm = server->bpm;
		mono_debugger_breakpoint_manager_unlock ();
		return info;
	}

	if (out_bpm)
		*out_bpm = NULL;

	mono_debugger_breakpoint_manager_unlock ();
	return info;
}

ServerCommandError
mdb_arch_get_registers (ServerHandle *server)
{
	return mdb_inferior_get_registers (server->inferior, &server->arch->current_regs);
}

ServerCommandError
mdb_server_count_registers (ServerHandle *server, guint32 *out_count)
{
	*out_count = DEBUGGER_REG_LAST;
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			    guint32 *target_address_size, guint32 *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint64);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_push_registers (ServerHandle *server, guint64 *new_esp)
{
	ArchInfo *arch = server->arch;
	ServerCommandError result;

	if (arch->pushed_regs_rsp)
		return COMMAND_ERROR_INTERNAL_ERROR;

	arch->pushed_regs_rsp = INFERIOR_REG_RSP (arch->current_regs);

#if defined(__x86_64__)
	INFERIOR_REG_RSP (arch->current_regs) -= AMD64_RED_ZONE_SIZE + sizeof (arch->current_regs) + 16;
	INFERIOR_REG_RSP (arch->current_regs) &= 0xfffffffffffffff0L;
#else
	INFERIOR_REG_RSP (arch->current_regs) -= sizeof (arch->current_regs);
#endif

	result = mdb_inferior_set_registers (server->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	*new_esp = INFERIOR_REG_RSP (arch->current_regs);

	return mdb_inferior_write_memory (
		server->inferior, *new_esp, sizeof (arch->current_regs), &arch->current_regs);
}

ServerCommandError
mdb_server_pop_registers (ServerHandle *server)
{
	ArchInfo *arch = server->arch;
	ServerCommandError result;

	if (!arch->pushed_regs_rsp)
		return COMMAND_ERROR_INTERNAL_ERROR;

	INFERIOR_REG_RSP (arch->current_regs) = arch->pushed_regs_rsp;
	arch->pushed_regs_rsp = 0;

	result = mdb_inferior_set_registers (server->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static int
find_breakpoint_table_slot (MonoRuntimeInfo *runtime)
{
	guint32 i;

	for (i = 1; i < runtime->breakpoint_table_size; i++) {
		if (runtime->breakpoint_table_bitfield [i])
			continue;

		runtime->breakpoint_table_bitfield [i] = 1;
		return i;
	}

	return -1;
}

static ServerCommandError
runtime_info_enable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	guint64 table_address, index_address;
	int slot;

	runtime = server->mono_runtime;
	g_assert (runtime);

	slot = find_breakpoint_table_slot (runtime);
	if (slot < 0)
		return COMMAND_ERROR_INTERNAL_ERROR;

	breakpoint->runtime_table_slot = slot;

	table_address = runtime->breakpoint_info_area + 8 * slot;
	index_address = runtime->breakpoint_table + 4 * slot;

	result = mdb_inferior_poke_word (server->inferior, table_address, (gsize) breakpoint->address);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_poke_word (server->inferior, table_address + 4, (gsize) breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = mdb_inferior_poke_word (server->inferior, index_address, (gsize) slot);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
runtime_info_disable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	guint64 index_address;
	int slot;

	runtime = server->mono_runtime;
	g_assert (runtime);

	slot = breakpoint->runtime_table_slot;
	index_address = runtime->breakpoint_table + runtime->address_size * slot;

	result = mdb_inferior_poke_word (server->inferior, index_address, 0);
	if (result != COMMAND_ERROR_NONE)
		return result;

	runtime->breakpoint_table_bitfield [slot] = 0;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_arch_enable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	ArchInfo *arch = server->arch;
	InferiorHandle *inferior = server->inferior;
	char bopcode = 0xcc;
	gsize address;

	if (breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (gsize) breakpoint->address;

	if (breakpoint->dr_index >= 0) {
#if defined(__x86_64__)
		if (breakpoint->type == HARDWARE_BREAKPOINT_READ)
			X86_DR_SET_RW_LEN (arch->current_regs, breakpoint->dr_index, DR_RW_READ | DR_LEN_8);
		else if (breakpoint->type == HARDWARE_BREAKPOINT_WRITE)
			X86_DR_SET_RW_LEN (arch->current_regs, breakpoint->dr_index, DR_RW_WRITE | DR_LEN_8);
		else
			X86_DR_SET_RW_LEN (arch->current_regs, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
#else
		if (breakpoint->type == HARDWARE_BREAKPOINT_READ)
			X86_DR_SET_RW_LEN (arch->current_regs, breakpoint->dr_index, DR_RW_READ | DR_LEN_4);
		else if (breakpoint->type == HARDWARE_BREAKPOINT_WRITE)
			X86_DR_SET_RW_LEN (arch->current_regs, breakpoint->dr_index, DR_RW_WRITE | DR_LEN_4);
		else
			X86_DR_SET_RW_LEN (arch->current_regs, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
#endif
		X86_DR_LOCAL_ENABLE (arch->current_regs, breakpoint->dr_index);
		INFERIOR_REG_DR_N (arch->current_regs, breakpoint->dr_index) = address;

		result = mdb_inferior_set_registers (inferior, &arch->current_regs);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC);
			return result;
		}

		arch->dr_index [breakpoint->dr_index] = breakpoint->id;
	} else {
		result = mdb_inferior_read_memory (inferior, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		if (server->mono_runtime) {
			result = runtime_info_enable_breakpoint (server, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}

		return mdb_inferior_write_memory (inferior, address, 1, &bopcode);
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_arch_disable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	ArchInfo *arch = server->arch;
	InferiorHandle *inferior = server->inferior;
	gsize address;

	if (!breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (gsize) breakpoint->address;

	if (breakpoint->dr_index >= 0) {
		X86_DR_DISABLE (arch->current_regs, breakpoint->dr_index);
		INFERIOR_REG_DR_N (arch->current_regs, breakpoint->dr_index) = 0L;

		result = mdb_inferior_set_registers (inferior, &arch->current_regs);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC);
			return result;
		}

		arch->dr_index [breakpoint->dr_index] = 0;
	} else {
		result = mdb_inferior_write_memory (inferior, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		if (server->mono_runtime) {
			result = runtime_info_disable_breakpoint (server, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_insert_breakpoint (ServerHandle *server, guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (server->bpm, address);
	if (breakpoint) {
		breakpoint->refcount++;
		goto done;
	}

	breakpoint = g_new0 (BreakpointInfo, 1);

	breakpoint->refcount = 1;
	breakpoint->address = address;
	breakpoint->is_hardware_bpt = FALSE;
	breakpoint->id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->dr_index = -1;

	result = mdb_arch_enable_breakpoint (server, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_insert (server->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_remove_breakpoint (ServerHandle *server, guint32 idx)
{
	BreakpointManager *bpm;
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = mdb_arch_lookup_breakpoint (server, idx, &bpm);
	if (!breakpoint) {
		result = COMMAND_ERROR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	if (--breakpoint->refcount > 0) {
		result = COMMAND_ERROR_NONE;
		goto out;
	}

	result = mdb_arch_disable_breakpoint (server, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	breakpoint->enabled = FALSE;
	mono_debugger_breakpoint_manager_remove (bpm, breakpoint);

 out:
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

void
mdb_server_remove_hardware_breakpoints (ServerHandle *server)
{
	int i;

	for (i = 0; i < DR_NADDR; i++) {
		X86_DR_DISABLE (server->arch->current_regs, i);
		INFERIOR_REG_DR_N (server->arch->current_regs, i) = 0L;
		server->arch->dr_index [i] = 0;

		mdb_inferior_set_registers (server->inferior, &server->arch->current_regs);
	}
}

static ServerCommandError
find_free_hw_register (ServerHandle *server, guint32 *idx)
{
	int i;

	for (i = 0; i < DR_NADDR; i++) {
		if (!server->arch->dr_index [i]) {
			*idx = i;
			return COMMAND_ERROR_NONE;
		}
	}

	return COMMAND_ERROR_DR_OCCUPIED;
}

ServerCommandError
mdb_server_insert_hw_breakpoint (ServerHandle *server, guint32 type, guint32 *idx,
				 guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();

	result = find_free_hw_register (server, idx);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		return result;
	}

	breakpoint = g_new0 (BreakpointInfo, 1);
	breakpoint->type = (HardwareBreakpointType) type;
	breakpoint->address = address;
	breakpoint->refcount = 1;
	breakpoint->id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->is_hardware_bpt = TRUE;
	breakpoint->dr_index = *idx;

	result = mdb_arch_enable_breakpoint (server, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_insert (server->arch->hw_bpm, (BreakpointInfo *) breakpoint);

	*bhandle = breakpoint->id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mdb_server_enable_breakpoint (ServerHandle *server, guint32 idx)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = mdb_arch_lookup_breakpoint (server, idx, NULL);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = mdb_arch_enable_breakpoint (server, breakpoint);
	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

ServerCommandError
mdb_server_disable_breakpoint (ServerHandle *server, guint32 idx)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = mdb_arch_lookup_breakpoint (server, idx, NULL);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = mdb_arch_disable_breakpoint (server, breakpoint);
	breakpoint->enabled = FALSE;
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

ServerCommandError
mdb_server_get_breakpoints (ServerHandle *server, guint32 *count, guint32 **retval)
{
	guint32 i;
	GPtrArray *breakpoints;

	mono_debugger_breakpoint_manager_lock ();
	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (server->bpm);
	*count = breakpoints->len;
	*retval = g_new0 (guint32, breakpoints->len);

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		(*retval) [i] = info->id;
	}
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;	
}

static int
find_code_buffer_slot (MonoRuntimeInfo *runtime)
{
	guint32 i;

	for (i = runtime->executable_code_last_slot + 1; i < runtime->executable_code_total_chunks; i++) {
		if (runtime->executable_code_bitfield [i])
			continue;

		runtime->executable_code_bitfield [i] = 1;
		runtime->executable_code_last_slot = i;
		return i;
	}

	runtime->executable_code_last_slot = 0;
	for (i = 0; i < runtime->executable_code_total_chunks; i++) {
		if (runtime->executable_code_bitfield [i])
			continue;

		runtime->executable_code_bitfield [i] = 1;
		runtime->executable_code_last_slot = i;
		return i;
	}

	return -1;
}

ServerCommandError
mdb_server_execute_instruction (ServerHandle *server, const guint8 *instruction,
				guint32 size, gboolean update_ip)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	CodeBufferData *data;
	gsize code_address;
	int slot;

	runtime = server->mono_runtime;
	g_assert (runtime);

	if (!runtime->executable_code_buffer)
		return COMMAND_ERROR_INTERNAL_ERROR;

	slot = find_code_buffer_slot (runtime);
	if (slot < 0)
		return COMMAND_ERROR_INTERNAL_ERROR;

	if (size > runtime->executable_code_chunk_size)
		return COMMAND_ERROR_INTERNAL_ERROR;
	if (server->arch->code_buffer)
		return COMMAND_ERROR_INTERNAL_ERROR;

	code_address = (gsize) runtime->executable_code_buffer + slot * runtime->executable_code_chunk_size;

	data = g_new0 (CodeBufferData, 1);
	data->slot = slot;
	data->insn_size = size;
	data->update_ip = update_ip;
	data->original_rip = INFERIOR_REG_RIP (server->arch->current_regs);
	data->code_address = code_address;

	server->arch->code_buffer = data;

	result = mdb_inferior_write_memory (server->inferior, code_address, size, instruction);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if defined(__linux__)
	INFERIOR_REG_ORIG_RAX (server->arch->current_regs) = -1;
#endif
	INFERIOR_REG_RIP (server->arch->current_regs) = code_address;

	result = mdb_inferior_set_registers (server->inferior, &server->arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return mdb_server_step (server);
}
