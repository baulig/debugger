#include <mdb-server.h>
#include <bfd.h>
#if defined(__linux__) || defined(__FreeBSD__)
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

	g_message (G_STRLOC ": %d - %d - %p", reader->symtab_size, reader->num_symbols, reader->symtab);

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

		if (!strcmp (symbol->name, name))
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
		gpointer sym_address;
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

		// g_message (G_STRLOC ": %i - %p - %s", i, sym_address, bfd_asymbol_name (reader->symtab [i]));
		if (bfd_asymbol_value (reader->symtab [i]) == address)
			return bfd_asymbol_name (reader->symtab [i]);
	}

	return NULL;
}

static asection *
find_section (MdbExeReader *reader, const char *name)
{
	asection *section;

	g_message (G_STRLOC ": %s - %p", name, reader->bfd->sections);

	for (section = reader->bfd->sections; section; section = section->next) {
		g_message (G_STRLOC ": %p - %s - %Lx", section, section->name, section->vma);
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

#if defined(__linux__) || defined(__FreeBSD__)

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
