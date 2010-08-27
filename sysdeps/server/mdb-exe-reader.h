#ifndef __MDB_EXE_READER_H__
#define __MDB_EXE_READER_H__

#include <mdb-inferior.h>

class MdbDisassembler;

class MdbExeReader {
protected:
	const char *filename;
	MdbExeReader (const char *filename)
	{
		this->filename = filename;
	}

public:
	const char *GetFileName (void)
	{
		return filename;
	}

	virtual guint64 GetStartAddress (void) = 0;
	virtual guint64 LookupSymbol (const char *name) = 0;
	virtual const char *GetTargetName (void) = 0;
	virtual gboolean HasSection (const char *name) = 0;
	virtual guint64 GetSectionAddress (const char *name) = 0;
	virtual gpointer GetSectionContents (const char *name, guint32 *out_size) = 0;
	virtual const char *LookupSymbol (guint64 address) = 0;

	virtual MdbDisassembler *GetDisassembler (MdbInferior *inferior) = 0;
};

class MdbDisassembler
{
public:
	virtual gchar *DisassembleInstruction (guint64 address, guint32 *out_insn_size) = 0;
protected:
	MdbInferior *inferior;
};

extern MdbExeReader *mdb_server_create_exe_reader (const char *filename);

#endif