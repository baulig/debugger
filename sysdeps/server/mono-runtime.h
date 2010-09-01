#ifndef __MONO_RUNTIME_H__
#define __MONO_RUNTIME_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

class MonoRuntime : public ServerObject
{
public:
	static MonoRuntime *Initialize (MdbInferior *inferior, MdbExeReader *exe);

protected:
	MonoRuntime (MdbProcess *process, MdbExeReader *exe)
		: ServerObject (SERVER_OBJECT_KIND_MONO_RUNTIME)
	{
		this->process = process;
		this->exe = exe;
	}

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

private:
	MdbProcess *process;
	MdbExeReader *exe;
};

#endif
