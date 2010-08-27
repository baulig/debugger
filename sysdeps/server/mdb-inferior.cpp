#include <mdb-inferior.h>
#include <mdb-exe-reader.h>
#include <mdb-arch.h>

ErrorCode
MdbInferior::GetFrame (StackFrame *out_frame)
{
	return arch->GetFrame (out_frame);
}

ErrorCode
MdbInferior::InsertBreakpoint (guint64 address, guint32 *out_idx)
{
	BreakpointInfo *breakpoint;
	ErrorCode result;

	breakpoint = bpm->Lookup (address);
	if (breakpoint) {
		breakpoint->Ref ();
		goto done;
	}

	breakpoint = new BreakpointInfo ((gsize) address);

	result = arch->EnableBreakpoint (breakpoint);
	if (result) {
		delete breakpoint;
		return result;
	}

	breakpoint->enabled = true;
	bpm->Insert (breakpoint);

 done:
	*out_idx = breakpoint->id;
	return ERR_NONE;
}

ErrorCode
MdbInferior::RemoveBreakpoint (guint32 idx)
{
	BreakpointManager *bpm;
	BreakpointInfo *breakpoint;
	ErrorCode result;

	breakpoint = arch->LookupBreakpoint (idx, &bpm);
	if (!breakpoint) {
		result = ERR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	if (breakpoint->Unref ()) {
		result = ERR_NONE;
		goto out;
	}

	result = arch->DisableBreakpoint (breakpoint);
	if (result)
		goto out;

	breakpoint->enabled = false;
	bpm->Remove (breakpoint);

 out:
	return result;
}

ErrorCode
MdbInferior::EnableBreakpoint (guint32 idx)
{
	BreakpointInfo *breakpoint;
	ErrorCode result;

	breakpoint = arch->LookupBreakpoint (idx, NULL);
	if (!breakpoint)
		return ERR_NO_SUCH_BREAKPOINT;

	result = arch->EnableBreakpoint (breakpoint);
	if (!result)
		breakpoint->enabled = true;
	return result;
}

ErrorCode
MdbInferior::DisableBreakpoint (guint32 idx)
{
	BreakpointInfo *breakpoint;
	ErrorCode result;

	breakpoint = arch->LookupBreakpoint (idx, NULL);
	if (!breakpoint)
		return ERR_NO_SUCH_BREAKPOINT;

	result = arch->DisableBreakpoint (breakpoint);
	if (!result)
		breakpoint->enabled = false;
	return result;
}


gchar *
MdbInferior::DisassembleInstruction (guint64 address, guint32 *out_insn_size)
{
	if (!disassembler)
		disassembler = server->GetDisassembler (this);

	return disassembler->DisassembleInstruction (address, out_insn_size);
}
