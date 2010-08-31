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

	bpm = inferior->GetServer ()->GetBreakpointManager ();

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
