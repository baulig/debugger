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
Connection::TransportSend (guint8 *data, int len)
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
		guint8 *p = (guint8 *) g_realloc (buf, new_size);
		size = p - buf;
		buf = p;
		p = p + size;
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
Buffer::AddData (guint8 *data, int len)
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
}

int
Buffer::GetDataSize (void)
{
	return p - buf;
}

guint8 *
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

int
Buffer::AdvanceOffset (int offset)
{
	p += offset;
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

#ifdef TRANSPORT_DEBUG
	g_message (G_STRLOC ": Received command %d/%d, id=%d.", command_set, command, id);
#endif

	if (len - HEADER_LENGTH > 0) {
		in = new Buffer (len - HEADER_LENGTH);

		res = recv_length (conn_fd, in->GetData (), len - HEADER_LENGTH, 0);
		if (res != len - HEADER_LENGTH)
			return false;
	}

	buf = new Buffer (128);

	err = ERR_NONE;
	no_reply = FALSE;

	/* Process the request */
	switch (command_set) {
	case CMD_SET_SERVER:
		err = ServerCommands (server, command, id, in, buf);
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
		err = InferiorCommands (inferior, command, id, in, buf);
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

		err = BreakpointManagerCommands (bpm, command, id, in, buf);
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

		err = ExeReaderCommands (reader, command, id, in, buf);
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

	delete in;
	delete buf;

	return false;
}

ErrorCode
Connection::ServerCommands (MdbServer *server, int command, int id, Buffer *in, Buffer *buf)
{
	switch (command) {
	case CMD_SERVER_GET_TARGET_INFO: {
		guint32 int_size, long_size, addr_size, is_bigendian;
		ErrorCode result;

		result = MdbInferior::GetTargetInfo (&int_size, &long_size, &addr_size, &is_bigendian);
		if (result)
			return result;

		buf->AddInt (int_size);
		buf->AddInt (long_size);
		buf->AddInt (addr_size);
		buf->AddByte (is_bigendian != 0);
		break;
	}

	case CMD_SERVER_GET_SERVER_TYPE: {
		buf->AddInt (MdbInferior::GetServerType ());
		break;
	}

	case CMD_SERVER_GET_ARCH_TYPE: {
		buf->AddInt (MdbInferior::GetArchType ());
		break;
	}

	case CMD_SERVER_GET_CAPABILITIES: {
		buf->AddInt (MdbInferior::GetCapabilities ());
		break;
	}

	case CMD_SERVER_CREATE_INFERIOR: {
		MdbInferior *inferior;
		BreakpointManager *bpm;
		int bpm_iid;

		bpm_iid = in->DecodeID ();

		bpm = (BreakpointManager *) ServerObject::GetObjectByID (bpm_iid, SERVER_OBJECT_KIND_BREAKPOINT_MANAGER);

		g_message (G_STRLOC ": create inferior: %d - %p", bpm_iid, bpm);

		if (!bpm)
			return ERR_NO_SUCH_BPM;

		inferior = mdb_inferior_new (server, bpm);

		buf->AddInt (inferior->GetID ());
		break;
	}

	case CMD_SERVER_CREATE_BPM: {
		BreakpointManager *bpm;
		int iid;

		bpm = new BreakpointManager ();

		buf->AddInt (bpm->GetID ());
		break;
	}

	case CMD_SERVER_CREATE_EXE_READER: {
		MdbExeReader *reader;
		gchar *filename;
		int iid;

		filename = in->DecodeString ();

		reader = server->GetExeReader (filename);
		if (!reader)
			return ERR_CANNOT_OPEN_EXE;

		buf->AddInt (reader->GetID ());
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

ErrorCode
Connection::InferiorCommands (MdbInferior *inferior, int command, int id, Buffer *in, Buffer *buf)
{
	ErrorCode result = ERR_NONE;

	switch (command) {
	case CMD_INFERIOR_SPAWN: {
		char *cwd, **argv, *error;
		int argc, i, child_pid;

		cwd = in->DecodeString ();
		argc = in->DecodeInt ();

		argv = g_new0 (char *, argc + 1);
		for (i = 0; i < argc; i++)
			argv [i] = in->DecodeString ();
		argv [argc] = NULL;

		if (!*cwd) {
			g_free (cwd);
			cwd = g_get_current_dir ();
		}

		result = inferior->Spawn (cwd, (const gchar **) argv, NULL, &child_pid, &error);
		if (result)
			return result;

		inferior->GetServer ()->AddInferior (inferior, child_pid);

		buf->AddInt (child_pid);

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

		buf->AddInt (sinfo->sigkill);
		buf->AddInt (sinfo->sigstop);
		buf->AddInt (sinfo->sigint);
		buf->AddInt (sinfo->sigchld);
		buf->AddInt (sinfo->sigfpe);
		buf->AddInt (sinfo->sigquit);
		buf->AddInt (sinfo->sigabrt);
		buf->AddInt (sinfo->sigsegv);
		buf->AddInt (sinfo->sigill);
		buf->AddInt (sinfo->sigbus);
		buf->AddInt (sinfo->sigwinch);
		buf->AddInt (sinfo->kernel_sigrtmin);

		g_free (sinfo);
		break;
	}

	case CMD_INFERIOR_GET_APPLICATION: {
		gchar *exe_file = NULL, *cwd = NULL, **cmdline_args = NULL;
		guint32 nargs, i;

		result = inferior->GetApplication (&exe_file, &cwd, &nargs, &cmdline_args);
		if (result)
			return result;

		buf->AddString (exe_file);
		buf->AddString (cwd);

		buf->AddInt (nargs);
		for (i = 0; i < nargs; i++)
			buf->AddString (cmdline_args [i]);

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

		buf->AddLong (frame.address);
		buf->AddLong (frame.stack_pointer);
		buf->AddLong (frame.frame_address);
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

		address = in->DecodeLong ();

		result = inferior->InsertBreakpoint (address, &breakpoint);
		if (result == ERR_NONE)
			buf->AddInt (breakpoint->id);
		break;
	}

	case CMD_INFERIOR_ENABLE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = in->DecodeInt ();
		breakpoint = inferior->LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = inferior->EnableBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_DISABLE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = in->DecodeInt ();
		breakpoint = inferior->LookupBreakpointById (idx);
		if (!breakpoint)
			return ERR_NO_SUCH_BREAKPOINT;

		result = inferior->DisableBreakpoint (breakpoint);
		break;
	}

	case CMD_INFERIOR_REMOVE_BREAKPOINT: {
		BreakpointInfo *breakpoint;
		guint32 idx;

		idx = in->DecodeInt ();
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

		buf->AddInt (count);
		for (i = 0; i < count; i++)
			buf->AddLong (regs [i]);

		g_free (regs);
		break;
	}

	case CMD_INFERIOR_READ_MEMORY: {
		guint64 address;
		guint32 size;

		address = in->DecodeLong ();
		size = in->DecodeInt ();

		buf->MakeRoom (size);

		result = inferior->ReadMemory (address, size, buf->GetData ());
		if (result == ERR_NONE)
			buf->AdvanceOffset (size);

		break;
	}

	case CMD_INFERIOR_WRITE_MEMORY: {
		guint64 address;
		guint32 size;

		address = in->DecodeLong ();
		size = in->DecodeInt ();

		result = inferior->WriteMemory (address, size, in->GetData ());
		break;
	}

	case CMD_INFERIOR_GET_PENDING_SIGNAL: {
		guint32 sig;

		result = inferior->GetPendingSignal (&sig);
		if (result == ERR_NONE)
			buf->AddInt (sig);

		break;
	}

	case CMD_INFERIOR_SET_SIGNAL: {
		guint32 sig, send_it;

		sig = in->DecodeInt ();
		send_it = in->DecodeByte ();

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

		address = in->DecodeLong ();

		insn = inferior->DisassembleInstruction (address, &insn_size);
		buf->AddInt (insn_size);
		buf->AddString (insn);
		g_free (insn);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return result;
}

ErrorCode
Connection::BreakpointManagerCommands (BreakpointManager *bpm, int command, int id, Buffer *in, Buffer *buf)
{
	switch (command) {
	case CMD_BPM_LOOKUP_BY_ADDR: {
		guint64 address;
		BreakpointInfo *info;

		address = in->DecodeLong ();

		info = bpm->Lookup (address);

		if (info) {
			buf->AddInt (info->id);
			buf->AddByte (info->enabled ? 1 : 0);
		} else {
			buf->AddInt (0);
			buf->AddByte (0);
		}
		break;
	}

	case CMD_BPM_LOOKUP_BY_ID: {
		BreakpointInfo *info;
		guint32 idx;

		idx = in->DecodeInt ();

		info = bpm->LookupById (idx);

		if (!info) {
			buf->AddByte (0);
			break;
		}

		buf->AddByte (1);
		buf->AddByte (info->enabled ? 1 : 0);
		break;
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

ErrorCode
Connection::ExeReaderCommands (MdbExeReader *reader, int command, int id, Buffer *in, Buffer *buf)
{
	switch (command) {
	case CMD_EXE_READER_GET_START_ADDRESS: {
		guint64 address;

		address = reader->GetStartAddress ();
		buf->AddLong (address);
		break;
	}

	case CMD_EXE_READER_LOOKUP_SYMBOL: {
		guint64 address;
		gchar *name;

		name = in->DecodeString ();
		address = reader->LookupSymbol (name);
		buf->AddLong (address);
		g_free (name);
		break;
	}

	case CMD_EXE_READER_GET_TARGET_NAME:
		buf->AddString (reader->GetTargetName ());
		break;

	case CMD_EXE_READER_HAS_SECTION: {
		gchar *section_name;
		gboolean has_section;

		section_name = in->DecodeString ();
		has_section = reader->HasSection (section_name);
		buf->AddByte (has_section ? 1 : 0);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_ADDRESS: {
		gchar *section_name;
		guint64 address;

		section_name = in->DecodeString ();
		address = reader->GetSectionAddress (section_name);
		buf->AddLong (address);
		g_free (section_name);
		break;
	}

	case CMD_EXE_READER_GET_SECTION_CONTENTS: {
		gchar *section_name;
		gpointer contents;
		guint32 size;

		section_name = in->DecodeString ();

		contents = reader->GetSectionContents (section_name, &size);
		buf->AddInt (size);

		if (contents) {
			buf->AddData ((guint8 *) contents, size);
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

void
Connection::SendEvent (ServerEvent *e)
{
	Buffer *buf;

	buf = new Buffer (128 + e->opt_data_size);
	buf->AddByte (EVENT_KIND_TARGET_EVENT);
	if (e->sender) {
		buf->AddByte (e->sender->GetObjectKind ());
		buf->AddInt (e->sender->GetID ());
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

