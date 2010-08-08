#ifndef __MDB_LINUX_WAIT_H__
#define __MDB_LINUX_WAIT_H__

#include <debugger-mutex.h>

G_BEGIN_DECLS

extern void
_linux_wait_global_init (void);

extern gboolean
_linux_wait_for_new_thread (ServerHandle *handle);

extern guint32
mdb_server_global_wait (guint32 *status_ret);

extern ServerCommandError
mdb_server_stop (ServerHandle *handle);

extern ServerCommandError
mdb_server_stop_and_wait (ServerHandle *handle, guint32 *out_status);

G_END_DECLS

#endif
