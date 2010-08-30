#ifndef __MDB_PROCESS_H__
#define __MDB_PROCESS_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

class MdbProcess : public ServerObject
{
public:
	virtual bool Initialize (void) = 0;

	virtual void InitializeProcess (void) = 0;

	MdbExeReader *GetMainReader (void)
	{
		return main_reader;
	}

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

protected:
	MdbProcess (MdbInferior *inferior)
		: ServerObject (SERVER_OBJECT_KIND_PROCESS)
	{
		this->inferior = inferior;
		this->main_reader = NULL;
	}

	MdbInferior *inferior;
	MdbExeReader *main_reader;
};

#endif
