#ifndef __MONO_DEBUGGER_BREAKPOINTS_H__
#define __MONO_DEBUGGER_BREAKPOINTS_H__

#include <glib.h>

typedef enum {
	HARDWARE_BREAKPOINT_NONE = 0,
	HARDWARE_BREAKPOINT_EXECUTE,
	HARDWARE_BREAKPOINT_READ,
	HARDWARE_BREAKPOINT_WRITE
} HardwareBreakpointType;

class BreakpointManager;

class BreakpointInfo
{
public:
	BreakpointInfo (BreakpointManager *bpm, gsize address)
	{
		this->bpm = bpm;
		this->refcount = 1;
		this->address = address;
		this->is_hardware_bpt = false;
		this->id = ++next_id;
		this->dr_index = -1;
		this->type = HARDWARE_BREAKPOINT_NONE;
		this->runtime_table_slot = -1;
		this->enabled = false;
	}

	BreakpointInfo (BreakpointManager *bpm, gsize address, int dr_idx)
	{
		this->bpm = bpm;
		this->refcount = 1;
		this->address = address;
		this->is_hardware_bpt = true;
		this->id = ++next_id;
		this->dr_index = dr_idx;
		this->type = HARDWARE_BREAKPOINT_NONE;
		this->runtime_table_slot = -1;
		this->enabled = false;
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
	BreakpointManager *bpm;
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
