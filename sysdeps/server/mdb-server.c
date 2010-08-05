#include <mdb-server.h>
#include <debugger-mutex.h>
#include <errno.h>
#include <unistd.h>
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
	CMD_SERVER_GET_CAPABILITIES = 3,
	CMD_SERVER_CREATE_INFERIOR = 4,
	CMD_SERVER_CREATE_BPM = 5,
	CMD_SERVER_CREATE_EXE_READER = 6
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
	CMD_INFERIOR_GET_DYNAMIC_INFO = 18
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
		res = send (conn_fd, data, len, 0);
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

	s = g_malloc (len + 1);
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
	buf->buf = g_malloc (size);
	buf->p = buf->buf;
	buf->end = buf->buf + size;
}

static inline void
buffer_make_room (Buffer *buf, int size)
{
	if (buf->end - buf->p < size) {
		int new_size = buf->end - buf->buf + size + 32;
		guint8 *p = g_realloc (buf->buf, new_size);
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

	g_message (G_STRLOC);
	
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

ServerHandle *
mdb_server_get_inferior_by_pid (int pid)
{
	return (ServerHandle *) g_hash_table_lookup (inferior_by_pid, GUINT_TO_POINTER (pid));
}

void
mdb_server_process_child_event (ServerStatusMessageType message, guint32 pid, guint64 arg,
				guint64 data1, guint64 data2, guint32 opt_data_size, gpointer opt_data)
{
	Buffer buf;

	buffer_init (&buf, 128 + opt_data_size);
	buffer_add_byte (&buf, EVENT_KIND_TARGET_EVENT);
	buffer_add_int (&buf, pid);
	buffer_add_byte (&buf, message);
	buffer_add_long (&buf, arg);
	buffer_add_long (&buf, data1);
	buffer_add_long (&buf, data2);
	buffer_add_int (&buf, opt_data_size);
	if (opt_data)
		buffer_add_data (&buf, opt_data, opt_data_size);

	debugger_mutex_lock (main_mutex);
	send_packet (CMD_SET_EVENT, CMD_COMPOSITE, &buf);
	debugger_mutex_unlock (main_mutex);

	buffer_free (&buf);
}

int
main (int argc, char *argv[])
{
	struct sockaddr_in serv_addr, cli_addr;
	int fd, res;
	socklen_t cli_len;
	char buf[128];

	inferior_hash = g_hash_table_new (NULL, NULL);
	inferior_by_pid = g_hash_table_new (NULL, NULL);
	bpm_hash = g_hash_table_new (NULL, NULL);
	exe_reader_hash = g_hash_table_new (NULL, NULL);

	main_mutex = debugger_mutex_new ();

	if (mdb_server_init_os ()) {
		g_warning (G_STRLOC ": Failed to initialize OS backend.");
		return -1;
	}

	fd = socket (AF_INET, SOCK_STREAM, 0);
	g_message (G_STRLOC ": %d", fd);

	memset (&serv_addr, 0, sizeof (serv_addr));

	serv_addr.sin_family = AF_INET;
	serv_addr.sin_addr.s_addr = INADDR_ANY;
	serv_addr.sin_port = htons (8888);

	res = bind (fd, (struct sockaddr *) &serv_addr, sizeof (serv_addr));
	if (res < 0) {
		g_warning (G_STRLOC ": bind() failed: %s", g_strerror (errno));
		return -1;
	}

	listen (fd, 1);

	cli_len = sizeof (cli_addr);

	conn_fd = accept (fd, (struct sockaddr *) &cli_addr, &cli_len);
	if (res < 0) {
		g_warning (G_STRLOC ": accept() failed: %s", g_strerror (errno));
		return -1;
	}

	g_message (G_STRLOC ": accepted!");

	/* Write handshake message */
	do {
		res = send (conn_fd, handshake_msg, strlen (handshake_msg), 0);
	} while (res == -1 && errno == EINTR);

	if (res < 0) {
		g_error (G_STRLOC ": Handshake failed!");
		return -1;
	}

	g_message (G_STRLOC ": sent handshake");

	/* Read answer */
	res = recv_length (conn_fd, buf, strlen (handshake_msg), 0);
	if ((res != strlen (handshake_msg)) || (memcmp (buf, handshake_msg, strlen (handshake_msg) != 0))) {
		g_error (G_STRLOC ": Handshake failed!");
		return -1;
	}

	g_message (G_STRLOC ": handshake ok");

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

	mono_debugger_breakpoint_manager_init ();

	mdb_server_main_loop (conn_fd);

	close (conn_fd);
	close (fd);

	return 0;
}

static ErrorCode
server_commands (int command, int id, guint8 *p, guint8 *end, Buffer *buf)
{
	switch (command) {
	case CMD_SERVER_GET_TARGET_INFO: {
		ServerCommandError result;
		guint32 int_size, long_size, addr_size, is_bigendian;

		result = mono_debugger_server_get_target_info (
			&int_size, &long_size, &addr_size, &is_bigendian);

		if (result != COMMAND_ERROR_NONE)
			return result;

		buffer_add_int (buf, int_size);
		buffer_add_int (buf, long_size);
		buffer_add_int (buf, addr_size);
		buffer_add_byte (buf, is_bigendian != 0);
		break;
	}

	case CMD_SERVER_GET_SERVER_TYPE: {
		buffer_add_int (buf, mono_debugger_server_get_server_type ());
		break;
	}

	case CMD_SERVER_GET_CAPABILITIES: {
		buffer_add_int (buf, mono_debugger_server_get_capabilities ());
		break;
	}

	case CMD_SERVER_CREATE_INFERIOR: {
		ServerHandle *inferior;
		BreakpointManager *bpm;
		int iid, bpm_iid;

		bpm_iid = decode_int (p, &p, end);

		bpm = g_hash_table_lookup (bpm_hash, GUINT_TO_POINTER (bpm_iid));

		g_message (G_STRLOC ": create inferior: %d - %p", bpm_iid, bpm);

		if (!bpm)
			return ERR_NO_SUCH_BPM;

		iid = ++next_unique_id;
		inferior = mono_debugger_server_create_inferior (bpm);
		g_hash_table_insert (inferior_hash, GUINT_TO_POINTER (iid), inferior);

		buffer_add_int (buf, iid);
		break;
	}

	case CMD_SERVER_CREATE_BPM: {
		BreakpointManager *bpm;
		int iid;

		iid = ++next_unique_id;
		bpm = mono_debugger_breakpoint_manager_new ();
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
		g_message (G_STRLOC ": %s", filename);

		reader = mdb_server_create_exe_reader (filename);
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
inferior_commands (int command, int id, ServerHandle *inferior, guint8 *p, guint8 *end, Buffer *buf)
{
	ServerCommandError result = COMMAND_ERROR_NONE;

	switch (command) {
	case CMD_INFERIOR_SPAWN: {
		char *cwd, **argv, *error;
		int argc, i, child_pid;

		cwd = decode_string (p, &p, end);
		argc = decode_int (p, &p, end);

		g_message (G_STRLOC ": spawn: %s - %d", cwd, argc);

		argv = g_new0 (char *, argc + 1);
		for (i = 0; i < argc; i++)
			argv [i] = decode_string (p, &p, end);
		argv [argc] = NULL;

		g_message (G_STRLOC);

		argv [0] = g_strdup_printf ("X:\\Work\\Martin\\mdb\\testnativetypes.exe");
		cwd = g_get_current_dir (); // FIXME

		result = mono_debugger_server_spawn (inferior, cwd, argv, NULL, FALSE, &child_pid, NULL, &error);
		g_message (G_STRLOC ": %d - %d - %s", result, child_pid, error);

		if (result != COMMAND_ERROR_NONE)
			return result;

		g_hash_table_insert (inferior_by_pid, GUINT_TO_POINTER (child_pid), inferior);

		buffer_add_int (buf, child_pid);
		break;
	}

	case CMD_INFERIOR_INIT_PROCESS:
		result = mono_debugger_server_initialize_process (inferior);
		break;

	case CMD_INFERIOR_GET_SIGNAL_INFO: {
		SignalInfo *sinfo;

		result = mono_debugger_server_get_signal_info (inferior, &sinfo);
		if (result != COMMAND_ERROR_NONE)
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
		gchar *exe_file, *cwd, **cmdline_args;
		guint32 nargs, i;

		result = mono_debugger_server_get_application (
			inferior, &exe_file, &cwd, &nargs, &cmdline_args);

		if (result != COMMAND_ERROR_NONE)
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

		result = mono_debugger_server_get_frame (inferior, &frame);
		if (result != COMMAND_ERROR_NONE)
			return result;

		buffer_add_long (buf, frame.address);
		buffer_add_long (buf, frame.stack_pointer);
		buffer_add_long (buf, frame.frame_address);

		break;
	}

	case CMD_INFERIOR_STEP:
		result = mono_debugger_server_step (inferior);
		break;

	case CMD_INFERIOR_CONTINUE:
		result = mono_debugger_server_continue (inferior);
		break;

	case CMD_INFERIOR_RESUME:
		result = mono_debugger_server_resume (inferior);
		break;

	case CMD_INFERIOR_INSERT_BREAKPOINT: {
		guint64 address;
		guint32 breakpoint;

		address = decode_long (p, &p, end);

		result = mono_debugger_server_insert_breakpoint (
			inferior, address, &breakpoint);

		if (result == COMMAND_ERROR_NONE) {
			buffer_add_int (buf, breakpoint);
		}

		break;
	}

	case CMD_INFERIOR_ENABLE_BREAKPOINT: {
		guint32 breakpoint;

		breakpoint = decode_int (p, &p, end);

		result = mono_debugger_server_enable_breakpoint (inferior, breakpoint);
		break;
	}

	case CMD_INFERIOR_DISABLE_BREAKPOINT: {
		guint32 breakpoint;

		breakpoint = decode_int (p, &p, end);

		result = mono_debugger_server_disable_breakpoint (inferior, breakpoint);
		break;
	}

	case CMD_INFERIOR_REMOVE_BREAKPOINT: {
		guint32 breakpoint;

		breakpoint = decode_int (p, &p, end);

		result = mono_debugger_server_remove_breakpoint (inferior, breakpoint);
		break;
	}

	case CMD_INFERIOR_GET_REGISTERS: {
		guint32 count, i;
		guint64 *regs;

		result = mono_debugger_server_count_registers (inferior, &count);
		if (result != COMMAND_ERROR_NONE)
			return ERR_UNKNOWN_ERROR;

		regs = g_new0 (guint64, count);

		result = mono_debugger_server_get_registers (inferior, regs);
		if (result != COMMAND_ERROR_NONE) {
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

		result = mono_debugger_server_read_memory (inferior, address, size, buf->p);

		if (result == COMMAND_ERROR_NONE)
			buf->p += size;

		break;
	}

	case CMD_INFERIOR_WRITE_MEMORY: {
		guint64 address;
		guint32 size;

		address = decode_long (p, &p, end);
		size = decode_int (p, &p, end);

		result = mono_debugger_server_write_memory (inferior, address, size, p);
		break;
	}

	case CMD_INFERIOR_GET_PENDING_SIGNAL: {
		guint32 sig;

		result = mono_debugger_server_get_pending_signal (inferior, &sig);

		if (result == COMMAND_ERROR_NONE)
			buffer_add_int (buf, sig);

		break;
	}

	case CMD_INFERIOR_SET_SIGNAL: {
		guint32 sig, send_it;

		sig = decode_int (p, &p, end);
		send_it = decode_byte (p, &p, end);

		result = mono_debugger_server_set_signal (inferior, sig, send_it);
		break;
	}

	case CMD_INFERIOR_GET_DYNAMIC_INFO: {
		MdbExeReader *reader;
		guint32 reader_iid;
		guint64 dynamic_info;

		reader_iid = decode_int (p, &p, end);

		reader = g_hash_table_lookup (exe_reader_hash, GUINT_TO_POINTER (reader_iid));

		if (!reader)
			return ERR_NO_SUCH_EXE_READER;

		dynamic_info = mdb_exe_reader_get_dynamic_info (inferior, reader);

		buffer_add_long (buf, dynamic_info);
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

		info = mono_debugger_breakpoint_manager_lookup (bpm, address);

		if (info) {
			buffer_add_int (buf, info->id);
			buffer_add_byte (buf, info->enabled != 0);
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

		info = mono_debugger_breakpoint_manager_lookup_by_id (bpm, idx);

		if (!info) {
			buffer_add_byte (buf, 0);
			break;
		}

		buffer_add_byte (buf, 1);
		buffer_add_byte (buf, info->enabled != 0);
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

		address = mdb_exe_reader_get_start_address (reader);
		buffer_add_long (buf, address);
		break;
	}

	case CMD_EXE_READER_LOOKUP_SYMBOL: {
		guint64 address;
		gchar *name;

		name = decode_string (p, &p, end);
		address = mdb_exe_reader_lookup_symbol (reader, name);
		buffer_add_long (buf, address);
		g_free (name);
		break;
	}

	case CMD_EXE_READER_GET_TARGET_NAME: {
		gchar *target_name;

		target_name = mdb_exe_reader_get_target_name (reader);
		buffer_add_string (buf, target_name);
		g_free (target_name);
		break;
	}

	case CMD_EXE_READER_HAS_SECTION: {
		gchar *section_name;
		gboolean has_section;

		section_name = decode_string (p, &p, end);
		has_section = mdb_exe_reader_has_section (reader, section_name);
		buffer_add_byte (buf, has_section ? 1 : 0);
		g_free (section_name);
		break;
	}


	case CMD_EXE_READER_GET_SECTION_ADDRESS: {
		gchar *section_name;
		guint64 address;

		section_name = decode_string (p, &p, end);
		address = mdb_exe_reader_get_section_address (reader, section_name);
		g_message (G_STRLOC ": %s - %Lx", section_name, address);
		buffer_add_long (buf, address);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_CONTENTS: {
		gchar *section_name;
		gpointer contents;
		guint32 size;

		section_name = decode_string (p, &p, end);

		contents = mdb_exe_reader_get_section_contents (reader, section_name, &size);
		buffer_add_int (buf, size);

		if (contents) {
			buffer_add_data (buf, contents, size);
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

gboolean
mdb_server_main_loop_iteration (void)
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

	g_message (G_STRLOC ": Received command %d/%d, id=%d.", command_set, command, id);

	data = g_malloc (len - HEADER_LENGTH);
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
		err = server_commands (command, id, p, end, &buf);
		break;

	case CMD_SET_INFERIOR: {
		ServerHandle *inferior;
		int iid;

		iid = decode_int (p, &p, end);
		inferior = g_hash_table_lookup (inferior_hash, GUINT_TO_POINTER (iid));

		if (!inferior) {
			err = ERR_NO_SUCH_INFERIOR;
			break;
		}

		err = inferior_commands (command, id, inferior, p, end, &buf);
		break;
	}

	case CMD_SET_BPM: {
		BreakpointManager *bpm;
		int iid;

		iid = decode_int (p, &p, end);
		bpm = g_hash_table_lookup (bpm_hash, GUINT_TO_POINTER (iid));

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
		reader = g_hash_table_lookup (exe_reader_hash, GUINT_TO_POINTER (iid));

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

	g_message (G_STRLOC ": Command done: %d/%d, id=%d - err=%d, no-reply=%d.",
		   command_set, command, id, err, no_reply);


	if (!no_reply) {
		debugger_mutex_lock (main_mutex);
		send_reply_packet (id, err, &buf);
		debugger_mutex_unlock (main_mutex);
	}

	g_free (data);
	buffer_free (&buf);

	return TRUE;
}