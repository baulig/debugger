#ifndef __MDB_PROCESS_H__
#define __MDB_PROCESS_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

class MdbProcess : public ServerObject
{
public:
	virtual void InitializeProcess (MdbInferior *inferior) = 0;

	MdbExeReader *GetMainReader (void)
	{
		return main_reader;
	}

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

protected:
	MdbProcess (MdbServer *server)
		: ServerObject (SERVER_OBJECT_KIND_PROCESS)
	{
		this->server = server;
		this->main_reader = NULL;
	}

	MdbServer *server;
	MdbExeReader *main_reader;
};

#endif
