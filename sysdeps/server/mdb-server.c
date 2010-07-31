#include <server.h>
#include <errno.h>
#include <unistd.h>
#include <string.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <netinet/tcp.h>

static const char *handshake_msg = "9da91832-87f3-4cde-a92f-6384fec6536e";

#define HEADER_LENGTH 11

#define MAJOR_VERSION 1
#define MINOR_VERSION

typedef enum {
	CMD_SET_VM = 1
} CommandSet;

typedef enum {
	CMD_VM_SPAWN = 1
} CmdVM;

typedef enum {
	ERR_NONE = 0,
	ERR_UNKNOWN_ERROR = 1,
	ERR_NOT_IMPLEMENTED = 2
} ErrorCode;

static gboolean main_loop_iteration (void);

volatile static int packet_id = 0;
static int conn_fd = 0;

static BreakpointManager *breakpoint_manager;
static ServerHandle *server;

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

	id = g_atomic_int_exchange_and_add (&packet_id, 1);

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

int
main (int argc, char *argv[])
{
	struct sockaddr_in serv_addr, cli_addr;
	int fd, res;
	socklen_t cli_len;
	char buf[128];

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

	breakpoint_manager = mono_debugger_breakpoint_manager_new ();
	server = mono_debugger_server_create_inferior (breakpoint_manager);

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

	while (main_loop_iteration ()) {
		;
	}

	close (conn_fd);
	close (fd);

	return 0;
}

static ErrorCode
vm_commands (int command, int id, guint8 *p, guint8 *end, Buffer *buf)
{
	switch (command) {
	case CMD_VM_SPAWN: {
		ServerCommandError result;
		char *cwd, **argv;
		int argc, i, child_pid;

		cwd = decode_string (p, &p, end);
		argc = decode_int (p, &p, end);

		g_message (G_STRLOC ": spawn: %s - %d", cwd, argc);

		argv = g_new0 (char *, argc);
		for (i = 0; i < argc; i++) {
			argv [i] = decode_string (p, &p, end);
			g_message (G_STRLOC ": arg[%d]: %s", i, argv [i]);
		}

		g_message (G_STRLOC);

		result = mono_debugger_server_spawn (server, cwd, argv, NULL, FALSE, &child_pid, NULL, NULL);
		g_message (G_STRLOC ": %d - %d", result, child_pid);

		if (result != COMMAND_ERROR_NONE)
			return ERR_UNKNOWN_ERROR;

		buffer_add_int (buf, child_pid);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

static gboolean
main_loop_iteration (void)
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

	g_message (G_STRLOC ": Received command %d/%d, id=%d.\n", command_set, command, id);

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
	case CMD_SET_VM:
		err = vm_commands (command, id, p, end, &buf);
		break;

	default:
		err = ERR_NOT_IMPLEMENTED;
	}		

	if (!no_reply)
		send_reply_packet (id, err, &buf);

	g_free (data);
	buffer_free (&buf);

	return TRUE;
}
