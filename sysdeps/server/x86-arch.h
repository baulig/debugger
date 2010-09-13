#ifndef __X86_ARCH_H__
#define __X86_ARCH_H__ 1

#include <mdb-arch.h>

typedef enum {
	DEBUGGER_REG_RAX	= 0,
	DEBUGGER_REG_RCX,
	DEBUGGER_REG_RDX,
	DEBUGGER_REG_RBX,

	DEBUGGER_REG_RSP,
	DEBUGGER_REG_RBP,
	DEBUGGER_REG_RSI,
	DEBUGGER_REG_RDI,

	DEBUGGER_REG_R8,
	DEBUGGER_REG_R9,
	DEBUGGER_REG_R10,
	DEBUGGER_REG_R11,
	DEBUGGER_REG_R12,
	DEBUGGER_REG_R13,
	DEBUGGER_REG_R14,
	DEBUGGER_REG_R15,

	DEBUGGER_REG_RIP,
	DEBUGGER_REG_EFLAGS,

	DEBUGGER_REG_ORIG_RAX,
	DEBUGGER_REG_CS,
	DEBUGGER_REG_SS,
	DEBUGGER_REG_DS,
	DEBUGGER_REG_ES,
	DEBUGGER_REG_FS,
	DEBUGGER_REG_GS,

	DEBUGGER_REG_FS_BASE,
	DEBUGGER_REG_GS_BASE,

	DEBUGGER_REG_LAST
} X86Registers;

typedef struct _CodeBufferData CodeBufferData;
typedef struct _CallbackData CallbackData;

/* Debug registers' indices.  */
#define DR_NADDR		4  /* the number of debug address registers */
#define DR_STATUS		6  /* index of debug status register (DR6) */
#define DR_CONTROL		7  /* index of debug control register (DR7) */

/* DR7 Debug Control register fields.  */

/* How many bits to skip in DR7 to get to R/W and LEN fields.  */
#define DR_CONTROL_SHIFT	16
/* How many bits in DR7 per R/W and LEN field for each watchpoint.  */
#define DR_CONTROL_SIZE		4

/* Watchpoint/breakpoint read/write fields in DR7.  */
#define DR_RW_EXECUTE		(0x0) /* break on instruction execution */
#define DR_RW_WRITE		(0x1) /* break on data writes */
#define DR_RW_READ		(0x3) /* break on data reads or writes */

/* This is here for completeness.  No platform supports this
   functionality yet (as of Mar-2001).  Note that the DE flag in the
   CR4 register needs to be set to support this.  */
#ifndef DR_RW_IORW
#define DR_RW_IORW		(0x2) /* break on I/O reads or writes */
#endif

/* Watchpoint/breakpoint length fields in DR7.  The 2-bit left shift
   is so we could OR this with the read/write field defined above.  */
#define DR_LEN_1		(0x0 << 2) /* 1-byte region watch or breakpt */
#define DR_LEN_2		(0x1 << 2) /* 2-byte region watch */
#define DR_LEN_4		(0x3 << 2) /* 4-byte region watch */
#define DR_LEN_8		(0x2 << 2) /* 8-byte region watch (x86-64) */

/* Local and Global Enable flags in DR7. */
#define DR_LOCAL_ENABLE_SHIFT	0   /* extra shift to the local enable bit */
#define DR_GLOBAL_ENABLE_SHIFT	1   /* extra shift to the global enable bit */
#define DR_ENABLE_SIZE		2   /* 2 enable bits per debug register */

/* The I'th debug register is vacant if its Local and Global Enable
   bits are reset in the Debug Control register.  */
#define X86_DR_VACANT(r,i) \
  ((INFERIOR_REG_DR_CONTROL(r) & (3 << (DR_ENABLE_SIZE * (i)))) == 0)

/* Locally enable the break/watchpoint in the I'th debug register.  */
#define X86_DR_LOCAL_ENABLE(r,i) \
  INFERIOR_REG_DR_CONTROL(r) |= (1 << (DR_LOCAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Globally enable the break/watchpoint in the I'th debug register.  */
#define X86_DR_GLOBAL_ENABLE(r,i) \
  INFERIOR_REG_DR_CONTROL(r) |= (1 << (DR_GLOBAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Disable the break/watchpoint in the I'th debug register.  */
#define X86_DR_DISABLE(r,i) \
  INFERIOR_REG_DR_CONTROL(r) &= ~(3 << (DR_ENABLE_SIZE * (i)))

/* Set in DR7 the RW and LEN fields for the I'th debug register.  */
#define X86_DR_SET_RW_LEN(r,i,rwlen) \
  do { \
    INFERIOR_REG_DR_CONTROL(r) &= ~(0x0f << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i))); \
    INFERIOR_REG_DR_CONTROL(r) |= ((rwlen) << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i))); \
  } while (0)

/* Get from DR7 the RW and LEN fields for the I'th debug register.  */
#define X86_DR_GET_RW_LEN(r,i) \
  ((INFERIOR_REG_DR_CONTROL(r) >> (DR_CONTROL_SHIFT + DR_CONTROL_SIZE * (i))) & 0x0f)

/* Did the watchpoint whose address is in the I'th register break?  */
#define X86_DR_WATCH_HIT(r,i) \
  (INFERIOR_REG_DR_STATUS(r) & (1 << (i)))

#define AMD64_RED_ZONE_SIZE 128

#if defined(WINDOWS)

#include <windows.h>

struct _InferiorRegs {
	CONTEXT context;
	DWORD dr_control;
	DWORD dr_status;
	DWORD dr_regs [DR_NADDR];
	int dr_index [DR_NADDR];
};

#define INFERIOR_REG_DR_CONTROL(r)	r.dr_control
#define INFERIOR_REG_DR_STATUS(r)	r.dr_status
#define INFERIOR_REG_DR_N(r,n)		r.dr_regs[n]
#define INFERIOR_DR_INDEX(r,n)		r.dr_index[n]

#elif defined(__linux__) || defined(__FreeBSD__)

#include <sys/user.h>
#include <signal.h>

struct _InferiorRegs {
	struct user_regs_struct regs;
	struct user_fpregs_struct fpregs;
	gsize dr_control, dr_status;
	gsize dr_regs [DR_NADDR];
	int dr_index [DR_NADDR];
};

#define INFERIOR_REG_DR_CONTROL(r)	r.dr_control
#define INFERIOR_REG_DR_STATUS(r)	r.dr_status
#define INFERIOR_REG_DR_N(r,n)		r.dr_regs[n]
#define INFERIOR_DR_INDEX(r,n)		r.dr_index[n]

#else
#error "Unknown operating systrem."
#endif

class X86Arch : public MdbArch
{
public:
	X86Arch (MdbInferior *inferior) : MdbArch (inferior)
	{
		callback_stack = g_ptr_array_new ();
		current_code_buffer = NULL;
		hw_bpm = NULL;
	}


	ErrorCode EnableBreakpoint (BreakpointInfo *breakpoint);
	ErrorCode DisableBreakpoint (BreakpointInfo *breakpoint);

	ServerEvent *ChildStopped (int stopsig, bool *out_remain_stopped);
	ErrorCode GetFrame (StackFrame *out_frame);

	void RemoveBreakpointsFromTargetMemory (guint64 start, guint32 size, gpointer buffer);

	int GetRegisterCount (void);
	ErrorCode GetRegisterValues (guint64 *values);
	ErrorCode SetRegisterValues (const guint64 *values);

	ErrorCode CallMethod (InvocationData *data);

	ErrorCode ExecuteInstruction (MdbInferior *inferior, gsize code_address, int insn_size,
				      bool update_ip, InferiorCallback *callback);

protected:
	ErrorCode
	GetRegisters (void)
	{
		return inferior->GetRegisters (&current_regs);
	}

	ErrorCode SetRegisters (void)
	{
		return inferior->SetRegisters (&current_regs);
	}

	CallbackData *
	GetCallbackData (void)
	{
		if (!callback_stack || !callback_stack->len)
			return NULL;

		return (CallbackData *) g_ptr_array_index (callback_stack, callback_stack->len - 1);
	}

	bool Marshal_Generic (InvocationData *data, CallbackData *cdata);

	InferiorRegs current_regs;

	GPtrArray *callback_stack;
	CodeBufferData *current_code_buffer;
	BreakpointManager *hw_bpm;
};

#endif
