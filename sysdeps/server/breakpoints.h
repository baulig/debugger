#ifndef __MONO_DEBUGGER_BREAKPOINTS_H__
#define __MONO_DEBUGGER_BREAKPOINTS_H__

#include <glib.h>

G_BEGIN_DECLS

typedef struct {
	GPtrArray *breakpoints;
	GHashTable *breakpoint_hash;
	GHashTable *breakpoint_by_addr;
} BreakpointManager;

typedef enum {
	HARDWARE_BREAKPOINT_NONE = 0,
	HARDWARE_BREAKPOINT_EXECUTE,
	HARDWARE_BREAKPOINT_READ,
	HARDWARE_BREAKPOINT_WRITE
} HardwareBreakpointType;

typedef struct {
	HardwareBreakpointType type;
	int id;
	int refcount;
	int enabled;
	int is_hardware_bpt;
	int dr_index;
	char saved_insn;
	int runtime_table_slot;
	guint64 address;
} BreakpointInfo;

void
mono_debugger_breakpoint_manager_init                (void);

BreakpointManager *
mono_debugger_breakpoint_manager_new                 (void);

BreakpointManager *
mono_debugger_breakpoint_manager_clone               (BreakpointManager *old);

void
mono_debugger_breakpoint_manager_free                (BreakpointManager *bpm);

void
mono_debugger_breakpoint_manager_lock                (void);

void
mono_debugger_breakpoint_manager_unlock              (void);

int
mono_debugger_breakpoint_manager_get_next_id         (void);

void
mono_debugger_breakpoint_manager_insert              (BreakpointManager *bpm, BreakpointInfo *breakpoint);

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup              (BreakpointManager *bpm, guint64 address);

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup_by_id        (BreakpointManager *bpm, guint32 id);

GPtrArray *
mono_debugger_breakpoint_manager_get_breakpoints     (BreakpointManager *bpm);

void
mono_debugger_breakpoint_manager_remove              (BreakpointManager *bpm, BreakpointInfo *breakpoint);

int
mono_debugger_breakpoint_info_get_id                 (BreakpointInfo *info);

gboolean
mono_debugger_breakpoint_info_get_is_enabled         (BreakpointInfo *info);

G_END_DECLS

#endif
