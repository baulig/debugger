#include <mdb-inferior.h>
#include <mdb-exe-reader.h>
#include <mdb-arch.h>

ErrorCode
MdbInferior::GetFrame (StackFrame *out_frame)
{
	return arch->GetFrame (out_frame);
}

ErrorCode
MdbInferior::InsertBreakpoint (guint64 address, BreakpointInfo **out_breakpoint)
{
	ErrorCode result;

	*out_breakpoint = bpm->Lookup (address);
	if (*out_breakpoint) {
		(*out_breakpoint)->Ref ();
		return ERR_NONE;
	}

	*out_breakpoint = new BreakpointInfo (bpm, (gsize) address);

	result = arch->EnableBreakpoint (*out_breakpoint);
	if (result) {
		delete *out_breakpoint;
		*out_breakpoint = NULL;
		return result;
	}

	(*out_breakpoint)->enabled = true;
	bpm->Insert (*out_breakpoint);

	return ERR_NONE;
}

BreakpointInfo *
MdbInferior::LookupBreakpointById (guint32 idx)
{
	return arch->LookupBreakpoint (idx, NULL);
}

ErrorCode
MdbInferior::RemoveBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;

	if (breakpoint->Unref ()) {
		result = ERR_NONE;
		goto out;
	}

	result = arch->DisableBreakpoint (breakpoint);
	if (result)
		goto out;

	breakpoint->enabled = false;
	breakpoint->bpm->Remove (breakpoint);

 out:
	return result;
}

ErrorCode
MdbInferior::EnableBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;

	result = arch->EnableBreakpoint (breakpoint);
	if (!result)
		breakpoint->enabled = true;
	return result;
}

ErrorCode
MdbInferior::DisableBreakpoint (BreakpointInfo *breakpoint)
{
	ErrorCode result;

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
