#ifndef __CONNECTION_H__
#define __CONNECTION_H__

#include <server-object.h>

typedef enum {
	ERR_NONE = 0,

	ERR_UNKNOWN_ERROR,
	ERR_INTERNAL_ERROR,
	ERR_NO_TARGET,
	ERR_ALREADY_HAVE_TARGET,
	ERR_CANNOT_START_TARGET,
	ERR_NOT_STOPPED,
	ERR_ALREADY_STOPPED,
	ERR_RECURSIVE_CALL,
	ERR_NO_SUCH_BREAKPOINT,
	ERR_NO_SUCH_REGISTER,
	ERR_DR_OCCUPIED,
	ERR_MEMORY_ACCESS,
	ERR_NOT_IMPLEMENTED = 0x1002,
	ERR_IO_ERROR,
	ERR_NO_CALLBACK_FRAME,
	ERR_PERMISSION_DENIED,

	ERR_TARGET_ERROR_MASK = 0x0fff,

	ERR_NO_SUCH_INFERIOR = 0x1001,
	ERR_NO_SUCH_BPM = 0x1002,
	ERR_NO_SUCH_EXE_READER = 0x1003,
	ERR_CANNOT_OPEN_EXE = 0x1004,
} ErrorCode;

typedef enum {
	SERVER_EVENT_NONE,
	SERVER_EVENT_UNKNOWN_ERROR = 1,
	SERVER_EVENT_CHILD_EXITED = 2,
	SERVER_EVENT_CHILD_STOPPED,
	SERVER_EVENT_CHILD_SIGNALED,
	SERVER_EVENT_CHILD_CALLBACK,
	SERVER_EVENT_CHILD_CALLBACK_COMPLETED,
	SERVER_EVENT_CHILD_HIT_BREAKPOINT,
	SERVER_EVENT_CHILD_MEMORY_CHANGED,
	SERVER_EVENT_CHILD_CREATED_THREAD,
	SERVER_EVENT_CHILD_FORKED,
	SERVER_EVENT_CHILD_EXECD,
	SERVER_EVENT_CHILD_CALLED_EXIT,
	SERVER_EVENT_CHILD_NOTIFICATION,
	SERVER_EVENT_CHILD_INTERRUPTED,
	SERVER_EVENT_RUNTIME_INVOKE_DONE,
	SERVER_EVENT_INTERNAL_ERROR,

	SERVER_EVENT_DLL_LOADED = 0x41
} ServerEventType;

typedef struct {
	ServerEventType type;
	ServerObject *sender;
	guint64 arg;
	guint64 data1;
	guint64 data2;
	guint32 opt_data_size;
	gpointer opt_data;
} ServerEvent;

class MdbServer;
class MdbInferior;
class MdbExeReader;
class BreakpointManager;
class Buffer;

class Buffer
{
public:
	Buffer (int size);
	Buffer (const guint8 *data, int size);
	~Buffer (void);

public:
	void AddByte (guint8 val);
	void AddInt (guint32 val);
	void AddLong (guint64 l);
	void AddID (int id);
	void AddData (guint8 *data, int len);
	void AddString (const char *str);

	int DecodeByte (void);
	int DecodeInt (void);
	gint64 DecodeLong (void);
	int DecodeID (void);
	gchar* DecodeString (void);

protected:

	void MakeRoom (int size);

	int AdvanceOffset (int offset);
	int GetDataSize (void);
	guint8 *GetData (void);

private:
	guint8 *buf, *p, *end;

	friend class Connection;
};

class Connection
{
public:
	Connection (int conn_fd)
	{
		this->conn_fd = conn_fd;
	}


	void SendEvent (ServerEvent *e);

	bool Setup (void);

	bool HandleIncomingRequest (MdbServer *server);

protected:
	bool TransportSend (guint8 *data, int len);

	bool SendPacket (int command_set, int command, Buffer *data);
	bool SendReplyPacket (int id, int error, Buffer *data);

	ErrorCode ServerCommands (MdbServer *server, int command, int id, Buffer *in, Buffer *buf);
	ErrorCode InferiorCommands (MdbInferior *inferior, int command, int id, Buffer *in, Buffer *buf);
	ErrorCode BreakpointManagerCommands (BreakpointManager *bpm, int command, int id, Buffer *in, Buffer *buf);
	ErrorCode ExeReaderCommands (MdbExeReader *reader, int command, int id, Buffer *in, Buffer *buf);

private:
	int conn_fd;
};

#endif
