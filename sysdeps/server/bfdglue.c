#if 0
#include <bfdglue.h>
#include <signal.h>
#include <string.h>
#if defined(__linux__) || defined(__FreeBSD__)
#include <link.h>
#include <elf.h>
#endif
#ifdef __linux__
#include <sys/user.h>
#include <sys/procfs.h>
#endif
#ifdef __FreeBSD__
#include <sys/param.h>
#include <sys/procfs.h>
#endif

bfd *
bfd_glue_openr (const char *filename, const char *target)
{
	return bfd_openr (g_strdup (filename), NULL);
}

gchar *
bfd_glue_get_target_name (bfd *abfd)
{
	return g_strdup (abfd->xvec->name);
}

gboolean
bfd_glue_check_format_object (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_object);
}

gboolean
bfd_glue_check_format_core (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_core);
}

gboolean
bfd_glue_check_format_archive (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_archive);
}

bfd *
bfd_glue_openr_next_archived_file (bfd* archive, bfd* last)
{
	return bfd_openr_next_archived_file (archive, last);
}

int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table)
{
	int storage_needed = bfd_get_symtab_upper_bound (abfd);

	if (storage_needed <= 0) {
		*symbol_table = NULL;
		return storage_needed;
	}

	*symbol_table = g_malloc0 (storage_needed);

	return bfd_canonicalize_symtab (abfd, *symbol_table);
}

gchar *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int idx, int *is_function, guint64 *address)
{
	asymbol *symbol;
	int flags;

	symbol = symbol_table [idx];

	if ((symbol->flags & (BSF_WEAK | BSF_DYNAMIC)) == (BSF_WEAK | BSF_DYNAMIC))
		return NULL;
	if ((symbol->flags & BSF_DEBUGGING) || !symbol->name || !strlen (symbol->name))
		return NULL;

	flags = symbol->flags & ~(BSF_DYNAMIC | BSF_NOT_AT_END);

	if (flags == (BSF_OBJECT | BSF_GLOBAL)) {
		*is_function = 0;
		*address = symbol->section->vma + symbol->value;
	} else if (flags == BSF_FUNCTION) {
		*is_function = 1;
		*address = symbol->value;
	} else if (flags == (BSF_FUNCTION | BSF_GLOBAL)) {
		*is_function = 1;
		*address = symbol->section->vma + symbol->value;
	} else if (flags == (BSF_OBJECT | BSF_LOCAL)) {
#if 0
		if (strncmp (symbol->name, "__pthread_", 10) &&
		    strncmp (symbol->name, "MONO_DEBUGGER_", 14) &&
		    strcmp (symbol->name, "__libc_pthread_functions"))
			return NULL;
#endif

		*is_function = 0;
		*address = symbol->section->vma + symbol->value;
	}
	else
	{
		//Mach headers don't have a function flag. Mark everything as function for now.
		//Possibly check address against text section to fix this properly?
		*is_function = 1;
		*address = symbol->section->vma + symbol->value;
	}

	return g_strdup (symbol->name);
}

int
bfd_glue_get_dynamic_symbols (bfd *abfd, asymbol ***symbol_table)
{
	int storage_needed = bfd_get_dynamic_symtab_upper_bound (abfd);

	if (storage_needed <= 0) {
		*symbol_table = NULL;
		return storage_needed;
	}

	*symbol_table = g_malloc0 (storage_needed);

	return bfd_canonicalize_dynamic_symtab (abfd, *symbol_table);
}

static int
read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	BfdGlueDisassemblerInfo *data = info->application_data;

	return (* data->read_memory_cb) (memaddr, myaddr, length);
}

static int
fprintf_func (gpointer stream, const char *message, ...)
{
	BfdGlueDisassemblerInfo *data = stream;
	va_list args;
	gchar *output;
	int retval;

	va_start (args, message);
	output = g_strdup_vprintf (message, args);
	va_end (args);

	data->output_cb (output);
	retval = strlen (output);
	g_free (output);

	return retval;
}

static void
print_address_func (bfd_vma address, struct disassemble_info *info)
{
	BfdGlueDisassemblerInfo *data = info->application_data;

	(* data->print_address_cb) (address);
}

BfdGlueDisassemblerInfo *
bfd_glue_create_disassembler (gboolean is_x86_64,
			      BfdGlueReadMemoryHandler read_memory_cb,
			      BfdGlueOutputHandler output_cb,
			      BfdGluePrintAddressHandler print_address_cb)
{
	BfdGlueDisassemblerInfo *handle;
	struct disassemble_info *info;

	info = g_new0 (struct disassemble_info, 1);
	INIT_DISASSEMBLE_INFO (*info, stderr, fprintf);
	info->flavour = bfd_target_elf_flavour;
	info->arch = bfd_arch_i386;
	info->mach = is_x86_64 ? bfd_mach_x86_64 : bfd_mach_i386_i386;
	info->octets_per_byte = 1;
	info->display_endian = info->endian = BFD_ENDIAN_LITTLE;

	handle = g_new0 (BfdGlueDisassemblerInfo, 1);
	handle->read_memory_cb = read_memory_cb;
	handle->output_cb = output_cb;
	handle->print_address_cb = print_address_cb;
	handle->disassembler = print_insn_i386;
	handle->info = info;

	info->application_data = handle;
	info->read_memory_func = read_memory_func;
	info->fprintf_func = fprintf_func;
	info->print_address_func = print_address_func;
	info->stream = handle;

	return handle;
}

void
bfd_glue_free_disassembler (BfdGlueDisassemblerInfo *handle)
{
	g_free (handle->info);
	g_free (handle);
}

int
bfd_glue_disassemble_insn (BfdGlueDisassemblerInfo *handle, guint64 address)
{
	return handle->disassembler (address, handle->info);
}

gboolean
bfd_glue_get_section_contents (bfd *abfd, asection *section, gpointer data, guint32 size)
{
	return bfd_get_section_contents (abfd, section, data, 0, size);
}

asection *
bfd_glue_get_first_section (bfd *abfd)
{
	return abfd->sections;
}

asection *
bfd_glue_get_next_section (asection *p)
{
	return p->next;
}

guint64
bfd_glue_get_section_vma (asection *p)
{
	return p->vma;
}

gchar *
bfd_glue_get_section_name (asection *p)
{
	return g_strdup (p->name);
}

gchar *
bfd_glue_get_errormsg (void)
{
	return g_strdup (bfd_errmsg (bfd_get_error ()));
}

guint32
bfd_glue_get_section_size (asection *p)
{
	return p->_raw_size;
}

BfdGlueSectionFlags
bfd_glue_get_section_flags (asection *p)
{
	BfdGlueSectionFlags flags = 0;

	if (p->flags & SEC_LOAD)
		flags |= SECTION_FLAGS_LOAD;
	if (p->flags & SEC_ALLOC)
		flags |= SECTION_FLAGS_ALLOC;
	if (p->flags & SEC_READONLY)
		flags |= SECTION_FLAGS_READONLY;

	return flags;
}

gchar *
bfd_glue_core_file_failing_command (bfd *abfd)
{
	return g_strdup (bfd_core_file_failing_command (abfd));
}

guint64
bfd_glue_elfi386_locate_base (bfd *abfd, const guint8 *data, int size)
{
#if defined(__linux__) || defined(__FreeBSD__)
	const guint8 *ptr;

#if defined(__i386__)
	for (ptr = data; ptr < data + size; ptr += sizeof (Elf32_Dyn)) {
		Elf32_Dyn *dyn = (Elf32_Dyn *) ptr;

		if (dyn->d_tag == DT_NULL)
			break;
		else if (dyn->d_tag == DT_DEBUG)
			return (guint32) dyn->d_un.d_ptr;
	}
#elif defined(__x86_64__)
	for (ptr = data; ptr < data + size; ptr += sizeof (Elf64_Dyn)) {
		Elf64_Dyn *dyn = (Elf64_Dyn *) ptr;

		if (dyn->d_tag == DT_NULL)
			break;
		else if (dyn->d_tag == DT_DEBUG)
			return (guint64) dyn->d_un.d_ptr;
	}
#else
#error "Unknown architecture"
#endif
#endif

	return 0;
}

gboolean
bfd_glue_core_file_elfi386_get_registers (const guint8 *data, int size, guint32 *regs)
{
#ifdef __linux__
	if (size != 68) {
		g_warning (G_STRLOC ": Core file has unknown .reg section size %d", size);
		return FALSE;
	}

	memcpy (regs, data, size);
	return TRUE;
#endif
#ifdef __FreeBSD__
	gregset_t *regset = (gregset_t *) data;

	if (size != sizeof (gregset_t)) {
		g_warning (G_STRLOC ": Core file has unknown .reg section size %d (expected %d)",
			   size, sizeof (gregset_t));
		return FALSE;
	}

	regs [0] = regset->r_ebx;
	regs [1] = regset->r_ecx;
	regs [2] = regset->r_edx;
	regs [3] = regset->r_esi;
	regs [4] = regset->r_edi;
	regs [5] = regset->r_ebp;
	regs [6] = regset->r_eax;
	regs [7] = regset->r_ds;
	regs [8] = regset->r_es;
	regs [9] = regset->r_fs;
	regs [10] = regset->r_gs;
	regs [12] = regset->r_eip;
	regs [13] = regset->r_cs;
	regs [14] = regset->r_eflags;
	regs [15] = regset->r_esp;
	regs [16] = regset->r_ss;

	return TRUE;
#endif

	return FALSE;
}

guint64
bfd_glue_get_start_address (bfd *abfd)
{
	return bfd_get_start_address (abfd);
}
#endif
