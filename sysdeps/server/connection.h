#ifndef __CONNECTION_H__
#define __CONNECTION_H__

#include <config.h>
#include <glib.h>

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
	ERR_NOT_IMPLEMENTED,
	ERR_IO_ERROR,
	ERR_NO_CALLBACK_FRAME,
	ERR_PERMISSION_DENIED,

	ERR_INFERIOR_EXITED,
	ERR_NO_MONO_RUNTIME,
	ERR_NO_CODE_BUFFER,

	ERR_TARGET_ERROR_MASK = 0x0fff,

	ERR_NO_SUCH_INFERIOR = 0x1001,
	ERR_NO_SUCH_BPM = 0x1002,
	ERR_NO_SUCH_EXE_READER = 0x1003,
	ERR_CANNOT_OPEN_EXE = 0x1004,
	ERR_NO_SUCH_PROCESS = 0x1005,
	ERR_NO_SUCH_MONO_RUNTIME = 0x1006,
} ErrorCode;

typedef enum {
	CMD_SET_SERVER = 1,
	CMD_SET_INFERIOR = 2,
	CMD_SET_EVENT = 3,
	CMD_SET_BPM = 4,
	CMD_SET_EXE_READER = 5,
	CMD_SET_PROCESS = 6,
	CMD_SET_MONO_RUNTIME = 7
} CommandSet;

typedef enum {
	CMD_SERVER_GET_TARGET_INFO = 1,
	CMD_SERVER_GET_SERVER_TYPE = 2,
	CMD_SERVER_GET_ARCH_TYPE = 3,
	CMD_SERVER_GET_CAPABILITIES = 4,
	CMD_SERVER_GET_BPM = 5,
	CMD_SERVER_CREATE_PROCESS = 6
} CmdServer;

typedef enum {
	CMD_INFERIOR_GET_SIGNAL_INFO = 3,
	CMD_INFERIOR_GET_APPLICATION = 4,
	CMD_INFERIOR_GET_FRAME = 5,
	CMD_INFERIOR_INSERT_BREAKPOINT = 6,
	CMD_INFERIOR_ENABLE_BREAKPOINT = 7,
	CMD_INFERIOR_DISABLE_BREAKPOINT = 8,
	CMD_INFERIOR_REMOVE_BREAKPOINT = 9,
	CMD_INFERIOR_STEP = 10,
	CMD_INFERIOR_CONTINUE = 11,
	CMD_INFERIOR_RESUME_STEPPING = 12,
	CMD_INFERIOR_GET_REGISTERS = 13,
	CMD_INFERIOR_SET_REGISTERS = 14,
	CMD_INFERIOR_READ_MEMORY = 15,
	CMD_INFERIOR_WRITE_MEMORY = 16,
	CMD_INFERIOR_GET_PENDING_SIGNAL = 17,
	CMD_INFERIOR_SET_SIGNAL = 18,
	CMD_INFERIOR_DISASSEMBLE_INSN = 19,
	CMD_INFERIOR_STOP = 20,
	CMD_INFERIOR_CALL_METHOD = 21
} CmdInferior;

typedef enum {
	CMD_BPM_LOOKUP_BY_ADDR = 1,
	CMD_BPM_LOOKUP_BY_ID = 2,
} CmdBpm;

typedef enum {
	CMD_EXE_READER_GET_FILENAME = 1,
	CMD_EXE_READER_GET_START_ADDRESS = 2,
	CMD_EXE_READER_LOOKUP_SYMBOL = 3,
	CMD_EXE_READER_GET_TARGET_NAME = 4,
	CMD_EXE_READER_HAS_SECTION = 5,
	CMD_EXE_READER_GET_SECTION_ADDRESS = 6,
	CMD_EXE_READER_GET_SECTION_CONTENTS = 7
} CmdExeReader;

typedef enum {
	CMD_PROCESS_GET_MAIN_READER = 1,
	CMD_PROCESS_INITIALIZE_PROCESS = 2,
	CMD_PROCESS_SPAWN = 3,
	CMD_PROCESS_SUSPEND = 4,
	CMD_PROCESS_RESUME = 5
} CmdProcess;

typedef enum {
	CMD_MONO_RUNTIME_GET_DEBUGGER_INFO = 1,
	CMD_MONO_RUNTIME_SET_EXTENDED_NOTIFICATIONS = 2,
	CMD_MONO_RUNTIME_EXECUTE_INSTRUCTION = 3
} CmdMonoRuntime;

typedef enum {
	CMD_COMPOSITE = 100
} CmdComposite;

typedef enum {
	SERVER_EVENT_NONE = 0,
	SERVER_EVENT_EXITED,
	SERVER_EVENT_STOPPED,
	SERVER_EVENT_SIGNALED,
	SERVER_EVENT_CALLBACK,
	SERVER_EVENT_CALLBACK_COMPLETED,
	SERVER_EVENT_BREAKPOINT,
	SERVER_EVENT_MEMORY_CHANGED,
	SERVER_EVENT_THREAD_CREATED,
	SERVER_EVENT_FORKED,
	SERVER_EVENT_EXECD,
	SERVER_EVENT_CALLED_EXIT,
	SERVER_EVENT_NOTIFICATION,
	SERVER_EVENT_INTERRUPTED,
	SERVER_EVENT_RUNTIME_INVOKE_DONE,

	SERVER_EVENT_UNKNOWN_ERROR = 0x40,
	SERVER_EVENT_INTERNAL_ERROR,

	SERVER_EVENT_MAIN_MODULE_LOADED = 0x50,
	SERVER_EVENT_DLL_LOADED,
	SERVER_EVENT_MONO_RUNTIME_LOADED
} ServerEventType;

class ServerObject;
class MdbServer;
class MdbInferior;
class MdbExeReader;
class BreakpointManager;

typedef struct {
	ServerEventType type;
	ServerObject *sender;
	ServerObject *arg_object;
	guint64 arg;
	guint64 data1;
	guint64 data2;
	guint32 opt_data_size;
	gpointer opt_data;
} ServerEvent;

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
	void AddData (const guint8 *data, int len);
	void AddString (const char *str);

	int DecodeByte (void);
	int DecodeInt (void);
	gint64 DecodeLong (void);
	int DecodeID (void);
	gchar* DecodeString (void);
	const guint8 *DecodeBuffer (int size);

	const guint8 *GetData (void);
	int GetDataSize (void);

protected:
	void MakeRoom (int size);

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
	bool TransportSend (const guint8 *data, int len);

	bool SendPacket (int command_set, int command, Buffer *data);
	bool SendReplyPacket (int id, int error, Buffer *data);

private:
	int conn_fd;
	static volatile int packet_id;

};

#endif
