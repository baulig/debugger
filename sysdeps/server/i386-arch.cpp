#ifndef __X86_ARCH_CPP__
#error "This file must not be used directly."
#endif

#if defined(WINDOWS)

#define INFERIOR_REG_RBP(r)		r.context.Ebp
#define INFERIOR_REG_RBX(r)		r.context.Ebx
#define INFERIOR_REG_RAX(r)		r.context.Eax
#define INFERIOR_REG_RCX(r)		r.context.Ecx
#define INFERIOR_REG_RDX(r)		r.context.Edx
#define INFERIOR_REG_RSI(r)		r.context.Esi
#define INFERIOR_REG_RDI(r)		r.context.Edi
#define INFERIOR_REG_RIP(r)		r.context.Eip
#define INFERIOR_REG_CS(r)		r.context.SegCs
#define INFERIOR_REG_EFLAGS(r)		r.context.EFlags
#define INFERIOR_REG_RSP(r)		r.context.Esp
#define INFERIOR_REG_SS(r)		r.context.SegSs

#define INFERIOR_REG_DS(r)		r.context.SegDs
#define INFERIOR_REG_ES(r)		r.context.SegEs
#define INFERIOR_REG_FS(r)		r.context.SegFs
#define INFERIOR_REG_GS(r)		r.context.SegGs

#elif defined(__linux__) || defined(__FreeBSD__)

#define INFERIOR_REG_RBP(r)		r.regs.ebp
#define INFERIOR_REG_RBX(r)		r.regs.ebx
#define INFERIOR_REG_RAX(r)		r.regs.eax
#define INFERIOR_REG_RCX(r)		r.regs.ecx
#define INFERIOR_REG_RDX(r)		r.regs.edx
#define INFERIOR_REG_RSI(r)		r.regs.esi
#define INFERIOR_REG_RDI(r)		r.regs.edi
#define INFERIOR_REG_ORIG_RAX(r)	r.regs.orig_eax
#define INFERIOR_REG_RIP(r)		r.regs.eip
#define INFERIOR_REG_CS(r)		r.regs.xcs
#define INFERIOR_REG_EFLAGS(r)		r.regs.eflags
#define INFERIOR_REG_RSP(r)		r.regs.esp
#define INFERIOR_REG_SS(r)		r.regs.xss

#define INFERIOR_REG_FS_BASE(r)		r.regs.fs_base
#define INFERIOR_REG_GS_BASE(r)		r.regs.gs_base

#define INFERIOR_REG_DS(r)		r.regs.xds
#define INFERIOR_REG_ES(r)		r.regs.xes
#define INFERIOR_REG_FS(r)		r.regs.xfs
#define INFERIOR_REG_GS(r)		r.regs.xgs

#else
#error "Unknown operating systrem."
#endif

ErrorCode
X86Arch::GetRegisterValues (guint64 *values)
{
	ErrorCode result;

	result = GetRegisters ();
	if (result)
		return result;

	values [DEBUGGER_REG_RBX] = (gsize) INFERIOR_REG_RBX (current_regs);
	values [DEBUGGER_REG_RCX] = (gsize) INFERIOR_REG_RCX (current_regs);
	values [DEBUGGER_REG_RDX] = (gsize) INFERIOR_REG_RDX (current_regs);
	values [DEBUGGER_REG_RSI] = (gsize) INFERIOR_REG_RSI (current_regs);
	values [DEBUGGER_REG_RDI] = (gsize) INFERIOR_REG_RDI (current_regs);
	values [DEBUGGER_REG_RBP] = (gsize) INFERIOR_REG_RBP (current_regs);
	values [DEBUGGER_REG_RAX] = (gsize) INFERIOR_REG_RAX (current_regs);
	values [DEBUGGER_REG_DS] = (gsize) INFERIOR_REG_DS (current_regs);
	values [DEBUGGER_REG_ES] = (gsize) INFERIOR_REG_ES (current_regs);
	values [DEBUGGER_REG_FS] = (gsize) INFERIOR_REG_FS (current_regs);
	values [DEBUGGER_REG_GS] = (gsize) INFERIOR_REG_GS (current_regs);
	values [DEBUGGER_REG_RIP] = (gsize) INFERIOR_REG_RIP (current_regs);
	values [DEBUGGER_REG_CS] = (gsize) INFERIOR_REG_CS (current_regs);
	values [DEBUGGER_REG_EFLAGS] = (gsize) INFERIOR_REG_EFLAGS (current_regs);
	values [DEBUGGER_REG_RSP] = (gsize) INFERIOR_REG_RSP (current_regs);
	values [DEBUGGER_REG_SS] = (gsize) INFERIOR_REG_SS (current_regs);

	return ERR_NONE;
}

ErrorCode
X86Arch::SetRegisterValues (const guint64 *values)
{
	INFERIOR_REG_RBX (current_regs) = (gsize) values [DEBUGGER_REG_RBX];
	INFERIOR_REG_RCX (current_regs) = (gsize) values [DEBUGGER_REG_RCX];
	INFERIOR_REG_RDX (current_regs) = (gsize) values [DEBUGGER_REG_RDX];
	INFERIOR_REG_RSI (current_regs) = (gsize) values [DEBUGGER_REG_RSI];
	INFERIOR_REG_RDI (current_regs) = (gsize) values [DEBUGGER_REG_RDI];
	INFERIOR_REG_RBP (current_regs) = (gsize) values [DEBUGGER_REG_RBP];
	INFERIOR_REG_RAX (current_regs) = (gsize) values [DEBUGGER_REG_RAX];
	INFERIOR_REG_DS (current_regs) = (gsize) values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (current_regs) = (gsize) values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (current_regs) = (gsize) values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (current_regs) = (gsize) values [DEBUGGER_REG_GS];
	INFERIOR_REG_RIP (current_regs) = (gsize) values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (current_regs) = (gsize) values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (current_regs) = (gsize) values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_RSP (current_regs) = (gsize) values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (current_regs) = (gsize) values [DEBUGGER_REG_SS];

	return SetRegisters ();
}

bool
X86Arch::Marshal_Generic (InvocationData *invocation, CallbackData *cdata)
{
	MonoRuntime *runtime = inferior->GetProcess ()->GetMonoRuntime ();
	GenericInvocationData data;
	gconstpointer data_ptr = NULL;
	int data_size = 0;
	gsize new_rsp;

	memset (&data, 0, sizeof (data));
	data.invocation_type = invocation->type;
	data.callback_id = (guint32) invocation->callback_id;
	data.method_address = invocation->method_address;
	data.arg1 = invocation->arg1;
	data.arg2 = invocation->arg2;
	data.arg3 = invocation->arg3;

	if (invocation->type == 	INVOCATION_TYPE_LONG_LONG_LONG_STRING) {
		data_ptr = invocation->string_arg;
		data_size = strlen (invocation->string_arg) + 1;
	}

	new_rsp = INFERIOR_REG_RSP (current_regs) - sizeof (data) - data_size - 3 * sizeof (gsize) - AMD64_RED_ZONE_SIZE;

	if (data_ptr) {
		data.data_size = data_size;
		data.data_arg_ptr = new_rsp + 3 * sizeof (gsize) + sizeof (data);

		if (inferior->WriteMemory (data.data_arg_ptr, data_size, data_ptr))
			return false;
	}

	cdata->call_address = new_rsp + 2 * sizeof (gsize);
	cdata->stack_pointer = new_rsp;

	cdata->callback = invocation->callback;

	if (inferior->PokeWord (new_rsp, cdata->call_address))
		return false;
	if (inferior->PokeWord (new_rsp + sizeof (gsize), new_rsp + 3 * sizeof (gsize)))
		return false;
	if (inferior->PokeWord (new_rsp + 2 * sizeof (gsize), 0x000000CC))
		return false;
	if (inferior->WriteMemory (new_rsp + 3 * sizeof (gsize), sizeof (data), &data))
		return false;

	INFERIOR_REG_RSP (current_regs) = new_rsp;
	INFERIOR_REG_RIP (current_regs) = runtime->GetGenericInvocationFunc ();

	g_message (G_STRLOC ": %Lx - %Lx", INFERIOR_REG_RIP (current_regs), new_rsp);

	return true;
}
