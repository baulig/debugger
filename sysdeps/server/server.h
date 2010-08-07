#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <config.h>
#include <library.h>
#include <glib.h>

G_BEGIN_DECLS

typedef enum {
	STOP_ACTION_STOPPED,
	STOP_ACTION_INTERRUPTED,
	STOP_ACTION_BREAKPOINT_HIT,
	STOP_ACTION_CALLBACK,
	STOP_ACTION_CALLBACK_COMPLETED,
	STOP_ACTION_NOTIFICATION,
	STOP_ACTION_RTI_DONE,
	STOP_ACTION_INTERNAL_ERROR
} ChildStoppedAction;

typedef struct _ArchInfo ArchInfo;
typedef struct _InferiorRegsType INFERIOR_REGS_TYPE;

struct _ServerHandle {
	ArchInfo *arch;
	InferiorHandle *inferior;
	MonoRuntimeInfo *mono_runtime;
	BreakpointManager *bpm;
};

/*
 * Public Methods
 */

ServerType
mdb_server_get_server_type (void);

ServerCapabilities
mdb_server_get_capabilities (void);

extern void
mdb_server_remove_hardware_breakpoints (ServerHandle *server);

extern void
mdb_server_remove_breakpoints_from_target_memory (ServerHandle *server, guint64 start,
						  guint32 size, gpointer buffer);

extern ServerCommandError
mdb_server_read_memory (ServerHandle *server, guint64 start, guint32 size, gpointer buffer);

extern ServerCommandError
mdb_server_peek_word (ServerHandle *server, guint64 start, guint64 *retval);

extern ServerCommandError
mdb_server_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer buffer);

extern ServerCommandError
mdb_server_step (ServerHandle *server);

extern ServerCommandError
mdb_server_continue (ServerHandle *server);

extern ServerCommandError
mdb_server_current_insn_is_bpt (ServerHandle *server, guint32 *is_breakpoint);

extern ServerCommandError
mdb_server_get_frame (ServerHandle *server, StackFrame *frame);

extern ServerCommandError
mdb_server_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			    guint32 *target_address_size, guint32 *is_bigendian);

extern ServerCommandError
mdb_server_push_registers (ServerHandle *server, guint64 *new_esp);

extern ServerCommandError
mdb_server_pop_registers (ServerHandle *server);

extern ServerCommandError
mdb_server_insert_breakpoint (ServerHandle *server, guint64 address, guint32 *bhandle);

extern ServerCommandError
mdb_server_remove_breakpoint (ServerHandle *server, guint32 idx);

extern ServerCommandError
mdb_server_insert_hw_breakpoint (ServerHandle *server, guint32 type, guint32 *idx,
				 guint64 address, guint32 *bhandle);

extern ServerCommandError
mdb_server_enable_breakpoint (ServerHandle *server, guint32 idx);

extern ServerCommandError
mdb_server_disable_breakpoint (ServerHandle *server, guint32 idx);

extern ServerCommandError
mdb_server_get_breakpoints (ServerHandle *server, guint32 *count, guint32 **retval);

extern ServerCommandError
mdb_server_execute_instruction (ServerHandle *server, const guint8 *instruction,
				guint32 size, gboolean update_ip);

extern ServerCommandError
mdb_server_call_method (ServerHandle *handle, guint64 method_address,
			guint64 method_argument1, guint64 method_argument2,
			guint64 callback_argument);

extern ServerCommandError
mdb_server_call_method_1 (ServerHandle *handle, guint64 method_address,
			  guint64 method_argument, guint64 data_argument,
			  guint64 data_argument2, const gchar *string_argument,
			  guint64 callback_argument);

extern ServerCommandError
mdb_server_call_method_2 (ServerHandle *handle, guint64 method_address,
			  guint32 data_size, gconstpointer data_buffer,
			  guint64 callback_argument);

extern ServerCommandError
mdb_server_call_method_3 (ServerHandle *handle, guint64 method_address,
			  guint64 method_argument, guint64 address_argument,
			  guint32 blob_size, gconstpointer blob_data,
			  guint64 callback_argument);

extern ServerCommandError
mdb_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
			       guint64 method_argument, guint32 num_params,
			       guint32 blob_size, guint64 *param_data,
			       gint32 *offset_data, gconstpointer blob_data,
			       guint64 callback_argument, gboolean debug);

extern ChildStoppedAction
mdb_arch_child_stopped (ServerHandle *handle, int stopsig,
			guint64 *callback_arg, guint64 *retval, guint64 *retval2,
			guint32 *opt_data_size, gpointer *opt_data);

extern ServerCommandError
mdb_server_get_registers (ServerHandle *handle, guint64 *values);

extern ServerCommandError
mdb_server_set_registers (ServerHandle *handle, guint64 *values);

extern ServerCommandError
mdb_server_mark_rti_frame (ServerHandle *handle);

extern ServerCommandError
mdb_server_abort_invoke (ServerHandle *handle, guint64 rti_id);

extern ServerCommandError
mdb_server_get_callback_frame (ServerHandle *handle, guint64 stack_pointer,
			       gboolean exact_match, CallbackInfo *info);

extern void
mdb_server_get_registers_from_core_file (guint64 *values, const guint8 *buffer);

extern ServerCommandError
mdb_server_restart_notification (ServerHandle *handle);

extern ServerCommandError
mdb_server_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out);

extern ServerCommandError
mdb_server_get_threads (ServerHandle *handle, guint32 *count, guint32 **threads);

extern ServerCommandError
mdb_server_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			    guint32 *nargs, gchar ***cmdline_args);

extern ServerCommandError
mdb_server_detach_after_fork (ServerHandle *handle);

extern ServerCommandError
mdb_server_count_registers (ServerHandle *server, guint32 *out_count);

/*
 * Arch-specific methods
 */

extern ArchInfo *
mdb_arch_initialize (void);

extern void
mdb_arch_finalize (ArchInfo *arch);

extern ServerCommandError
mdb_arch_get_registers (ServerHandle *server);

extern ChildStoppedAction
mdb_arch_child_stopped (ServerHandle *server, int stopsig,
			guint64 *callback_arg, guint64 *retval, guint64 *retval2,
			guint32 *opt_data_size, gpointer *opt_data);

extern ServerCommandError
mdb_arch_disable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint);

extern ServerCommandError
mdb_arch_enable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint);

extern ServerCommandError
mdb_arch_enable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint);

extern ServerCommandError
mdb_arch_disable_breakpoint (ServerHandle *server, BreakpointInfo *breakpoint);

/*
 * Inferior
 */

extern ServerCommandError
mdb_inferior_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

extern ServerCommandError
mdb_inferior_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

extern ServerCommandError
mdb_inferior_read_memory (InferiorHandle *inferior, guint64 start, guint32 size, gpointer buffer);

extern ServerCommandError
mdb_inferior_write_memory (InferiorHandle *inferior, guint64 start, guint32 size, gconstpointer buffer);

extern ServerCommandError
mdb_inferior_peek_word (InferiorHandle *inferior, guint64 start, guint64 *retval);

extern ServerCommandError
mdb_inferior_poke_word (InferiorHandle *inferior, guint64 addr, gsize value);

extern ServerCommandError
mdb_inferior_make_memory_executable (InferiorHandle *inferior, guint64 start, guint32 size);

/*
 * Private stuff
 */

extern void
_mdb_inferior_set_last_signal (InferiorHandle *inferior, int last_signal);

extern int
_mdb_inferior_get_last_signal (InferiorHandle *inferior);

G_END_DECLS

#endif
