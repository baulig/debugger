#include <config.h>
#include <mdb-server.h>
#include <mdb-inferior.h>
#include <mdb-exe-reader.h>
#include <debugger-mutex.h>
#include <errno.h>
#include <stdlib.h>
#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif
#include <string.h>
#ifdef HAVE_SYS_TYPES_H
#include <sys/types.h>
#endif
#ifdef HAVE_SYS_SELECT_H
#include <sys/select.h>
#endif
#ifdef HAVE_SYS_SOCKET_H
#include <sys/socket.h>
#endif
#ifdef HAVE_NETINET_TCP_H
#include <netinet/tcp.h>
#endif
#ifdef HAVE_NETINET_IN_H
#include <netinet/in.h>
#endif
#ifdef HAVE_NETDB_H
#include <netdb.h>
#endif
#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif

#ifdef WINDOWS
#include <winsock2.h>
#include <ws2tcpip.h>
#ifdef __GNUC__
/* cygwin's headers do not seem to define these */
void WSAAPI freeaddrinfo (struct addrinfo*);
int WSAAPI getaddrinfo (const char*,const char*,const struct addrinfo*,
                        struct addrinfo**);
int WSAAPI getnameinfo(const struct sockaddr*,socklen_t,char*,DWORD,
                       char*,DWORD,int);
#endif
#endif

static const char *handshake_msg = "9da91832-87f3-4cde-a92f-6384fec6536e";

#define HEADER_LENGTH 11

#define MAJOR_VERSION 1
#define MINOR_VERSION

typedef enum {
	CMD_SET_SERVER = 1,
	CMD_SET_INFERIOR = 2,
	CMD_SET_EVENT = 3,
	CMD_SET_BPM = 4,
	CMD_SET_EXE_READER = 5
} CommandSet;

typedef enum {
	CMD_SERVER_GET_TARGET_INFO = 1,
	CMD_SERVER_GET_SERVER_TYPE = 2,
	CMD_SERVER_GET_ARCH_TYPE = 3,
	CMD_SERVER_GET_CAPABILITIES = 4,
	CMD_SERVER_CREATE_INFERIOR = 5,
	CMD_SERVER_CREATE_BPM = 6,
	CMD_SERVER_CREATE_EXE_READER = 7
} CmdServer;

typedef enum {
	CMD_INFERIOR_SPAWN = 1,
	CMD_INFERIOR_INIT_PROCESS = 2,
	CMD_INFERIOR_GET_SIGNAL_INFO = 3,
	CMD_INFERIOR_GET_APPLICATION = 4,
	CMD_INFERIOR_GET_FRAME = 5,
	CMD_INFERIOR_INSERT_BREAKPOINT = 6,
	CMD_INFERIOR_ENABLE_BREAKPOINT = 7,
	CMD_INFERIOR_DISABLE_BREAKPOINT = 8,
	CMD_INFERIOR_REMOVE_BREAKPOINT = 9,
	CMD_INFERIOR_STEP = 10,
	CMD_INFERIOR_CONTINUE = 11,
	CMD_INFERIOR_RESUME = 12,
	CMD_INFERIOR_GET_REGISTERS = 13,
	CMD_INFERIOR_READ_MEMORY = 14,
	CMD_INFERIOR_WRITE_MEMORY = 15,
	CMD_INFERIOR_GET_PENDING_SIGNAL = 16,
	CMD_INFERIOR_SET_SIGNAL = 17,
	CMD_INFERIOR_INIT_AT_ENTRYPOINT = 18,
	CMD_INFERIOR_DISASSEMBLE_INSN = 19
} CmdInferior;

typedef enum {
	CMD_BPM_LOOKUP_BY_ADDR = 1,
	CMD_BPM_LOOKUP_BY_ID = 2,
} CmdBpm;

typedef enum {
	CMD_EXE_READER_GET_START_ADDRESS = 1,
	CMD_EXE_READER_LOOKUP_SYMBOL = 2,
	CMD_EXE_READER_GET_TARGET_NAME = 3,
	CMD_EXE_READER_HAS_SECTION = 4,
	CMD_EXE_READER_GET_SECTION_ADDRESS = 5,
	CMD_EXE_READER_GET_SECTION_CONTENTS = 6
} CmdExeReader;

typedef enum {
	CMD_COMPOSITE = 100
} CmdComposite;

volatile static int packet_id = 0;
static int conn_fd = 0;

static int next_unique_id = 0;
static GHashTable *inferior_hash = NULL;
static GHashTable *inferior_by_pid = NULL;
static GHashTable *bpm_hash = NULL;
static GHashTable *exe_reader_hash = NULL;

static DebuggerMutex *main_mutex;

/*
 * recv_length:
 *
 * recv() + handle incomplete reads and EINTR
 */
static int
recv_length (int fd, void *buf, int len, int flags)
{
	int res;
	int total = 0;

	do {
		res = recv (fd, (char *) buf + total, len - total, flags);
		if (res > 0)
			total += res;
	} while ((res > 0 && total < len) || (res == -1 && errno == EINTR));
	return total;
}

static gboolean
transport_send (guint8 *data, int len)
{
	int res;

	do {
		res = send (conn_fd, (const char *) data, len, 0);
	} while (res == -1 && errno == EINTR);
	if (res != len)
		return FALSE;
	else
		return TRUE;
}

/*
 * Functions to decode protocol data
 */

static inline int
decode_byte (guint8 *buf, guint8 **endbuf, guint8 *limit)
{
	*endbuf = buf + 1;
	g_assert (*endbuf <= limit);
	return buf [0];
}

static inline int
decode_int (guint8 *buf, guint8 **endbuf, guint8 *limit)
{
	*endbuf = buf + 4;
	g_assert (*endbuf <= limit);

	return (((int)buf [0]) << 24) | (((int)buf [1]) << 16) | (((int)buf [2]) << 8) | (((int)buf [3]) << 0);
}

static inline gint64
decode_long (guint8 *buf, guint8 **endbuf, guint8 *limit)
{
	guint32 high = decode_int (buf, &buf, limit);
	guint32 low = decode_int (buf, &buf, limit);

	*endbuf = buf;

	return ((((guint64)high) << 32) | ((guint64)low));
}

static inline int
decode_id (guint8 *buf, guint8 **endbuf, guint8 *limit)
{
	return decode_int (buf, endbuf, limit);
}

static inline char*
decode_string (guint8 *buf, guint8 **endbuf, guint8 *limit)
{
	int len = decode_int (buf, &buf, limit);
	char *s;

	s = (char *) g_malloc0 (len + 1);
	g_assert (s);

	memcpy (s, buf, len);
	s [len] = '\0';
	buf += len;
	*endbuf = buf;

	return s;
}

/*
 * Functions to encode protocol data
 */

typedef struct {
	guint8 *buf, *p, *end;
} Buffer;

static inline void
buffer_init (Buffer *buf, int size)
{
	buf->buf = (guint8 *) g_malloc (size);
	buf->p = buf->buf;
	buf->end = buf->buf + size;
}

static inline void
buffer_make_room (Buffer *buf, int size)
{
	if (buf->end - buf->p < size) {
		int new_size = buf->end - buf->buf + size + 32;
		guint8 *p = (guint8 *) g_realloc (buf->buf, new_size);
		size = buf->p - buf->buf;
		buf->buf = p;
		buf->p = p + size;
		buf->end = buf->buf + new_size;
	}
}

static inline void
buffer_add_byte (Buffer *buf, guint8 val)
{
	buffer_make_room (buf, 1);
	buf->p [0] = val;
	buf->p++;
}

static inline void
buffer_add_int (Buffer *buf, guint32 val)
{
	buffer_make_room (buf, 4);
	buf->p [0] = (val >> 24) & 0xff;
	buf->p [1] = (val >> 16) & 0xff;
	buf->p [2] = (val >> 8) & 0xff;
	buf->p [3] = (val >> 0) & 0xff;
	buf->p += 4;
}

static inline void
buffer_add_long (Buffer *buf, guint64 l)
{
	buffer_add_int (buf, (l >> 32) & 0xffffffff);
	buffer_add_int (buf, (l >> 0) & 0xffffffff);
}

static inline void
buffer_add_id (Buffer *buf, int id)
{
	buffer_add_int (buf, (guint64)id);
}

static inline void
buffer_add_data (Buffer *buf, guint8 *data, int len)
{
	buffer_make_room (buf, len);
	memcpy (buf->p, data, len);
	buf->p += len;
}

static inline void
buffer_add_string (Buffer *buf, const char *str)
{
	int len;

	if (str == NULL) {
		buffer_add_int (buf, 0);
	} else {
		len = strlen (str);
		buffer_add_int (buf, len);
		buffer_add_data (buf, (guint8*)str, len);
	}
}

static inline void
buffer_free (Buffer *buf)
{
	g_free (buf->buf);
}

static gboolean
send_packet (int command_set, int command, Buffer *data)
{
	Buffer buf;
	int len, id;
	gboolean res;

	id = ++packet_id;

	len = data->p - data->buf + 11;
	buffer_init (&buf, len);
	buffer_add_int (&buf, len);
	buffer_add_int (&buf, id);
	buffer_add_byte (&buf, 0); /* flags */
	buffer_add_byte (&buf, command_set);
	buffer_add_byte (&buf, command);
	memcpy (buf.buf + 11, data->buf, data->p - data->buf);

	res = transport_send (buf.buf, len);

	buffer_free (&buf);

	return res;
}

static gboolean
send_reply_packet (int id, int error, Buffer *data)
{
	Buffer buf;
	int len;
	gboolean res;

	len = data->p - data->buf + 11;
	buffer_init (&buf, len);
	buffer_add_int (&buf, len);
	buffer_add_int (&buf, id);
	buffer_add_byte (&buf, 0x80); /* flags */
	buffer_add_byte (&buf, (error >> 8) & 0xff);
	buffer_add_byte (&buf, error);
	memcpy (buf.buf + 11, data->buf, data->p - data->buf);

	res = transport_send (buf.buf, len);

	buffer_free (&buf);

	return res;
}

MdbInferior *
MdbServer::GetInferiorByPid (int pid)
{
	return (MdbInferior *) g_hash_table_lookup (inferior_by_pid, GUINT_TO_POINTER (pid));
}

void
MdbServer::ProcessChildEvent (ServerEvent *e)
{
	Buffer buf;

	buffer_init (&buf, 128 + e->opt_data_size);
	buffer_add_byte (&buf, EVENT_KIND_TARGET_EVENT);
	buffer_add_int (&buf, e->sender_iid);
	buffer_add_byte (&buf, e->type);
	buffer_add_long (&buf, e->arg);
	buffer_add_long (&buf, e->data1);
	buffer_add_long (&buf, e->data2);
	buffer_add_int (&buf, e->opt_data_size);
	if (e->opt_data)
		buffer_add_data (&buf, (guint8 *) e->opt_data, e->opt_data_size);

	debugger_mutex_lock (main_mutex);
	send_packet (CMD_SET_EVENT, CMD_COMPOSITE, &buf);
	debugger_mutex_unlock (main_mutex);

	buffer_free (&buf);
}

int
main (int argc, char *argv[])
{
	MdbServer *server;
	struct sockaddr_in serv_addr, cli_addr;
	int fd, res;
	socklen_t cli_len;
	char buf[128];

	inferior_hash = g_hash_table_new (NULL, NULL);
	inferior_by_pid = g_hash_table_new (NULL, NULL);
	bpm_hash = g_hash_table_new (NULL, NULL);
	exe_reader_hash = g_hash_table_new (NULL, NULL);

	main_mutex = debugger_mutex_new ();

	if (!MdbServer::Initialize ()) {
		g_warning (G_STRLOC ": Failed to initialize OS backend.");
		exit (-1);
	}

	MdbInferior::Initialize ();

	fd = socket (AF_INET, SOCK_STREAM, 0);
#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": %d", fd);
#endif

	memset (&serv_addr, 0, sizeof (serv_addr));

	serv_addr.sin_family = AF_INET;
	serv_addr.sin_addr.s_addr = INADDR_ANY;
	serv_addr.sin_port = htons (8888);

	res = bind (fd, (struct sockaddr *) &serv_addr, sizeof (serv_addr));
	if (res < 0) {
		g_warning (G_STRLOC ": bind() failed: %s", g_strerror (errno));
		exit (-1);
	}

	listen (fd, 1);

	cli_len = sizeof (cli_addr);

	conn_fd = accept (fd, (struct sockaddr *) &cli_addr, &cli_len);
	if (res < 0) {
		g_warning (G_STRLOC ": accept() failed: %s", g_strerror (errno));
		exit (-1);
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": accepted!");
#endif

	/* Write handshake message */
	do {
		res = send (conn_fd, handshake_msg, strlen (handshake_msg), 0);
	} while (res == -1 && errno == EINTR);

	if (res < 0) {
		g_error (G_STRLOC ": Handshake failed!");
		exit (-1);
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": sent handshake");
#endif

	/* Read answer */
	res = recv_length (conn_fd, buf, strlen (handshake_msg), 0);
	if ((res != strlen (handshake_msg)) || (memcmp (buf, handshake_msg, strlen (handshake_msg) != 0))) {
		g_error (G_STRLOC ": Handshake failed!");
		exit (-1);
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": handshake ok");
#else
	g_message (G_STRLOC ": waiting for incoming connections");
#endif

	/* 
	 * Set TCP_NODELAY on the socket so the client receives events/command
	 * results immediately.
	 */
	{
		int flag = 1;
		int result = setsockopt (conn_fd, IPPROTO_TCP, TCP_NODELAY, (char *) &flag, sizeof (int));
		g_assert (result >= 0);
		flag = 1;
		result = setsockopt (conn_fd, SOL_SOCKET, SO_REUSEADDR, (char *) &flag, sizeof (int));
		flag = 1;
	}

	BreakpointManager::Initialize ();

	server = new MdbServer (conn_fd);

	server->MainLoop ();

#if WINDOWS
	shutdown (fd, SD_BOTH);
	shutdown (conn_fd, SD_BOTH);

	closesocket (fd);
	closesocket (conn_fd);
#else
	shutdown (fd, SHUT_RDWR);
	shutdown (conn_fd, SHUT_RDWR);

	close (fd);
	close (conn_fd);
#endif

	exit (0);
}

static ErrorCode
server_commands (MdbServer *server, int command, int id, guint8 *p, guint8 *end, Buffer *buf)
{
	switch (command) {
	case CMD_SERVER_GET_TARGET_INFO: {
		guint32 int_size, long_size, addr_size, is_bigendian;
		ErrorCode result;

		result = MdbInferior::GetTargetInfo (&int_size, &long_size, &addr_size, &is_bigendian);
		if (result)
			return result;

		buffer_add_int (buf, int_size);
		buffer_add_int (buf, long_size);
		buffer_add_int (buf, addr_size);
		buffer_add_byte (buf, is_bigendian != 0);
		break;
	}

	case CMD_SERVER_GET_SERVER_TYPE: {
		buffer_add_int (buf, MdbInferior::GetServerType ());
		break;
	}

	case CMD_SERVER_GET_ARCH_TYPE: {
		buffer_add_int (buf, MdbInferior::GetArchType ());
		break;
	}

	case CMD_SERVER_GET_CAPABILITIES: {
		buffer_add_int (buf, MdbInferior::GetCapabilities ());
		break;
	}

	case CMD_SERVER_CREATE_INFERIOR: {
		MdbInferior *inferior;
		BreakpointManager *bpm;
		int bpm_iid;

		bpm_iid = decode_int (p, &p, end);

		bpm = (BreakpointManager *) g_hash_table_lookup (bpm_hash, GUINT_TO_POINTER (bpm_iid));

		g_message (G_STRLOC ": create inferior: %d - %p", bpm_iid, bpm);

		if (!bpm)
			return ERR_NO_SUCH_BPM;

		inferior = mdb_inferior_new (server, bpm);

		g_hash_table_insert (inferior_hash, GUINT_TO_POINTER (inferior->GetID ()), inferior);

		buffer_add_int (buf, inferior->GetID ());
		break;
	}

	case CMD_SERVER_CREATE_BPM: {
		BreakpointManager *bpm;
		int iid;

		iid = ++next_unique_id;
		bpm = new BreakpointManager ();
		g_hash_table_insert (bpm_hash, GUINT_TO_POINTER (iid), bpm);

		g_message (G_STRLOC ": new bpm: %d - %p", iid, bpm);

		buffer_add_int (buf, iid);
		break;
	}

	case CMD_SERVER_CREATE_EXE_READER: {
		MdbExeReader *reader;
		gchar *filename;
		int iid;

		filename = decode_string (p, &p, end);
		g_message (G_STRLOC ": create exe reader - %s", filename);

		reader = server->GetExeReader (filename);
		if (!reader)
			return ERR_CANNOT_OPEN_EXE;

		iid = ++next_unique_id;
		g_hash_table_insert (exe_reader_hash, GUINT_TO_POINTER (iid), reader);

		g_message (G_STRLOC ": exe reader: %s - %d - %p", filename, iid, reader);

		buffer_add_int (buf, iid);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

static ErrorCode
inferior_commands (int command, int id, MdbInferior *inferior, guint8 *p, guint8 *end, Buffer *buf)
{
	ErrorCode result = ERR_NONE;

	switch (command) {
	case CMD_INFERIOR_SPAWN: {
		char *cwd, **argv, *error;
		int argc, i, child_pid;

		cwd = decode_string (p, &p, end);
		argc = decode_int (p, &p, end);

		argv = g_new0 (char *, argc + 1);
		for (i = 0; i < argc; i++)
			argv [i] = decode_string (p, &p, end);
		argv [argc] = NULL;

		if (!*cwd) {
			g_free (cwd);
			cwd = g_get_current_dir ();
		}

		result = inferior->Spawn (cwd, (const gchar **) argv, NULL, &child_pid, &error);
		if (result)
			return result;

		g_hash_table_insert (inferior_by_pid, GUINT_TO_POINTER (child_pid), inferior);

		buffer_add_int (buf, child_pid);

		g_free (cwd);
		for (i = 0; i < argc; i++)
			g_free (argv [i]);
		g_free (argv);
		break;
	}

	case CMD_INFERIOR_INIT_PROCESS:
		result = inferior->InitializeProcess ();
		break;

	case CMD_INFERIOR_GET_SIGNAL_INFO: {
		SignalInfo *sinfo;

		result = inferior->GetSignalInfo (&sinfo);
		if (result)
			return result;

		buffer_add_int (buf, sinfo->sigkill);
		buffer_add_int (buf, sinfo->sigstop);
		buffer_add_int (buf, sinfo->sigint);
		buffer_add_int (buf, sinfo->sigchld);
		buffer_add_int (buf, sinfo->sigfpe);
		buffer_add_int (buf, sinfo->sigquit);
		buffer_add_int (buf, sinfo->sigabrt);
		buffer_add_int (buf, sinfo->sigsegv);
		buffer_add_int (buf, sinfo->sigill);
		buffer_add_int (buf, sinfo->sigbus);
		buffer_add_int (buf, sinfo->sigwinch);
		buffer_add_int (buf, sinfo->kernel_sigrtmin);

		g_free (sinfo);
		break;
	}

	case CMD_INFERIOR_GET_APPLICATION: {
		gchar *exe_file = NULL, *cwd = NULL, **cmdline_args = NULL;
		guint32 nargs, i;

		result = inferior->GetApplication (&exe_file, &cwd, &nargs, &cmdline_args);
		if (result)
			return result;

		buffer_add_string (buf, exe_file);
		buffer_add_string (buf, cwd);

		buffer_add_int (buf, nargs);
		for (i = 0; i < nargs; i++)
			buffer_add_string (buf, cmdline_args [i]);

		g_free (exe_file);
		g_free (cwd);
		g_free (cmdline_args);
		break;
	}

	case CMD_INFERIOR_GET_FRAME: {
		StackFrame frame;

		result = inferior->GetFrame (&frame);
		if (result)
			return result;

		buffer_add_long (buf, frame.address);
		buffer_add_long (buf, frame.stack_pointer);
		buffer_add_long (buf, frame.frame_address);
		break;
	}

	case CMD_INFERIOR_STEP:
		result = inferior->Step ();
		break;

	case CMD_INFERIOR_CONTINUE:
		result = inferior->Continue ();
		break;

	case CMD_INFERIOR_RESUME:
		result = inferior->Resume ();
		break;

	case CMD_INFERIOR_INSERT_BREAKPOINT: {
		guint64 address;
		BreakpointInfo *breakpoint;

		address = decode_long (p, &p, end);

		result = inferior->InsertBreakpoint (address, &breakpoint);
		if (result == ERR_NONE)
			buffer_add_int (buf, breakpoint->id);
		break;
	}

	case CMD_INFERIOR_ENABLE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = decode_int (p, &p, end);
		breakpoint = inferior->LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = inferior->EnableBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_DISABLE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = decode_int (p, &p, end);
		breakpoint = inferior->LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = inferior->DisableBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_REMOVE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = decode_int (p, &p, end);
		breakpoint = inferior->LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = inferior->RemoveBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_GET_REGISTERS: {
		guint32 count, i;
		guint64 *regs;

		result = inferior->GetRegisterCount (&count);
		if (result)
			return result;

		regs = g_new0 (guint64, count);

		result = inferior->GetRegisters (regs);
		if (result) {
			g_free (regs);
			return result;
		}

		buffer_add_int (buf, count);
		for (i = 0; i < count; i++)
			buffer_add_long (buf, regs [i]);

		g_free (regs);
		break;
	}

	case CMD_INFERIOR_READ_MEMORY: {
		guint64 address;
		guint32 size;

		address = decode_long (p, &p, end);
		size = decode_int (p, &p, end);

		buffer_make_room (buf, size);

		result = inferior->ReadMemory (address, size, buf->p);
		if (result == ERR_NONE)
			buf->p += size;

		break;
	}

	case CMD_INFERIOR_WRITE_MEMORY: {
		guint64 address;
		guint32 size;

		address = decode_long (p, &p, end);
		size = decode_int (p, &p, end);

		result = inferior->WriteMemory (address, size, p);
		break;
	}

	case CMD_INFERIOR_GET_PENDING_SIGNAL: {
		guint32 sig;

		result = inferior->GetPendingSignal (&sig);
		if (result == ERR_NONE)
			buffer_add_int (buf, sig);

		break;
	}

	case CMD_INFERIOR_SET_SIGNAL: {
		guint32 sig, send_it;

		sig = decode_int (p, &p, end);
		send_it = decode_byte (p, &p, end);

		result = inferior->SetSignal (sig, send_it);
		break;
	}

	case CMD_INFERIOR_INIT_AT_ENTRYPOINT:
		inferior->GetProcess ()->Initialize ();
		break;

	case CMD_INFERIOR_DISASSEMBLE_INSN: {
		guint64 address;
		guint32 insn_size;
		gchar *insn;

		address = decode_long (p, &p, end);

		insn = inferior->DisassembleInstruction (address, &insn_size);
		buffer_add_int (buf, insn_size);
		buffer_add_string (buf, insn);
		g_free (insn);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return result;
}

static ErrorCode
bpm_commands (int command, int id, BreakpointManager *bpm, guint8 *p, guint8 *end, Buffer *buf)
{
	switch (command) {
	case CMD_BPM_LOOKUP_BY_ADDR: {
		guint64 address;
		BreakpointInfo *info;

		address = decode_long (p, &p, end);

		info = bpm->Lookup (address);

		if (info) {
			buffer_add_int (buf, info->id);
			buffer_add_byte (buf, info->enabled ? 1 : 0);
		} else {
			buffer_add_int (buf, 0);
			buffer_add_byte (buf, 0);
		}
		break;
	}

	case CMD_BPM_LOOKUP_BY_ID: {
		BreakpointInfo *info;
		guint32 idx;

		idx = decode_int (p, &p, end);

		info = bpm->LookupById (idx);

		if (!info) {
			buffer_add_byte (buf, 0);
			break;
		}

		buffer_add_byte (buf, 1);
		buffer_add_byte (buf, info->enabled ? 1 : 0);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

static ErrorCode
exe_reader_commands (int command, int id, MdbExeReader *reader, guint8 *p, guint8 *end, Buffer *buf)
{
	switch (command) {
	case CMD_EXE_READER_GET_START_ADDRESS: {
		guint64 address;

		address = reader->GetStartAddress ();
		buffer_add_long (buf, address);
		break;
	}

	case CMD_EXE_READER_LOOKUP_SYMBOL: {
		guint64 address;
		gchar *name;

		name = decode_string (p, &p, end);
		address = reader->LookupSymbol (name);
		buffer_add_long (buf, address);
		g_free (name);
		break;
	}

	case CMD_EXE_READER_GET_TARGET_NAME:
		buffer_add_string (buf, reader->GetTargetName ());
		break;

	case CMD_EXE_READER_HAS_SECTION: {
		gchar *section_name;
		gboolean has_section;

		section_name = decode_string (p, &p, end);
		has_section = reader->HasSection (section_name);
		buffer_add_byte (buf, has_section ? 1 : 0);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_ADDRESS: {
		gchar *section_name;
		guint64 address;

		section_name = decode_string (p, &p, end);
		address = reader->GetSectionAddress (section_name);
		buffer_add_long (buf, address);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_CONTENTS: {
		gchar *section_name;
		gpointer contents;
		guint32 size;

		section_name = decode_string (p, &p, end);

		contents = reader->GetSectionContents (section_name, &size);
		buffer_add_int (buf, size);

		if (contents) {
			buffer_add_data (buf, (guint8 *) contents, size);
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

#if WINDOWS

typedef struct {
	int command;
	int id;
	MdbInferior *inferior;
	guint8 *p;
	guint8 *end;
	Buffer *buf;
	ErrorCode ret;
} InferiorData;

static void
inferior_command_proxy (gpointer user_data)
{
	InferiorData *data = (InferiorData *) user_data;

	data->ret = inferior_commands (data->command, data->id, data->inferior, data->p, data->end, data->buf);
}

#endif

gboolean
MdbServer::MainLoopIteration (void)
{
	guint8 header [HEADER_LENGTH];
	int res, len, id, flags, command_set, command;
	guint8 *data, *p, *end;
	Buffer buf;
	ErrorCode err;
	gboolean no_reply;

	res = recv_length (conn_fd, header, HEADER_LENGTH, 0);

	/* This will break if the socket is closed during shutdown too */
	if (res != HEADER_LENGTH)
		return FALSE;

	p = header;
	end = header + HEADER_LENGTH;

	len = decode_int (p, &p, end);
	id = decode_int (p, &p, end);
	flags = decode_byte (p, &p, end);
	command_set = decode_byte (p, &p, end);
	command = decode_byte (p, &p, end);

	g_assert (flags == 0);

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": Received command %d/%d, id=%d.", command_set, command, id);
#endif

	data = (guint8 *) g_malloc (len - HEADER_LENGTH);
	if (len - HEADER_LENGTH > 0) {
		res = recv_length (conn_fd, data, len - HEADER_LENGTH, 0);
		if (res != len - HEADER_LENGTH)
			return FALSE;
	}

	p = data;
	end = data + (len - HEADER_LENGTH);

	buffer_init (&buf, 128);

	err = ERR_NONE;
	no_reply = FALSE;

	/* Process the request */
	switch (command_set) {
	case CMD_SET_SERVER:
		err = server_commands (this, command, id, p, end, &buf);
		break;

	case CMD_SET_INFERIOR: {
#if WINDOWS
		InferiorDelegate delegate;
		InferiorData *inferior_data;
#endif
		MdbInferior *inferior;
		int iid;

		iid = decode_int (p, &p, end);
		inferior = (MdbInferior *) g_hash_table_lookup (inferior_hash, GUINT_TO_POINTER (iid));

		if (!inferior) {
			err = ERR_NO_SUCH_INFERIOR;
			break;
		}

#if WINDOWS
		inferior_data = g_new0 (InferiorData, 1);

		inferior_data->command = command;
		inferior_data->id = id;
		inferior_data->inferior = inferior;
		inferior_data->p = p;
		inferior_data->end = end;
		inferior_data->buf = &buf;

		delegate.func = inferior_command_proxy;
		delegate.user_data = inferior_data;

		if (!InferiorCommand (&delegate))
			err = ERR_NOT_STOPPED;
		else
			err = inferior_data->ret;

		break;
#else
		err = inferior_commands (command, id, inferior, p, end, &buf);
#endif
		break;
	}

	case CMD_SET_BPM: {
		BreakpointManager *bpm;
		int iid;

		iid = decode_int (p, &p, end);
		bpm = (BreakpointManager *) g_hash_table_lookup (bpm_hash, GUINT_TO_POINTER (iid));

		if (!bpm) {
			err = ERR_NO_SUCH_BPM;
			break;
		}

		err = bpm_commands (command, id, bpm, p, end, &buf);
		break;
	}

	case CMD_SET_EXE_READER: {
		MdbExeReader *reader;
		int iid;

		iid = decode_int (p, &p, end);
		reader = (MdbExeReader *) g_hash_table_lookup (exe_reader_hash, GUINT_TO_POINTER (iid));

		if (!reader) {
			err = ERR_NO_SUCH_EXE_READER;
			break;
		}

		err = exe_reader_commands (command, id, reader, p, end, &buf);
		break;
	}

	default:
		err = ERR_NOT_IMPLEMENTED;
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": Command done: %d/%d, id=%d - err=%d, no-reply=%d.",
		   command_set, command, id, err, no_reply);
#endif

	if (!no_reply) {
		debugger_mutex_lock (main_mutex);
		send_reply_packet (id, err, &buf);
		debugger_mutex_unlock (main_mutex);
	}

	g_free (data);
	buffer_free (&buf);

	return TRUE;
}

MdbExeReader *
MdbServer::GetExeReader (const char *filename)
{
	MdbExeReader *reader;

	reader = (MdbExeReader *) g_hash_table_lookup (exe_file_hash, filename);
	if (reader)
		return reader;

	reader = mdb_server_create_exe_reader (filename);
	g_hash_table_insert (exe_file_hash, g_strdup (filename), reader);

	if (!main_reader)
		main_reader = reader;

	return reader;
}

MdbDisassembler *
MdbServer::GetDisassembler (MdbInferior *inferior)
{
	return main_reader->GetDisassembler (inferior);
}
