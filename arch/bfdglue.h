#ifndef __BFD_GLUE_H__
#define __BFD_GLUE_H__

#include <glib.h>
#include <bfd.h>
#include <dis-asm.h>
#include <sys/user.h>

G_BEGIN_DECLS

extern gboolean
bfd_glue_check_format_object (bfd *abfd);

extern gboolean
bfd_glue_check_format_core (bfd *abfd);

extern int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table);

extern const char *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int idx, guint64 *address);

extern struct disassemble_info *
bfd_glue_init_disassembler (bfd *abfd);

typedef int (*BfdGlueReadMemoryHandler) (guint64 address, bfd_byte *buffer, int size);
typedef void (*BfdGlueOutputHandler) (const char *output);
typedef void (*BfdGluePrintAddressHandler) (guint64 address);

typedef struct {
	BfdGlueReadMemoryHandler read_memory_cb;
	BfdGlueOutputHandler output_cb;
	BfdGluePrintAddressHandler print_address_cb;
} BfdGlueDisassemblerInfo;

extern void
bfd_glue_setup_disassembler (struct disassemble_info *info, BfdGlueReadMemoryHandler read_memory_cb,
			     BfdGlueOutputHandler output_cb, BfdGluePrintAddressHandler print_address_cb);

extern void
bfd_glue_free_disassembler (struct disassemble_info *info);

extern int
bfd_glue_disassemble_insn (disassembler_ftype dis, struct disassemble_info *info, guint64 address);

extern gboolean
bfd_glue_get_section_contents (bfd *abfd, asection *section, int raw_section, guint64 offset,
			       gpointer *data, guint32 *size);

extern gboolean
bfd_glue_core_file_elfi386_get_registers (const guint8 *data, int size, struct user_regs_struct **regs);

G_END_DECLS

#endif
