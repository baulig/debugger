#ifndef __MDB_SERVER_H__
#define __MDB_SERVER_H__

#include <server.h>

G_BEGIN_DECLS

typedef enum {
	EVENT_KIND_TARGET_EVENT = 1
} EventKind;

typedef enum {
	ERR_NONE = 0,

	ERR_TARGET_ERROR_MASK = 0x0fff,

	ERR_UNKNOWN_ERROR = 0x1001,
	ERR_NOT_IMPLEMENTED = 0x1002,
	ERR_NO_SUCH_INFERIOR = 0x1003,
	ERR_NO_SUCH_BPM = 0x1004
} ErrorCode;

extern int
mdb_server_init_os (void);

extern ServerHandle *
mdb_server_get_inferior_by_pid (int pid);

extern void
mdb_server_handle_wait_event (void);

extern void
mdb_server_main_loop (int conn_fd);

extern gboolean
mdb_server_main_loop_iteration (void);

extern void
mdb_server_process_child_event (ServerStatusMessageType message, guint32 pid,
				guint64 arg, guint64 data1, guint64 data2,
				guint32 opt_data_size, gpointer opt_data);

G_END_DECLS

#endif
