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
