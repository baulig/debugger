#ifndef __MONO_RUNTIME_H__
#define __MONO_RUNTIME_H__ 1

#include <mdb-inferior.h>
#include <mdb-exe-reader.h>

struct GenericInvocationData
{
	guint32 invocation_type;
	guint32 callback_id;

	guint64 method_address;
	guint64 arg1;
	guint64 arg2;
	guint64 arg3;

	guint32 data_size;
	guint32 dummy;

	gsize data_arg_ptr;
};

class MonoRuntime : public ServerObject
{
public:
	static MonoRuntime *Initialize (MdbInferior *inferior, MdbExeReader *exe);

	virtual gsize GetNotificationAddress (void) = 0;

	virtual gsize GetGenericInvocationFunc (void) = 0;

protected:
	MonoRuntime (MdbProcess *process, MdbExeReader *exe)
		: ServerObject (SERVER_OBJECT_KIND_MONO_RUNTIME)
	{
		this->process = process;
		this->exe = exe;
	}

private:
	MdbProcess *process;
	MdbExeReader *exe;
};

#endif
