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

gchar *
MdbInferior::ReadString (guint64 address)
{
	char buffer [BUFSIZ + 1];
	int pos = 0;

	while (pos < BUFSIZ) {
		gsize word;

		if (PeekWord (address + pos, &word))
			return NULL;

		buffer [pos++] = (char) word;
		if ((word & 0x00FF) == 0)
			break;
	}
	buffer [pos] = (char) 0;
	return g_strdup (buffer);
}

ErrorCode
MdbInferior::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	ErrorCode result = ERR_NONE;

	switch (command) {
	case CMD_INFERIOR_GET_SIGNAL_INFO: {
		SignalInfo *sinfo;

		result = GetSignalInfo (&sinfo);
		if (result)
			return result;

		out->AddInt (sinfo->sigkill);
		out->AddInt (sinfo->sigstop);
		out->AddInt (sinfo->sigint);
		out->AddInt (sinfo->sigchld);
		out->AddInt (sinfo->sigfpe);
		out->AddInt (sinfo->sigquit);
		out->AddInt (sinfo->sigabrt);
		out->AddInt (sinfo->sigsegv);
		out->AddInt (sinfo->sigill);
		out->AddInt (sinfo->sigbus);
		out->AddInt (sinfo->sigwinch);
		out->AddInt (sinfo->kernel_sigrtmin);

		g_free (sinfo);
		break;
	}

	case CMD_INFERIOR_GET_APPLICATION: {
		gchar *exe_file = NULL, *cwd = NULL, **cmdline_args = NULL;
		guint32 nargs, i;

		result = GetApplication (&exe_file, &cwd, &nargs, &cmdline_args);
		if (result)
			return result;

		out->AddString (exe_file);
		out->AddString (cwd);

		out->AddInt (nargs);
		for (i = 0; i < nargs; i++)
			out->AddString (cmdline_args [i]);

		g_free (exe_file);
		g_free (cwd);
		g_free (cmdline_args);
		break;
	}

	case CMD_INFERIOR_GET_FRAME: {
		StackFrame frame;

		result = GetFrame (&frame);
		if (result)
			return result;

		out->AddLong (frame.address);
		out->AddLong (frame.stack_pointer);
		out->AddLong (frame.frame_address);
		break;
	}

	case CMD_INFERIOR_STEP:
		result = Step ();
		break;

	case CMD_INFERIOR_CONTINUE:
		result = Continue ();
		break;

	case CMD_INFERIOR_RESUME:
		result = Resume ();
		break;

	case CMD_INFERIOR_INSERT_BREAKPOINT: {
		guint64 address;
		BreakpointInfo *breakpoint;

		address = in->DecodeLong ();

		result = InsertBreakpoint (address, &breakpoint);
		if (result == ERR_NONE)
			out->AddInt (breakpoint->id);
		break;
	}

	case CMD_INFERIOR_ENABLE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = in->DecodeInt ();
		breakpoint = LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = EnableBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_DISABLE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = in->DecodeInt ();
		breakpoint = LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = DisableBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_REMOVE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = in->DecodeInt ();
		breakpoint = LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = RemoveBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_GET_REGISTERS: {
		guint32 count, i;
		guint64 *regs;

		result = GetRegisterCount (&count);
		if (result)
			return result;

		regs = g_new0 (guint64, count);

		result = GetRegisters (regs);
		if (result) {
			g_free (regs);
			return result;
		}

		out->AddInt (count);
		for (i = 0; i < count; i++)
			out->AddLong (regs [i]);

		g_free (regs);
		break;
	}

	case CMD_INFERIOR_READ_MEMORY: {
		guint64 address;
		guint32 size;
		guint8 *data;

		address = in->DecodeLong ();
		size = in->DecodeInt ();

		data = (guint8 *) g_malloc (size);

		result = ReadMemory (address, size, data);
		if (!result)
			out->AddData (data, size);

		g_free (data);
		break;
	}

	case CMD_INFERIOR_WRITE_MEMORY: {
		guint64 address;
		guint32 size;

		address = in->DecodeLong ();
		size = in->DecodeInt ();

		result = WriteMemory (address, size, in->GetData ());
		break;
	}

	case CMD_INFERIOR_GET_PENDING_SIGNAL: {
		guint32 sig;

		result = GetPendingSignal (&sig);
		if (result == ERR_NONE)
			out->AddInt (sig);

		break;
	}

	case CMD_INFERIOR_SET_SIGNAL: {
		guint32 sig, send_it;

		sig = in->DecodeInt ();
		send_it = in->DecodeByte ();

		result = SetSignal (sig, send_it);
		break;
	}

	case CMD_INFERIOR_DISASSEMBLE_INSN: {
		guint64 address;
		guint32 insn_size;
		gchar *insn;

		address = in->DecodeLong ();

		insn = DisassembleInstruction (address, &insn_size);
		out->AddInt (insn_size);
		out->AddString (insn);
		g_free (insn);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return result;
}

