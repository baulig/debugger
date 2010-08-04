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
	ERR_NO_SUCH_BPM = 0x1004,
	ERR_NO_SUCH_EXE_READER = 0x1005,
	ERR_CANNOT_OPEN_EXE = 0x1006
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

typedef struct _MdbExeReader MdbExeReader;

extern MdbExeReader *
mdb_server_create_exe_reader (const char *filename);

extern guint64
mdb_exe_reader_get_start_address (MdbExeReader *reader);

extern guint64
mdb_exe_reader_lookup_symbol (MdbExeReader *reader, const char *name);

extern gchar *
mdb_exe_reader_get_target_name (MdbExeReader *reader);

extern gboolean
mdb_exe_reader_has_section (MdbExeReader *reader, const char *name);

extern guint64
mdb_exe_reader_get_section_address (MdbExeReader *reader, const char *name);

extern gpointer
mdb_exe_reader_get_section_contents (MdbExeReader *reader, const char *name, guint32 *out_size);

G_END_DECLS

#endif
