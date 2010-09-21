#ifndef __MONO_RUNTIME_H__
#define __MONO_RUNTIME_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

struct GenericInvocationData
{
	guint32 invocation_type;
	guint32 callback_id;

	guint64 method_address;
	guint64 arg1;
	guint64 arg2;
	guint64 arg3;

	guint32 data_size;
	guint32 dummy;

	gsize data_arg_ptr;
};

typedef enum {
	NOTIFICATION_INITIALIZE_MANAGED_CODE	= 1,
	NOTIFICATION_INITIALIZE_CORLIB,
	NOTIFICATION_JIT_BREAKPOINT,
	NOTIFICATION_INITIALIZE_THREAD_MANAGER,
	NOTIFICATION_ACQUIRE_GLOBAL_THREAD_LOCK,
	NOTIFICATION_RELEASE_GLOBAL_THREAD_LOCK,
	NOTIFICATION_WRAPPER_MAIN,
	NOTIFICATION_MAIN_EXITED,
	NOTIFICATION_UNHANDLED_EXCEPTION,
	NOTIFICATION_THROW_EXCEPTION,
	NOTIFICATION_HANDLE_EXCEPTION,
	NOTIFICATION_THREAD_CREATED,
	NOTIFICATION_THREAD_CLEANUP,
	NOTIFICATION_GC_THREAD_CREATED,
	NOTIFICATION_GC_THREAD_EXITED,
	NOTIFICATION_REACHED_MAIN,
	NOTIFICATION_FINALIZE_MANAGED_CODE,
	NOTIFICATION_LOAD_MODULE,
	NOTIFICATION_UNLOAD_MODULE,
	NOTIFICATION_DOMAIN_CREATE,
	NOTIFICATION_DOMAIN_UNLOAD,
	NOTIFICATION_CLASS_INITIALIZED,
	NOTIFICATION_INTERRUPTION_REQUEST,
	NOTIFICATION_CREATE_APPDOMAIN,
	NOTIFICATION_UNLOAD_APPDOMAIN,

	/* Obsolete, only for backwards compatibility with older debugger versions */
	NOTIFICATION_OLD_TRAMPOLINE    	= 256,

	NOTIFICATION_TRAMPOLINE		= 512
} NotificationType;

class MonoRuntime : public ServerObject
{
public:
	static MonoRuntime *Initialize (MdbInferior *inferior, MdbExeReader *exe);

	virtual gsize GetNotificationAddress (void) = 0;

	virtual gsize GetGenericInvocationFunc (void) = 0;

	virtual gsize GetLMFAddress (MdbInferior *inferior) = 0;

	virtual ErrorCode EnableBreakpoint (MdbInferior *inferior, BreakpointInfo *breakpoint) = 0;
	virtual ErrorCode DisableBreakpoint (MdbInferior *inferior, BreakpointInfo *breakpoint) = 0;

	virtual ErrorCode ExecuteInstruction (MdbInferior *inferior, const guint8 *instruction,
					      guint32 size, bool update_ip) = 0;

	virtual ErrorCode SetExtendedNotifications (MdbInferior *inferior, NotificationType type, bool enable) = 0;

	virtual ErrorCode InitializeThreads (MdbInferior *inferior) = 0;

	virtual void HandleNotification (MdbInferior *inferior, NotificationType type, gsize arg1, gsize arg2) = 0;

protected:
	MonoRuntime (MdbProcess *process, MdbExeReader *exe)
		: ServerObject (SERVER_OBJECT_KIND_MONO_RUNTIME)
	{
		this->process = process;
		this->exe = exe;
	}

private:
	MdbProcess *process;
	MdbExeReader *exe;
};

#endif
