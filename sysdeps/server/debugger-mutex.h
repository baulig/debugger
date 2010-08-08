#ifndef __DEBUGGER_MUTEX_H__
#define __DEBUGGER_MUTEX_H__

#include <glib.h>

G_BEGIN_DECLS

typedef struct _DebuggerMutex DebuggerMutex;

DebuggerMutex *
debugger_mutex_new (void);

gboolean
debugger_mutex_trylock (DebuggerMutex *mutex);

void
debugger_mutex_lock (DebuggerMutex *mutex);

void
debugger_mutex_unlock (DebuggerMutex *mutex);

G_END_DECLS

#endif
