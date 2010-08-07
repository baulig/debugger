#ifndef __MONO_DEBUGGER_X86_86_ARCH_H__
#define __MONO_DEBUGGER_X86_86_ARCH_H__

#if !defined(__x86_64__)
#error "Wrong architecture"
#endif

#include <glib.h>

G_BEGIN_DECLS

#include <sys/user.h>

/* Debug registers' indices.  */
#define DR_NADDR		4  /* the number of debug address registers */
#define DR_STATUS		6  /* index of debug status register (DR6) */
#define DR_CONTROL		7  /* index of debug control register (DR7) */

struct _InferiorRegsType {
	struct user_regs_struct regs;
	struct user_fpregs_struct fpregs;
	guint64 dr_control, dr_status;
	guint64 dr_regs [DR_NADDR];
};

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

#define INFERIOR_REG_DR_CONTROL(r)	r.dr_control
#define INFERIOR_REG_DR_STATUS(r)	r.dr_control
#define INFERIOR_REG_DR_N(r,n)		r.dr_regs[n]

#define AMD64_RED_ZONE_SIZE 128

G_END_DECLS

#endif


