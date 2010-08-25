#include <mdb-server.h>
#include <mdb-server-bfd.h>
#include <string.h>
#include <bfd.h>
#include <dis-asm.h>
#if (defined(__linux__) || defined(__FreeBSD__)) && (defined(__i386__) || defined(__x86_64__))
#include <link.h>
#include <elf.h>
#endif

struct _MdbExeReader
{
	bfd *bfd;
	long symtab_size;
	long num_symbols;
	asymbol **symtab;
};

MdbExeReader *
mdb_server_load_library (ServerHandle *server, const char *filename)
{
	ProcessHandle *process = server->process;
	MdbExeReader *reader;

	reader = mdb_server_create_exe_reader (filename);
	if (!reader)
		return NULL;

	if (!process->main_bfd)
		process->main_bfd = reader;
	if (!process->bfd_hash)
		process->bfd_hash = g_hash_table_new (NULL, NULL);
	g_hash_table_insert (process->bfd_hash, g_strdup (filename), reader);
	return reader;
}

MdbExeReader *
mdb_server_create_exe_reader (const char *filename)
{
	MdbExeReader *reader = g_new0 (MdbExeReader, 1);

	reader->bfd = bfd_openr (g_strdup (filename), NULL);
	if (!reader->bfd) {
		g_free (reader);
		return NULL;
	}

	if (!bfd_check_format (reader->bfd, bfd_object) && !bfd_check_format (reader->bfd, bfd_archive)) {
		g_warning (G_STRLOC ": Invalid bfd format: %s", filename);
		bfd_close (reader->bfd);
		g_free (reader);
		return NULL;
	}

	reader->symtab_size = bfd_get_symtab_upper_bound (reader->bfd);

	if (reader->symtab_size) {
		reader->symtab = g_malloc0 (reader->symtab_size);
		reader->num_symbols = bfd_canonicalize_symtab (reader->bfd, reader->symtab);
	}

	return reader;
}

guint64
mdb_exe_reader_get_start_address (MdbExeReader *reader)
{
	return bfd_get_start_address (reader->bfd);
}

guint64
mdb_exe_reader_lookup_symbol (MdbExeReader *reader, const char *name)
{
	int i;

	if (!reader->symtab)
		return 0;

	for (i = 0; i < reader->num_symbols; i++) {
		asymbol *symbol = reader->symtab [i];
		const char *symname = symbol->name;
		gboolean is_function;
		guint64 address;
		int flags;

		if ((symbol->flags & (BSF_WEAK | BSF_DYNAMIC)) == (BSF_WEAK | BSF_DYNAMIC))
			continue;
		if ((symbol->flags & BSF_DEBUGGING) || !symbol->name || !strlen (symbol->name))
			continue;

		flags = symbol->flags & ~(BSF_DYNAMIC | BSF_NOT_AT_END);

		if (flags == (BSF_OBJECT | BSF_GLOBAL)) {
			is_function = 0;
			address = symbol->section->vma + symbol->value;
		} else if (flags == BSF_FUNCTION) {
			is_function = 1;
			address = symbol->value;
		} else if (flags == (BSF_FUNCTION | BSF_GLOBAL)) {
			is_function = 1;
			address = symbol->section->vma + symbol->value;
		} else if (flags == (BSF_OBJECT | BSF_LOCAL)) {
			is_function = 0;
			address = symbol->section->vma + symbol->value;
		} else {
			//Mach headers don't have a function flag. Mark everything as function for now.
			//Possibly check address against text section to fix this properly?
			is_function = 1;
			address = symbol->section->vma + symbol->value;
		}

#if WINDOWS
		if (*symname == '_')
			++symname;
#endif

		if (!strcmp (symname, name))
			return address;
	}

	return 0;
}

gchar *
mdb_exe_reader_get_target_name (MdbExeReader *reader)
{
	return g_strdup (reader->bfd->xvec->name);
}

const gchar *
mdb_exe_reader_lookup_symbol_by_addr (MdbExeReader *reader, guint64 address)
{
	long i;

	for (i = 0; i < reader->num_symbols; i++) {
		asymbol *symbol = reader->symtab [i];
		gboolean is_function;
		guint64 sym_address;
		int flags;

		if ((symbol->flags & (BSF_WEAK | BSF_DYNAMIC)) == (BSF_WEAK | BSF_DYNAMIC))
			continue;
		if ((symbol->flags & BSF_DEBUGGING) || !symbol->name || !strlen (symbol->name))
			continue;

		flags = symbol->flags & ~(BSF_DYNAMIC | BSF_NOT_AT_END);

		if (flags == (BSF_FUNCTION | BSF_GLOBAL)) {
			is_function = 1;
			sym_address = symbol->section->vma + symbol->value;
		} else {
			continue;
		}

		if (bfd_asymbol_value (reader->symtab [i]) == address)
			return bfd_asymbol_name (reader->symtab [i]);
	}

	return NULL;
}

static asection *
find_section (MdbExeReader *reader, const char *name)
{
	asection *section;

	for (section = reader->bfd->sections; section; section = section->next) {
		if (!strcmp (section->name, name))
			return section;
	}

	return NULL;
}

gboolean
mdb_exe_reader_has_section (MdbExeReader *reader, const char *name)
{
	return find_section (reader, name) != NULL;
}

guint64
mdb_exe_reader_get_section_address (MdbExeReader *reader, const char *name)
{
	asection *section = find_section (reader, name);
	if (!section)
		return 0;
	return section->vma;
}

gpointer
mdb_exe_reader_get_section_contents (MdbExeReader *reader, const char *name, guint32 *out_size)
{
	asection *section;
	gpointer contents;

	section = find_section (reader, name);
	if (!section) {
		*out_size = -1;
		return NULL;
	}

	*out_size = bfd_get_section_size (section);

	contents = g_malloc0 (*out_size);
	if (!bfd_get_section_contents (reader->bfd, section, contents, 0, *out_size)) {
		g_free (contents);
		*out_size = -1;
		return NULL;
	}

	return contents;
}

#if (defined(__linux__) || defined(__FreeBSD__)) && (defined(__i386__) || defined(__x86_64__))

static guint64
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

guint64
mdb_exe_reader_get_dynamic_info (ServerHandle *server, MdbExeReader *reader)
{
	asection *section;
	gpointer contents;
	guint64 dynamic_base;
	int size;

	section = find_section (reader, ".dynamic");
	if (!section)
		return 0;

	size = bfd_get_section_size (section);

	contents = g_malloc0 (size);

	if (mono_debugger_server_read_memory (server, section->vma, size, contents)) {
		g_free (contents);
		return 0;
	}

	dynamic_base = bfd_glue_elfi386_locate_base (reader->bfd, contents, size);

	g_free (contents);

	return dynamic_base;
}

#endif

#define DISASM_BUFSIZE 1024

struct _MdbDisassembler
{
	ServerHandle *server;
	MdbExeReader *main_bfd;
	struct disassemble_info info;
	char disasm_buffer [DISASM_BUFSIZE];
};

static int
disasm_read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	MdbDisassembler *disasm = info->application_data;

	return mdb_server_read_memory (disasm->server, memaddr, length, myaddr);
}

static int
disasm_fprintf_func (gpointer stream, const char *message, ...)
{
	MdbDisassembler *disasm = stream;
	va_list args;
	char *start;
	int len, max, retval;

	len = strlen (disasm->disasm_buffer);
	start = disasm->disasm_buffer + len;
	max = DISASM_BUFSIZE - len;

	va_start (args, message);
	retval = vsnprintf (start, max, message, args);
	va_end (args);

	return retval;
}

static void
disasm_print_address_func (bfd_vma addr, struct disassemble_info *info)
{
	MdbDisassembler *disasm = info->application_data;
	const gchar *sym;
	char buf[30];

	sprintf_vma (buf, addr);

	if (disasm->main_bfd) {
		sym = mdb_exe_reader_lookup_symbol_by_addr (disasm->main_bfd, addr);
		if (sym) {
			(*info->fprintf_func) (info->stream, "%s(0x%s)", sym, buf);
			return;
		}
	}

	(*info->fprintf_func) (info->stream, "0x%s", buf);
}

MdbDisassembler *
mdb_exe_reader_get_disassembler (ServerHandle *server, MdbExeReader *main_bfd)
{
	MdbDisassembler *disasm;

	disasm = g_new0 (MdbDisassembler, 1);

	disasm->server = server;
	disasm->main_bfd = main_bfd;

	INIT_DISASSEMBLE_INFO (disasm->info, stderr, fprintf);
	disasm->info.flavour = bfd_target_coff_flavour;
	disasm->info.arch = bfd_get_arch (main_bfd->bfd);
	disasm->info.mach = bfd_get_mach (main_bfd->bfd);
	disasm->info.octets_per_byte = bfd_octets_per_byte (main_bfd->bfd);
	disasm->info.display_endian = disasm->info.endian = main_bfd->bfd->xvec->byteorder;

	disasm->info.read_memory_func = disasm_read_memory_func;
	disasm->info.print_address_func = disasm_print_address_func;
	disasm->info.fprintf_func = disasm_fprintf_func;
	disasm->info.application_data = disasm;
	disasm->info.stream = disasm;

	return disasm;
}

gchar *
mdb_exe_reader_disassemble_insn (MdbDisassembler *disasm, guint64 address, guint32 *out_insn_size)
{
	int ret;

	memset (disasm->disasm_buffer, 0, DISASM_BUFSIZE);

#if defined(__x86_64__) || defined(__i386__)
	ret = print_insn_i386 ((gsize) address, &disasm->info);
#elif defined(__ARM__)
	if (bfd_little_endian (disasm->main_bfd))
		ret = print_insn_littlearm ((gsize) address, &disasm->info);
	else
		ret = print_insn_bigarm ((gsize) address, &disasm
#endif

	if (out_insn_size)
		*out_insn_size = ret;

	return g_strdup (disasm->disasm_buffer);
}
