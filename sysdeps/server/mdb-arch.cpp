#include <mdb-arch.h>

BreakpointInfo *
MdbArch::LookupBreakpoint (guint32 idx, BreakpointManager **out_bpm)
{
	BreakpointManager *bpm;
	BreakpointInfo *info;

	if (hw_bpm) {
		info = hw_bpm->LookupById (idx);
		if (info) {
			if (out_bpm)
				*out_bpm = hw_bpm;
			return info;
		}
	}

	bpm = inferior->GetBreakpointManager ();

	info = bpm->LookupById (idx);
	if (info) {
		if (out_bpm)
			*out_bpm = bpm;
		return info;
	}

	if (out_bpm)
		*out_bpm = NULL;
	return NULL;
}

gboolean
MdbArch::CheckBreakpoint (guint64 address, guint64 *retval)
{
	BreakpointManager *bpm = inferior->GetBreakpointManager ();
	BreakpointInfo *info;

	info = bpm->Lookup (address);
	if (!info || !info->enabled)
		return FALSE;

	*retval = info->id;
	return TRUE;
}
