#include <mdb-server.h>
#include <errno.h>
#include <unistd.h>
#include <string.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <sys/wait.h>
#include <bfd.h>
#if defined(__linux__) || defined(__FreeBSD__)
#include <link.h>
#include <elf.h>
#endif

static volatile sig_atomic_t got_SIGCHLD = 0;

static sigset_t sigmask, empty_mask;

static void
child_sig_handler (int sig)
{
	got_SIGCHLD = 1;
}

int
mdb_server_init_os (void)
{
	struct sigaction sa;
	int res;

	sigemptyset (&sigmask);
	sigaddset (&sigmask, SIGCHLD);

	res = sigprocmask (SIG_BLOCK, &sigmask, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigprocmask() failed!");
		return -1;
	}

	sa.sa_flags = 0;
	sa.sa_handler = child_sig_handler;
	sigemptyset (&sa.sa_mask);

	res = sigaction (SIGCHLD, &sa, NULL);
	if (res < 0) {
		g_error (G_STRLOC ": sigaction() failed!");
		return -1;
	}

	sigemptyset (&empty_mask);

	bfd_init ();

	return 0;
}

static void
handle_wait_event (void)
{
	ServerHandle *server;
	ServerStatusMessageType message;
	guint64 arg, data1, data2;
	guint32 opt_data_size;
	gpointer opt_data;
	int ret, status;

	ret = waitpid (-1, &status, WUNTRACED | __WALL | __WCLONE | WNOHANG);
	if (ret < 0) {
		g_warning (G_STRLOC ": waitpid() failed: %s", g_strerror (errno));
		return;
	} else if (ret == 0)
		return;

	g_message (G_STRLOC ": waitpid(): %d - %x", ret, status);

#ifdef PTRACE_EVENT_CLONE
	if (status >> 16) {
		switch (status >> 16) {
		case PTRACE_EVENT_CLONE: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, ret, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", ret, g_strerror (errno));
				return FALSE;
			}

			// *arg = new_pid;
			//return MESSAGE_CHILD_CREATED_THREAD;
			break;
		}

		case PTRACE_EVENT_FORK: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, ret, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", ret, g_strerror (errno));
				return FALSE;
			}

			// *arg = new_pid;
			//return MESSAGE_CHILD_FORKED;
			break;
		}

#if 0

		case PTRACE_EVENT_EXEC:
			return MESSAGE_CHILD_EXECD;

		case PTRACE_EVENT_EXIT: {
			int exitcode;

			if (ptrace (PTRACE_GETEVENTMSG, handle->inferior->pid, 0, &exitcode)) {
				g_warning (G_STRLOC ": %d - %s", handle->inferior->pid,
					   g_strerror (errno));
				return FALSE;
			}

			*arg = 0;
			return MESSAGE_CHILD_CALLED_EXIT;
		}

		default:
			g_warning (G_STRLOC ": Received unknown wait result %x on child %d",
				   status, handle->inferior->pid);
			return MESSAGE_UNKNOWN_ERROR;
		}
#endif
	}
#endif

	server = mdb_server_get_inferior_by_pid (ret);
	if (!server) {
		g_warning (G_STRLOC ": Got wait event for unknown pid: %d", ret);
		return;
	}

	message = mono_debugger_server_dispatch_event (
		server, status, &arg, &data1, &data2, &opt_data_size, &opt_data);

	g_message (G_STRLOC ": dispatched child event: %d / %d - %Ld, %Lx, %Lx",
		   message, ret, arg, data1, data2);


	mdb_server_process_child_event (
		message, ret, arg, data1, data2, opt_data_size, opt_data);
}

void
mdb_server_main_loop (int conn_fd)
{
	while (TRUE) {
		fd_set readfds;
		int ret, nfds;

		FD_ZERO (&readfds);
		FD_SET (conn_fd, &readfds);
		nfds = conn_fd + 1;

		g_message (G_STRLOC ": pselect()");
		ret = pselect (nfds, &readfds, NULL, NULL, NULL, &empty_mask);
		g_message (G_STRLOC ": pselect() returned: %d - %d", ret, got_SIGCHLD);

		if (got_SIGCHLD) {
			got_SIGCHLD = 0;
			handle_wait_event ();
		}

		if (FD_ISSET (conn_fd, &readfds)) {
			if (!mdb_server_main_loop_iteration ())
				break;
		}
	}
}

struct _MdbExeReader
{
	bfd *bfd;
	long symtab_size;
	long num_symbols;
	asymbol **symtab;
};

MdbExeReader *
mdb_server_create_exe_reader (const char *filename)
{
	MdbExeReader *reader = g_new0 (MdbExeReader, 1);

	reader->bfd = bfd_openr (g_strdup (filename), NULL);
	if (!reader->bfd) {
		g_free (reader);
		return NULL;
	}

	if (!bfd_check_format (reader->bfd, bfd_object) && !bfd_check_format (reader->bfd, bfd_archive)) {
		g_warning (G_STRLOC ": Invalid bfd format: %s", filename);
		bfd_close (reader->bfd);
		g_free (reader);
		return NULL;
	}

	reader->symtab_size = bfd_get_symtab_upper_bound (reader->bfd);

	if (reader->symtab_size) {
		reader->symtab = g_malloc0 (reader->symtab_size);
		reader->num_symbols = bfd_canonicalize_symtab (reader->bfd, reader->symtab);
	}

	return reader;
}

guint64
mdb_exe_reader_get_start_address (MdbExeReader *reader)
{
	return bfd_get_start_address (reader->bfd);
}

guint64
mdb_exe_reader_lookup_symbol (MdbExeReader *reader, const char *name)
{
	int i;

	if (!reader->symtab)
		return 0;

	for (i = 0; i < reader->num_symbols; i++) {
		asymbol *symbol = reader->symtab [i];
		gboolean is_function;
		guint64 address;
		int flags;

		if ((symbol->flags & (BSF_WEAK | BSF_DYNAMIC)) == (BSF_WEAK | BSF_DYNAMIC))
			continue;
		if ((symbol->flags & BSF_DEBUGGING) || !symbol->name || !strlen (symbol->name))
			continue;

		flags = symbol->flags & ~(BSF_DYNAMIC | BSF_NOT_AT_END);

		if (flags == (BSF_OBJECT | BSF_GLOBAL)) {
			is_function = 0;
			address = symbol->section->vma + symbol->value;
		} else if (flags == BSF_FUNCTION) {
			is_function = 1;
			address = symbol->value;
		} else if (flags == (BSF_FUNCTION | BSF_GLOBAL)) {
			is_function = 1;
			address = symbol->section->vma + symbol->value;
		} else if (flags == (BSF_OBJECT | BSF_LOCAL)) {
			is_function = 0;
			address = symbol->section->vma + symbol->value;
		} else {
			//Mach headers don't have a function flag. Mark everything as function for now.
			//Possibly check address against text section to fix this properly?
			is_function = 1;
			address = symbol->section->vma + symbol->value;
		}

		if (!strcmp (symbol->name, name))
			return address;
	}

	return 0;
}

gchar *
mdb_exe_reader_get_target_name (MdbExeReader *reader)
{
	return g_strdup (reader->bfd->xvec->name);
}

static asection *
find_section (MdbExeReader *reader, const char *name)
{
	asection *section;

	g_message (G_STRLOC ": %s - %p", name, reader->bfd->sections);

	for (section = reader->bfd->sections; section; section = section->next) {
		g_message (G_STRLOC ": %p - %s - %Lx", section, section->name, section->vma);
		if (!strcmp (section->name, name))
			return section;
	}

	return NULL;
}

gboolean
mdb_exe_reader_has_section (MdbExeReader *reader, const char *name)
{
	return find_section (reader, name) != NULL;
}

guint64
mdb_exe_reader_get_section_address (MdbExeReader *reader, const char *name)
{
	asection *section = find_section (reader, name);
	if (!section)
		return 0;
	return section->vma;
}

gpointer
mdb_exe_reader_get_section_contents (MdbExeReader *reader, const char *name, guint32 *out_size)
{
	asection *section;
	gpointer contents;

	section = find_section (reader, name);
	if (!section) {
		*out_size = -1;
		return NULL;
	}

	contents = g_malloc0 (section->_raw_size);
	if (!bfd_get_section_contents (reader->bfd, section, contents, 0, section->_raw_size)) {
		g_free (contents);
		*out_size = -1;
		return NULL;
	}

	*out_size = section->_raw_size;
	return contents;
}

static guint64
bfd_glue_elfi386_locate_base (bfd *abfd, const guint8 *data, int size)
{
#if defined(__linux__) || defined(__FreeBSD__)
	const guint8 *ptr;

#if defined(__i386__)
	for (ptr = data; ptr < data + size; ptr += sizeof (Elf32_Dyn)) {
		Elf32_Dyn *dyn = (Elf32_Dyn *) ptr;

		if (dyn->d_tag == DT_NULL)
			break;
		else if (dyn->d_tag == DT_DEBUG)
			return (guint32) dyn->d_un.d_ptr;
	}
#elif defined(__x86_64__)
	for (ptr = data; ptr < data + size; ptr += sizeof (Elf64_Dyn)) {
		Elf64_Dyn *dyn = (Elf64_Dyn *) ptr;

		if (dyn->d_tag == DT_NULL)
			break;
		else if (dyn->d_tag == DT_DEBUG)
			return (guint64) dyn->d_un.d_ptr;
	}
#else
#error "Unknown architecture"
#endif
#endif

	return 0;
}

guint64
mdb_exe_reader_get_dynamic_info (ServerHandle *server, MdbExeReader *reader)
{
	asection *section;
	gpointer contents;
	guint64 dynamic_base;

	section = find_section (reader, ".dynamic");
	if (!section)
		return 0;

	contents = g_malloc0 (section->_raw_size);

	if (mono_debugger_server_read_memory (server, section->vma, section->_raw_size, contents)) {
		g_free (contents);
		return 0;
	}

	dynamic_base = bfd_glue_elfi386_locate_base (reader->bfd, contents, section->_raw_size);

	g_free (contents);

	return dynamic_base;
}
