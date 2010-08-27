#ifndef __MONO_DEBUGGER_BREAKPOINTS_H__
#define __MONO_DEBUGGER_BREAKPOINTS_H__

#include <glib.h>

typedef enum {
	HARDWARE_BREAKPOINT_NONE = 0,
	HARDWARE_BREAKPOINT_EXECUTE,
	HARDWARE_BREAKPOINT_READ,
	HARDWARE_BREAKPOINT_WRITE
} HardwareBreakpointType;

class BreakpointInfo
{
public:
	BreakpointInfo (gsize address)
	{
		refcount = 1;
		address = address;
		is_hardware_bpt = FALSE;
		id = ++next_id;
		dr_index = -1;
	}

	BreakpointInfo (gsize address, int dr_idx)
	{
		refcount = 1;
		address = address;
		is_hardware_bpt = TRUE;
		id = ++next_id;
		dr_index = dr_idx;
	}

	void Ref ()
	{
		++refcount;
	}

	bool Unref ()
	{
		return --refcount > 0;
	}

public:
	HardwareBreakpointType type;
	int id;
	bool enabled;
	bool is_hardware_bpt;
	int dr_index;
	char saved_insn;
	int runtime_table_slot;
	gsize address;

private:
	int refcount;
	static int next_id;

	friend class BreakpointManager;
	friend class MdbArch;
};

class BreakpointManager
{
public:
	BreakpointManager (void);
	~BreakpointManager (void);

	BreakpointManager *Clone (void);

	static void Initialize (void);

	static void Lock (void);
	static void Unlock (void);

	void Insert (BreakpointInfo *breakpoint);

	BreakpointInfo *Lookup (guint64 address);

	BreakpointInfo *LookupById (guint32 id);

	GPtrArray *GetBreakpoints (void);

	void Remove (BreakpointInfo *breakpoint);

	BreakpointInfo *CreateBreakpoint (guint64 address);

	BreakpointInfo *CreateBreakpoint (guint64 address, int dr_idx);

private:
	GPtrArray *breakpoints;
	GHashTable *breakpoint_hash;
	GHashTable *breakpoint_by_addr;
};

#endif
