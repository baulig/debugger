#include <arm-arch.h>

MdbArch *
mdb_arch_new (MdbInferior *inferior)
{
	return new ArmArch (inferior);
}

ErrorCode
ArmArch::EnableBreakpoint (BreakpointInfo *breakpoint)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
ArmArch::DisableBreakpoint (BreakpointInfo *breakpoint)
{
	return ERR_NOT_IMPLEMENTED;
}

ServerEvent *
ArmArch::ChildStopped (int stopsig, bool *out_remain_stopped)
{
	return NULL;
}

ErrorCode
ArmArch::GetFrame (StackFrame *out_frame)
{
	return ERR_NOT_IMPLEMENTED;
}

void
ArmArch::RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer)
{ }

int
ArmArch::GetRegisterCount (void)
{
	return 0;
}

ErrorCode
ArmArch::GetRegisterValues (guint64 *values)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
ArmArch::SetRegisterValues (const guint64 *values)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
ArmArch::CallMethod (InvocationData *invocation)
{
	return ERR_NOT_IMPLEMENTED;
}

ErrorCode
ArmArch::ExecuteInstruction (MdbInferior *inferior, gsize code_address, int insn_size,
			     bool update_ip, InferiorCallback *callback)
{
	return ERR_NOT_IMPLEMENTED;
}


