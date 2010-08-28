#ifndef __MDB_PROCESS_H__
#define __MDB_PROCESS_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

class MdbProcess
{
public:
	virtual bool Initialize (void) = 0;

protected:
	MdbProcess (MdbInferior *inferior)
	{
		this->inferior = inferior;
		this->main_reader = NULL;
	}

	MdbInferior *inferior;
	MdbExeReader *main_reader;
};

#endif
