#ifndef __X86_ARCH_CPP__
#error "This file must not be used directly."
#endif

#include <x86-arch.h>

#if defined(WINDOWS)
#error "64-bit Windows is not supported yet."
#elif defined(__linux__) || defined(__FreeBSD__)

#define INFERIOR_REG_R15(r)		r.regs.r15
#define INFERIOR_REG_R14(r)		r.regs.r14
#define INFERIOR_REG_R13(r)		r.regs.r13
#define INFERIOR_REG_R12(r)		r.regs.r12
#define INFERIOR_REG_RBP(r)		r.regs.rbp
#define INFERIOR_REG_RBX(r)		r.regs.rbx
#define INFERIOR_REG_R11(r)		r.regs.r11
#define INFERIOR_REG_R10(r)		r.regs.r10
#define INFERIOR_REG_R9(r)		r.regs.r9
#define INFERIOR_REG_R8(r)		r.regs.r8
#define INFERIOR_REG_RAX(r)		r.regs.rax
#define INFERIOR_REG_RCX(r)		r.regs.rcx
#define INFERIOR_REG_RDX(r)		r.regs.rdx
#define INFERIOR_REG_RSI(r)		r.regs.rsi
#define INFERIOR_REG_RDI(r)		r.regs.rdi
#define INFERIOR_REG_ORIG_RAX(r)	r.regs.orig_rax
#define INFERIOR_REG_RIP(r)		r.regs.rip
#define INFERIOR_REG_CS(r)		r.regs.cs
#define INFERIOR_REG_EFLAGS(r)		r.regs.eflags
#define INFERIOR_REG_RSP(r)		r.regs.rsp
#define INFERIOR_REG_SS(r)		r.regs.ss

#define INFERIOR_REG_FS_BASE(r)		r.regs.fs_base
#define INFERIOR_REG_GS_BASE(r)		r.regs.gs_base

#define INFERIOR_REG_DS(r)		r.regs.ds
#define INFERIOR_REG_ES(r)		r.regs.es
#define INFERIOR_REG_FS(r)		r.regs.fs
#define INFERIOR_REG_GS(r)		r.regs.gs

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

	values [DEBUGGER_REG_R15] = INFERIOR_REG_R15 (current_regs);
	values [DEBUGGER_REG_R14] = INFERIOR_REG_R14 (current_regs);
	values [DEBUGGER_REG_R13] = INFERIOR_REG_R13 (current_regs);
	values [DEBUGGER_REG_R12] = INFERIOR_REG_R12 (current_regs);
	values [DEBUGGER_REG_RBP] = INFERIOR_REG_RBP (current_regs);
	values [DEBUGGER_REG_RBX] = INFERIOR_REG_RBX (current_regs);
	values [DEBUGGER_REG_R11] = INFERIOR_REG_R11 (current_regs);
	values [DEBUGGER_REG_R10] = INFERIOR_REG_R10 (current_regs);
	values [DEBUGGER_REG_R9] = INFERIOR_REG_R9 (current_regs);
	values [DEBUGGER_REG_R8] = INFERIOR_REG_R8 (current_regs);
	values [DEBUGGER_REG_RAX] = INFERIOR_REG_RAX (current_regs);
	values [DEBUGGER_REG_RCX] = INFERIOR_REG_RCX (current_regs);
	values [DEBUGGER_REG_RDX] = INFERIOR_REG_RDX (current_regs);
	values [DEBUGGER_REG_RSI] = INFERIOR_REG_RSI (current_regs);
	values [DEBUGGER_REG_RDI] = INFERIOR_REG_RDI (current_regs);
	values [DEBUGGER_REG_ORIG_RAX] = INFERIOR_REG_ORIG_RAX (current_regs);
	values [DEBUGGER_REG_RIP] = INFERIOR_REG_RIP (current_regs);
	values [DEBUGGER_REG_CS] = INFERIOR_REG_CS (current_regs);
	values [DEBUGGER_REG_EFLAGS] = INFERIOR_REG_EFLAGS (current_regs);
	values [DEBUGGER_REG_RSP] = INFERIOR_REG_RSP (current_regs);
	values [DEBUGGER_REG_SS] = INFERIOR_REG_SS (current_regs);
	values [DEBUGGER_REG_FS_BASE] = INFERIOR_REG_FS_BASE (current_regs);
	values [DEBUGGER_REG_GS_BASE] = INFERIOR_REG_GS_BASE (current_regs);
	values [DEBUGGER_REG_DS] = INFERIOR_REG_DS (current_regs);
	values [DEBUGGER_REG_ES] = INFERIOR_REG_ES (current_regs);
	values [DEBUGGER_REG_FS] = INFERIOR_REG_FS (current_regs);
	values [DEBUGGER_REG_GS] = INFERIOR_REG_GS (current_regs);

	return ERR_NONE;
}

ErrorCode
X86Arch::SetRegisterValues (const guint64 *values)
{
	INFERIOR_REG_R15 (current_regs) = values [DEBUGGER_REG_R15];
	INFERIOR_REG_R14 (current_regs) = values [DEBUGGER_REG_R14];
	INFERIOR_REG_R13 (current_regs) = values [DEBUGGER_REG_R13];
	INFERIOR_REG_R12 (current_regs) = values [DEBUGGER_REG_R12];
	INFERIOR_REG_RBP (current_regs) = values [DEBUGGER_REG_RBP];
	INFERIOR_REG_RBX (current_regs) = values [DEBUGGER_REG_RBX];
	INFERIOR_REG_R11 (current_regs) = values [DEBUGGER_REG_R11];
	INFERIOR_REG_R10 (current_regs) = values [DEBUGGER_REG_R10];
	INFERIOR_REG_R9 (current_regs) = values [DEBUGGER_REG_R9];
	INFERIOR_REG_R8 (current_regs) = values [DEBUGGER_REG_R8];
	INFERIOR_REG_RAX (current_regs) = values [DEBUGGER_REG_RAX];
	INFERIOR_REG_RCX (current_regs) = values [DEBUGGER_REG_RCX];
	INFERIOR_REG_RDX (current_regs) = values [DEBUGGER_REG_RDX];
	INFERIOR_REG_RSI (current_regs) = values [DEBUGGER_REG_RSI];
	INFERIOR_REG_RDI (current_regs) = values [DEBUGGER_REG_RDI];
	INFERIOR_REG_ORIG_RAX (current_regs) = values [DEBUGGER_REG_ORIG_RAX];
	INFERIOR_REG_RIP (current_regs) = values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (current_regs) = values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (current_regs) = values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_RSP (current_regs) = values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (current_regs) = values [DEBUGGER_REG_SS];
	INFERIOR_REG_FS_BASE (current_regs) = values [DEBUGGER_REG_FS_BASE];
	INFERIOR_REG_GS_BASE (current_regs) = values [DEBUGGER_REG_GS_BASE];
	INFERIOR_REG_DS (current_regs) = values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (current_regs) = values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (current_regs) = values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (current_regs) = values [DEBUGGER_REG_GS];

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

	if (invocation->type == INVOCATION_TYPE_LONG_LONG_LONG_STRING) {
		data_ptr = invocation->string_arg;
		data_size = strlen (invocation->string_arg) + 1;
	}

	new_rsp = INFERIOR_REG_RSP (current_regs) - sizeof (data) - data_size - 2 * sizeof (gsize) - AMD64_RED_ZONE_SIZE;

	if (data_ptr) {
		data.data_size = data_size;
		data.data_arg_ptr = new_rsp + 2 * sizeof (gsize) + sizeof (data);

		if (inferior->WriteMemory (data.data_arg_ptr, data_size, data_ptr))
			return false;
	}

	cdata->call_address = new_rsp + sizeof (gsize);
	cdata->stack_pointer = new_rsp;

	cdata->callback = invocation->callback;

	if (inferior->PokeWord (new_rsp, cdata->call_address))
		return false;
	if (inferior->PokeWord (new_rsp + sizeof (gsize), 0x000000CC))
		return false;
	if (inferior->WriteMemory (new_rsp + 2 * sizeof (gsize), sizeof (data), &data))
		return false;

	INFERIOR_REG_RSP (current_regs) = new_rsp;
	INFERIOR_REG_RDI (current_regs) = new_rsp + 2 * sizeof (gsize);
	INFERIOR_REG_RIP (current_regs) = runtime->GetGenericInvocationFunc ();

	return true;
}
