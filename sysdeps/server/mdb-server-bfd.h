#ifndef __MDB_SERVER_BFD_H__
#define __MDB_SERVER_BFD_H__

#include <server.h>

G_BEGIN_DECLS

typedef struct _MdbExeReader MdbExeReader;
typedef struct _MdbDisassembler MdbDisassembler;

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

extern const gchar *
mdb_exe_reader_lookup_symbol_by_addr (MdbExeReader *reader, guint64 address);

#if defined(__linux__) || defined(__FreeBSD__)

extern guint64
mdb_exe_reader_get_dynamic_info (ServerHandle *server, MdbExeReader *reader);

#endif

extern MdbDisassembler *
mdb_exe_reader_get_disassembler (ServerHandle *server, MdbExeReader *main_bfd);

extern gchar *
mdb_exe_reader_disassemble_insn (MdbDisassembler *disasm, guint64 address, guint32 *out_insn_size);

G_END_DECLS

#endif
