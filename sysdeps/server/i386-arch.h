#ifndef __MONO_DEBUGGER_I386_ARCH_H__
#define __MONO_DEBUGGER_I386_ARCH_H__

#include <glib.h>

G_BEGIN_DECLS

#if defined(__i386__)

#ifdef HAVE_SYS_USER_H
#include <sys/user.h>
#endif

#ifndef WINDOWS
#define INFERIOR_REGS_TYPE	struct user_regs_struct
#define INFERIOR_FPREGS_TYPE	struct user_fpregs_struct

#define INFERIOR_REG_EIP(r)	r.eip
#define INFERIOR_REG_ESP(r)	r.esp
#define INFERIOR_REG_EBP(r)	r.ebp
#define INFERIOR_REG_EAX(r)	r.eax
#define INFERIOR_REG_EBX(r)	r.ebx
#define INFERIOR_REG_ECX(r)	r.ecx
#define INFERIOR_REG_EDX(r)	r.edx
#define INFERIOR_REG_ESI(r)	r.esi
#define INFERIOR_REG_EDI(r)	r.edi
#define INFERIOR_REG_ORIG_EAX(r)	r.orig_eax
#define INFERIOR_REG_EFLAGS(r)	r.eflags
#define INFERIOR_REG_ESP(r)	r.esp
#define INFERIOR_REG_FS(r)	r.xfs
#define INFERIOR_REG_ES(r)	r.xes
#define INFERIOR_REG_DS(r)	r.xds
#define INFERIOR_REG_CS(r)	r.xcs
#define INFERIOR_REG_SS(r)	r.xss
#define INFERIOR_REG_GS(r)	r.xgs
#else
/* macros  for accessing registries in the debuggee.context structure */
#define INFERIOR_REG_EIP(r)	r.Eip
#define INFERIOR_REG_ESP(r)	r.Esp
#define INFERIOR_REG_EBP(r)	r.Ebp
#define INFERIOR_REG_EAX(r)	r.Eax
#define INFERIOR_REG_EBX(r)	r.Ebx
#define INFERIOR_REG_ECX(r)	r.Ecx
#define INFERIOR_REG_EDX(r)	r.Edx
#define INFERIOR_REG_ESI(r)	r.Esi
#define INFERIOR_REG_EDI(r)	r.Edi
/* what that in CONTEXT ? #define INFERIOR_REG_ORIG_EAX(r)	r.orig_eax */
#define INFERIOR_REG_EFLAGS(r)	r.EFlags
#define INFERIOR_REG_ESP(r)	r.Esp
/* extended registers, how to get them in Windows there is no 
   name for them, check with jacob
*/
/*
#define INFERIOR_REG_FS(r)	r.xfs
#define INFERIOR_REG_ES(r)	r.xes
#define INFERIOR_REG_DS(r)	r.xds
#define INFERIOR_REG_CS(r)	r.xcs
#define INFERIOR_REG_SS(r)	r.xss
#define INFERIOR_REG_GS(r)	r.xgs
*/
#endif

G_END_DECLS

#else
#error "Wrong architecture!"
#endif

#endif
