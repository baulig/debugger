#include <debugger-mutex.h>

#if WINDOWS
#include <windows.h>
#else
#include <pthread.h>
#endif

struct _DebuggerMutex
{
#if WINDOWS
	CRITICAL_SECTION section;
#else
	pthread_mutex_t mutex;
#endif
};

DebuggerMutex *
debugger_mutex_new (void)
{
	DebuggerMutex *mutex = g_new0 (DebuggerMutex, 1);

#if WINDOWS
	InitializeCriticalSection (&mutex->section);
#else
	pthread_mutex_init (&mutex->mutex);
#endif

	return mutex;
}

void
debugger_mutex_lock (DebuggerMutex *mutex)
{
#if WINDOWS
	EnterCriticalSection (&mutex->section);
#else
	pthread_mutex_lock (&mutex->mutex);
#endif
}

void
debugger_mutex_unlock (DebuggerMutex *mutex)
{
#if WINDOWS
	LeaveCriticalSection (&mutex->section);
#else
	pthread_mutex_unlock (&mutex->mutex);
#endif
}

