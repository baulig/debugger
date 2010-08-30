#include <mdb-exe-reader.h>

ErrorCode
MdbExeReader::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	switch (command) {
	case CMD_EXE_READER_GET_FILENAME: {
		out->AddString (filename);
		break;
	}

	case CMD_EXE_READER_GET_START_ADDRESS: {
		guint64 address;

		address = GetStartAddress ();
		out->AddLong (address);
		break;
	}

	case CMD_EXE_READER_LOOKUP_SYMBOL: {
		guint64 address;
		gchar *name;

		name = in->DecodeString ();
		address = LookupSymbol (name);
		out->AddLong (address);
		g_free (name);
		break;
	}

	case CMD_EXE_READER_GET_TARGET_NAME:
		out->AddString (GetTargetName ());
		break;

	case CMD_EXE_READER_HAS_SECTION: {
		gchar *section_name;
		gboolean has_section;

		section_name = in->DecodeString ();
		has_section = HasSection (section_name);
		out->AddByte (has_section ? 1 : 0);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_ADDRESS: {
		gchar *section_name;
		guint64 address;

		section_name = in->DecodeString ();
		address = GetSectionAddress (section_name);
		out->AddLong (address);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_CONTENTS: {
		gchar *section_name;
		gpointer contents;
		guint32 size;

		section_name = in->DecodeString ();

		contents = GetSectionContents (section_name, &size);
		out->AddInt (size);

		if (contents) {
			out->AddData ((guint8 *) contents, size);
			g_free (contents);
		}

		g_free (section_name);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

