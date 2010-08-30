#ifndef __SERVER_OBJECT_H__
#define __SERVER_OBJECT_H__

#include <connection.h>
#include <debugger-mutex.h>

enum ServerObjectKind {
	SERVER_OBJECT_KIND_NONE = 0,
	SERVER_OBJECT_KIND_SERVER,
	SERVER_OBJECT_KIND_INFERIOR,
	SERVER_OBJECT_KIND_PROCESS,
	SERVER_OBJECT_KIND_EXE_READER,
	SERVER_OBJECT_KIND_BREAKPOINT_MANAGER
};

class ServerObject
{
public:
	ServerObjectKind GetObjectKind (void) { return kind; }
	int GetID (void) { return iid; }

	static void Initialize (void);

	static void Lock ();

	static void Unlock ();

	static ServerObject *GetObjectByID (int id);

	static ServerObject *GetObjectByID (int id, ServerObjectKind kind);

	virtual ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out) = 0;

protected:
	ServerObject (ServerObjectKind kind);

	~ServerObject (void);

private:
	int iid;
	ServerObjectKind kind;

	static int next_iid;
	static GHashTable *object_hash;

	static DebuggerMutex *lock_mutex;
};

#endif
