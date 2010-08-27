#include <breakpoints.h>
#include <debugger-mutex.h>
#include <sys/stat.h>
#include <signal.h>
#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif
#include <string.h>
#include <fcntl.h>
#include <errno.h>

/*
 * There is only one thread in the server, so we don't need any mutex there.
 */
#ifndef MDB_SERVER
static DebuggerMutex *bpm_mutex;
#endif

int BreakpointInfo::next_id = 0;

void
BreakpointManager::Initialize (void)
{
#ifndef MDB_SERVER
	bpm_mutex = debugger_mutex_new ();
#endif
}

BreakpointManager::BreakpointManager (void)
{
	breakpoints = g_ptr_array_new ();
	breakpoint_hash = g_hash_table_new (NULL, NULL);
	breakpoint_by_addr = g_hash_table_new (NULL, NULL);
}

BreakpointManager *
BreakpointManager::Clone (void)
{
	BreakpointManager *new_bpm = new BreakpointManager ();
	guint32 i;

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *old_info = (BreakpointInfo *) g_ptr_array_index (breakpoints, i);
		BreakpointInfo *info = (BreakpointInfo *) g_memdup (old_info, sizeof (BreakpointInfo));

		new_bpm->Insert (info);
	}

	return new_bpm;
}

BreakpointManager::~BreakpointManager (void)
{
	g_ptr_array_free (breakpoints, TRUE);
	g_hash_table_destroy (breakpoint_hash);
	g_hash_table_destroy (breakpoint_by_addr);
}

void
BreakpointManager::Lock (void)
{
#ifndef MDB_SERVER
	debugger_mutex_lock (bpm_mutex);
#endif
}

void
BreakpointManager::Unlock (void)
{
#ifndef MDB_SERVER
	debugger_mutex_unlock (bpm_mutex);
#endif
}

void
BreakpointManager::Insert (BreakpointInfo *breakpoint)
{
	g_ptr_array_add (breakpoints, breakpoint);
	g_hash_table_insert (breakpoint_hash, GINT_TO_POINTER ((gsize) breakpoint->id), breakpoint);
	g_hash_table_insert (breakpoint_by_addr, GINT_TO_POINTER ((gsize) breakpoint->address), breakpoint);
}

BreakpointInfo *
BreakpointManager::Lookup (guint64 address)
{
	return (BreakpointInfo *) g_hash_table_lookup (breakpoint_by_addr, GINT_TO_POINTER ((gsize) address));
}

BreakpointInfo *
BreakpointManager::LookupById (guint32 id)
{
	return (BreakpointInfo *) g_hash_table_lookup (breakpoint_hash, GINT_TO_POINTER ((gsize) id));
}

GPtrArray *
BreakpointManager::GetBreakpoints (void)
{
	return breakpoints;
}

void
BreakpointManager::Remove (BreakpointInfo *breakpoint)
{
	if (!LookupById (breakpoint->id)) {
		g_warning (G_STRLOC ": mono_debugger_breakpoint_manager_remove(): No such breakpoint %d", breakpoint->id);
		return;
	}

	if (--breakpoint->refcount > 0)
		return;

	g_hash_table_remove (breakpoint_hash, GINT_TO_POINTER ((gsize)breakpoint->id));
	g_hash_table_remove (breakpoint_by_addr, GINT_TO_POINTER ((gsize)breakpoint->address));
	g_ptr_array_remove_fast (breakpoints, breakpoint);
	delete breakpoint;
}
