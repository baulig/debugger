#include <connection.h>
#include <mdb-inferior.h>
#include <stdlib.h>
#include <errno.h>

#ifdef HAVE_SYS_SOCKET_H
#include <sys/socket.h>
#endif
#ifdef WINDOWS
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

static const char *handshake_msg = "9da91832-87f3-4cde-a92f-6384fec6536e";

#define HEADER_LENGTH 11

#define MAJOR_VERSION 1
#define MINOR_VERSION

volatile int Connection::packet_id = 0;

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

bool
Connection::TransportSend (const guint8 *data, int len)
{
	int res;

	do {
		res = send (conn_fd, (const char *) data, len, 0);
	} while (res == -1 && errno == EINTR);
	if (res != len)
		return false;
	else
		return true;
}

Buffer::Buffer (int size)
{
	buf = (guint8 *) g_malloc (size);
	p = buf;
	end = buf + size;
}

Buffer::Buffer (const guint8* data, int size)
{
	buf = (guint8 *) g_malloc (size);
	p = buf;
	end = buf + size;
	memcpy (buf, data, size);
}

void
Buffer::MakeRoom (int size)
{
	if (end - p < size) {
		int new_size = end - buf + size + 32;
		int offset = p - buf;

		buf = (guint8 *) g_realloc (buf, new_size);
		p = buf + offset;
		end = buf + new_size;
	}
}

void
Buffer::AddByte (guint8 val)
{
	MakeRoom (1);
	p [0] = val;
	p++;
}

void
Buffer::AddInt (guint32 val)
{
	MakeRoom (4);
	p [0] = (val >> 24) & 0xff;
	p [1] = (val >> 16) & 0xff;
	p [2] = (val >> 8) & 0xff;
	p [3] = (val >> 0) & 0xff;
	p += 4;
}

void
Buffer::AddLong (guint64 l)
{
	AddInt ((l >> 32) & 0xffffffff);
	AddInt ((l >> 0) & 0xffffffff);
}

void
Buffer::AddID (int id)
{
	AddInt ((guint64)id);
}

void
Buffer::AddData (const guint8 *data, int len)
{
	MakeRoom (len);
	memcpy (p, data, len);
	p += len;
}

void
Buffer::AddString (const char *str)
{
	int len;

	if (str == NULL) {
		AddInt (0);
	} else {
		len = strlen (str);
		AddInt (len);
		AddData ((guint8*)str, len);
	}
}

Buffer::~Buffer (void)
{
	g_free (buf);
	buf = NULL;
}

int
Buffer::GetDataSize (void)
{
	return p - buf;
}

const guint8 *
Buffer::GetData (void)
{
	return buf;
}

int
Buffer::DecodeByte (void)
{
	int retval;
	g_assert (p+1 <= end);
	return *p++;
}

int
Buffer::DecodeInt (void)
{
	int retval;

	g_assert (p+4 <= end);

	retval = (((int)p [0]) << 24) | (((int)p [1]) << 16) | (((int)p [2]) << 8) | (((int)p [3]) << 0);
	p += 4;
	return retval;
}

gint64
Buffer::DecodeLong (void)
{
	guint32 high = DecodeInt ();
	guint32 low = DecodeInt ();

	return ((((guint64)high) << 32) | ((guint64)low));
}

int
Buffer::DecodeID (void)
{
	return DecodeInt ();
}

gchar*
Buffer::DecodeString (void)
{
	int len = DecodeInt ();
	char *s;

	s = (char *) g_malloc0 (len + 1);
	g_assert (s);

	memcpy (s, p, len);
	s [len] = '\0';
	p += len;

	return s;
}

bool
Connection::SendPacket (int command_set, int command, Buffer *data)
{
	Buffer *buf;
	int len, id;
	gboolean res;

	id = ++packet_id;

	len = data->GetDataSize () + 11;
	buf = new Buffer (len);
	buf->AddInt (len);
	buf->AddInt (id);
	buf->AddByte (0); /* flags */
	buf->AddByte (command_set);
	buf->AddByte (command);
	buf->AddData (data->GetData (), data->GetDataSize ());

	res = TransportSend (buf->GetData (), len);

	delete buf;

	return res;
}

bool
Connection::SendReplyPacket (int id, int error, Buffer *data)
{
	Buffer *buf;
	int len;
	gboolean res;

	len = data->GetDataSize () + 11;
	buf = new Buffer (len);
	buf->AddInt (len);
	buf->AddInt (id);
	buf->AddByte (0x80); /* flags */
	buf->AddByte ((error >> 8) & 0xff);
	buf->AddByte (error);
	buf->AddData (data->GetData (), data->GetDataSize ());

	res = TransportSend (buf->GetData (), len);

	delete buf;

	return res;
}

bool
Connection::Setup (void)
{
	char buf [BUFSIZ];
	int res;

	/* Write handshake message */
	do {
		res = send (conn_fd, handshake_msg, strlen (handshake_msg), 0);
	} while (res == -1 && errno == EINTR);

	if (res < 0) {
		g_error (G_STRLOC ": Handshake failed!");
		return false;
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": sent handshake");
#endif

	/* Read answer */
	res = recv_length (conn_fd, buf, strlen (handshake_msg), 0);
	if ((res != strlen (handshake_msg)) || (memcmp (buf, handshake_msg, strlen (handshake_msg) != 0))) {
		g_error (G_STRLOC ": Handshake failed!");
		return false;
	}

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": handshake ok");
#else
	g_message (G_STRLOC ": waiting for incoming connections");
#endif

	return true;
}

bool
Connection::HandleIncomingRequest (MdbServer *server)
{
	guint8 header [HEADER_LENGTH];
	int res, len, id, flags, command_set, command;
	Buffer *in = NULL;
	Buffer *buf;
	ErrorCode err;
	gboolean no_reply;

	res = recv_length (conn_fd, header, HEADER_LENGTH, 0);

	/* This will break if the socket is closed during shutdown too */
	if (res != HEADER_LENGTH)
		return false;

	in = new Buffer (header, HEADER_LENGTH);

	len = in->DecodeInt ();
	id = in->DecodeInt ();
	flags = in->DecodeByte ();
	command_set = in->DecodeByte ();
	command = in->DecodeByte ();

	g_assert (flags == 0);

	delete in;
	in = NULL;

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": Received command %d/%d, id=%d.", command_set, command, id);
#endif

	if (len - HEADER_LENGTH > 0) {
		int size = len - HEADER_LENGTH;
		guint8 *data = (guint8 *) g_malloc (size);

		res = recv_length (conn_fd, data, size, 0);
		if (res != size)
			return false;

		in = new Buffer (data, size);
		g_free (data);
	}

	buf = new Buffer (128);

	err = ERR_NONE;
	no_reply = FALSE;

	/* Process the request */
	switch (command_set) {
	case CMD_SET_SERVER:
		err = server->ProcessCommand (command, id, in, buf);
		break;

	case CMD_SET_INFERIOR: {
#if WINDOWS
		InferiorDelegate delegate;
		InferiorData *inferior_data;
#endif
		MdbInferior *inferior;
		int iid;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior) {
			err = ERR_NO_SUCH_INFERIOR;
			break;
		}

#if WINDOWS
		inferior_data = g_new0 (InferiorData, 1);

		inferior_data->command = command;
		inferior_data->id = id;
		inferior_data->inferior = inferior;
		inferior_data->in = in;
		inferior_data->out = buf;

		delegate.func = inferior_command_proxy;
		delegate.user_data = inferior_data;

		if (!InferiorCommand (&delegate))
			err = ERR_NOT_STOPPED;
		else
			err = inferior_data->ret;

		break;
#else
		err = inferior->ProcessCommand (command, id, in, buf);
#endif
		break;
	}

	case CMD_SET_BPM: {
		BreakpointManager *bpm;
		int iid;

		iid = in->DecodeID ();
		bpm = (BreakpointManager *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_BREAKPOINT_MANAGER);

		if (!bpm) {
			err = ERR_NO_SUCH_BPM;
			break;
		}

		err = bpm->ProcessCommand (command, id, in, buf);
		break;
	}

	case CMD_SET_EXE_READER: {
		MdbExeReader *reader;
		int iid;

		iid = in->DecodeID ();
		reader = (MdbExeReader *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_EXE_READER);

		if (!reader) {
			err = ERR_NO_SUCH_EXE_READER;
			break;
		}

		err = reader->ProcessCommand (command, id, in, buf);
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
		ServerObject::Lock ();
		SendReplyPacket (id, err, buf);
		ServerObject::Unlock ();
	}

	if (in)
		delete in;
	delete buf;

	return true;
}

#if WINDOWS

typedef struct {
	int command;
	int id;
	MdbInferior *inferior;
	Buffer *in;
	Buffer *out;
	ErrorCode ret;
} InferiorData;

static void
inferior_command_proxy (gpointer user_data)
{
	InferiorData *data = (InferiorData *) user_data;

	data->ret = inferior->ProcessCommand (data->command, data->id, data->in, data->out);
}

#endif

void
Connection::SendEvent (ServerEvent *e)
{
	Buffer *buf;

	buf = new Buffer (128 + e->opt_data_size);
	if (e->sender) {
		buf->AddByte (e->sender->GetObjectKind ());
		buf->AddInt (e->sender->GetID ());
	} else {
		buf->AddByte (0);
	}
	if (e->arg_object) {
		buf->AddByte (e->arg_object->GetObjectKind ());
		buf->AddInt (e->arg_object->GetID ());
	} else {
		buf->AddByte (0);
	}
	buf->AddByte (e->type);
	buf->AddLong (e->arg);
	buf->AddLong (e->data1);
	buf->AddLong (e->data2);
	buf->AddInt (e->opt_data_size);
	if (e->opt_data)
		buf->AddData ((guint8 *) e->opt_data, e->opt_data_size);

	ServerObject::Lock ();
	SendPacket (CMD_SET_EVENT, CMD_COMPOSITE, buf);
	ServerObject::Unlock ();

	delete buf;
}

