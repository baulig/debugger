#include <mdb-exe-reader.h>
#include <mdb-arch.h>
#include <string.h>
#include <bfd.h>
#include <dis-asm.h>

class BfdReader : public MdbExeReader {
public:
	guint64 GetStartAddress (void);
	guint64 LookupSymbol (const char *name);
	const char *GetTargetName (void);
	gboolean HasSection (const char *name);
	guint64 GetSectionAddress (const char *name);
	gpointer GetSectionContents (const char *name, guint32 *out_size);
	const char *LookupSymbol (guint64 address);

	MdbDisassembler *GetDisassembler (MdbInferior *inferior);

	bfd_architecture GetArch (void)
	{
		return bfd_get_arch (bfd_handle);
	}

	unsigned int GetMach (void)
	{
		return bfd_get_mach (bfd_handle);
	}

	unsigned int GetOctetsPerByte (void)
	{
		return bfd_octets_per_byte (bfd_handle);
	}

	bfd_endian GetByteOrder (void)
	{
		return bfd_handle->xvec->byteorder;
	}

private:
	bfd *bfd_handle;
	long symtab_size;
	long num_symbols;
	asymbol **symtab;

	BfdReader (const char *filename);
	asection *FindSection (const char *name);
	~BfdReader (void);

	friend MdbExeReader *mdb_server_create_exe_reader (const char *filename);
};

#define DISASM_BUFSIZE 1024

static int disasm_read_memory_func (bfd_vma, bfd_byte *, unsigned int, struct disassemble_info *);
static int disasm_fprintf_func (gpointer, const char *, ...);
static void disasm_print_address_func (bfd_vma, struct disassemble_info *);

class BfdDisassembler : public MdbDisassembler
{
public:
	BfdDisassembler (MdbInferior *inferior, BfdReader *main_bfd);
	gchar *DisassembleInstruction (guint64 address, guint32 *out_insn_size);

private:
	MdbExeReader *main_reader;
	struct disassemble_info info;
	char disasm_buffer [DISASM_BUFSIZE];

	friend int disasm_read_memory_func (bfd_vma, bfd_byte *, unsigned int, struct disassemble_info *);
	friend int disasm_fprintf_func (gpointer, const char *, ...);
	friend void disasm_print_address_func (bfd_vma, struct disassemble_info *);
};

BfdReader::BfdReader (const char *filename)
	: MdbExeReader (filename)
{
	this->bfd_handle = bfd_openr (filename, NULL);
	if (!this->bfd_handle)
		return;

	if (!bfd_check_format (this->bfd_handle, bfd_object) && !bfd_check_format (this->bfd_handle, bfd_archive)) {
		g_warning (G_STRLOC ": Invalid bfd format: %s", filename);
		bfd_close (this->bfd_handle);
		this->bfd_handle = NULL;
		return;
	}

	this->symtab_size = bfd_get_symtab_upper_bound (this->bfd_handle);

	if (this->symtab_size) {
		this->symtab = (asymbol **) g_malloc0 (this->symtab_size);
		this->num_symbols = bfd_canonicalize_symtab (this->bfd_handle, this->symtab);
	}
}

BfdReader::~BfdReader (void)
{
	if (this->bfd_handle) {
		bfd_close (this->bfd_handle);
		this->bfd_handle = NULL;
	}

	if (this->symtab) {
		g_free (this->symtab);
		this->symtab = NULL;
	}
}

guint64
BfdReader::GetStartAddress (void)
{
	return bfd_get_start_address (this->bfd_handle);
}

guint64
BfdReader::LookupSymbol (const char *name)
{
	int i;

	if (!this->symtab)
		return 0;

	for (i = 0; i < this->num_symbols; i++) {
		asymbol *symbol = this->symtab [i];
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

const char *
BfdReader::GetTargetName (void)
{
	return this->bfd_handle->xvec->name;
}

asection *
BfdReader::FindSection (const char *name)
{
	asection *section;

	for (section = this->bfd_handle->sections; section; section = section->next) {
		if (!strcmp (section->name, name))
			return section;
	}

	return NULL;
}

gboolean
BfdReader::HasSection (const char *name)
{
	return FindSection (name) != NULL;
}

guint64
BfdReader::GetSectionAddress (const char *name)
{
	asection *section = FindSection (name);
	if (!section)
		return 0;
	return section->vma;
}

gpointer
BfdReader::GetSectionContents (const char *name, guint32 *out_size)
{
	asection *section;
	gpointer contents;

	section = FindSection (name);
	if (!section) {
		*out_size = 0;
		return NULL;
	}

	*out_size = bfd_get_section_size (section);

	contents = g_malloc0 (*out_size);
	if (!bfd_get_section_contents (this->bfd_handle, section, contents, 0, *out_size)) {
		g_free (contents);
		*out_size = 0;
		return NULL;
	}

	return contents;
}

const char *
BfdReader::LookupSymbol (guint64 address)
{
		long i;

	for (i = 0; i < this->num_symbols; i++) {
		asymbol *symbol = this->symtab [i];
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

		if (bfd_asymbol_value (this->symtab [i]) == address)
			return bfd_asymbol_name (this->symtab [i]);
	}

	return NULL;
}

MdbExeReader *
mdb_server_create_exe_reader (const char *filename)
{
	BfdReader *reader = new BfdReader (filename);
	if (!reader->bfd_handle) {
		delete reader;
		return NULL;
	}
	return reader;
}

MdbDisassembler *
BfdReader::GetDisassembler (MdbInferior *inferior)
{
	return new BfdDisassembler (inferior, this);
}

static int
disasm_read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	BfdDisassembler *disasm = (BfdDisassembler *) info->application_data;

	if (disasm->inferior->ReadMemory (memaddr, length, myaddr))
		return 1;

	disasm->inferior->GetArch ()->RemoveBreakpointsFromTargetMemory (memaddr, length, myaddr);
	return 0;
}

static int
disasm_fprintf_func (gpointer stream, const char *message, ...)
{
	BfdDisassembler *disasm = (BfdDisassembler *) stream;
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
	BfdDisassembler *disasm = (BfdDisassembler *) info->application_data;
	const gchar *sym;
	char buf[30];

	sprintf_vma (buf, addr);

	if (disasm->main_reader) {
		sym = disasm->main_reader->LookupSymbol (addr);
		if (sym) {
			(*info->fprintf_func) (info->stream, "%s(0x%s)", sym, buf);
			return;
		}
	}

	(*info->fprintf_func) (info->stream, "0x%s", buf);
}

BfdDisassembler::BfdDisassembler (MdbInferior *inferior, BfdReader *main_bfd)
{
	this->inferior = inferior;
	this->main_reader = main_bfd;

	INIT_DISASSEMBLE_INFO (this->info, stderr, fprintf);
	this->info.flavour = bfd_target_coff_flavour;
	this->info.arch = main_bfd->GetArch ();
	this->info.mach = main_bfd->GetMach ();
	this->info.octets_per_byte = main_bfd->GetOctetsPerByte ();
	this->info.display_endian = this->info.endian = main_bfd->GetByteOrder ();

	this->info.read_memory_func = disasm_read_memory_func;
	this->info.print_address_func = disasm_print_address_func;
	this->info.fprintf_func = disasm_fprintf_func;
	this->info.application_data = this;
	this->info.stream = this;
}

gchar *
BfdDisassembler::DisassembleInstruction (guint64 address, guint32 *out_insn_size)
{
	int ret;

	memset (this->disasm_buffer, 0, DISASM_BUFSIZE);

#if defined(__x86_64__) || defined(__i386__)
	ret = print_insn_i386 ((gsize) address, &this->info);
#elif defined(__ARM__)
	if (bfd_little_endian (this->main_bfd))
		ret = print_insn_littlearm ((gsize) address, &this->info);
	else
		ret = print_insn_bigarm ((gsize) address, &this->info);
#endif

	if (out_insn_size)
		*out_insn_size = ret;

	return g_strdup (this->disasm_buffer);
}

