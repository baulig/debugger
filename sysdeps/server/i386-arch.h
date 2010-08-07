#ifndef __MONO_DEBUGGER_I386_ARCH_H__
#define __MONO_DEBUGGER_I386_ARCH_H__

#if !defined(__i386__)
#error "Wrong architecture!"
#endif

#include <glib.h>

G_BEGIN_DECLS

#if defined(WINDOWS)

#include <windows.h>

struct _InferiorRegsType {
	CONTEXT context;
};

#elif defined(__linux__) || defined(__FreeBSD__)

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

#define INFERIOR_REG_DR_CONTROL(r)	r.dr_control
#define INFERIOR_REG_DR_STATUS(r)	r.dr_control
#define INFERIOR_REG_DR_N(r,n)		r.dr_regs[n]

#else
#error "Unknown operating systrem."
#endif

G_END_DECLS

#endif
