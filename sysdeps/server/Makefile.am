bin_PROGRAMS = mdb-server

if USING_EGLIB
eglib_cflags = -I$(top_srcdir)/sysdeps/eglib/src -I../eglib/src
eglib_libs = ../eglib/src/libeglib.la
else
eglib_cflags =
eglib_libs =
endif

if SERVER_ONLY
server_libraries =
else
server_libraries = libmonodebuggerserver.la
endif

lib_LTLIBRARIES = $(server_libraries)

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-arch.c			\
	i386-arch.h			\
	x86-arch.h			\
	x86_64-arch.h			\
	x86_64-arch.c			\
	x86-linux-ptrace.c		\
	x86-linux-ptrace.h		\
	x86-ptrace.c			\
	x86-ptrace.h

if PLATFORM_POWERPC_DARWIN
platform_sources =
platform_server =
platform_libs =
endif
if PLATFORM_X86_DARWIN
platform_sources = \
	x86-ptrace.c \
	darwin-thread-db.c \
	thread-db.h
platform_server =
platform_libs =
endif
if PLATFORM_X86_WINDOWS
platform_sources = \
	x86-windows.c
platform_server = \
	mdb-server-bfd.c \
	mdb-server-windows.c
platform_libs = \
	-lmswsock -lws2_32 -lpsapi -lkernel32
endif
if PLATFORM_X86_LINUX
platform_sources = \
	x86-ptrace.c \
	thread-db.c	\
	thread-db.h
platform_server = \
	mdb-server-bfd.c \
	mdb-server-linux.c
platform_libs = \
	-lpthread \
	-lthread_db
endif

libmonodebuggerserver_la_CPPFLAGS = \
	-I$(top_srcdir)/sysdeps/bfd \
	-I$(top_srcdir)/sysdeps/bfd/include \
	-I$(top_srcdir)/sysdeps/bfd/opcodes \
	@SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@ \
	$(eglib_cflags)

libmonodebuggerserver_la_SOURCES = \
	server.h		\
	library.c		\
	$(platform_sources)	\
	debugger-mutex.c	\
	debugger-mutex.h	\
	breakpoints.c		\
	breakpoints.h		\
	libgtop-glue.c		\
	libgtop-glue.h		\
	linux-proc-service.h	\
	bfdglue.c		\
	bfdglue.h

libmonodebuggerserver_la_LDFLAGS = \
	 -no-undefined -export-dynamic -shared

libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ \
	../../sysdeps/bfd/opcodes/libopcodes.la \
	../../sysdeps/bfd/libbfd.la \
	$(eglib_libs) $(platform_libs)

mdb_server_CPPFLAGS = \
	-I$(top_srcdir)/sysdeps/bfd \
	-I$(top_srcdir)/sysdeps/bfd/include \
	-I$(top_srcdir)/sysdeps/bfd/opcodes \
	@SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@ \
	$(eglib_cflags)

EXTRA_mdb_server_SOURCES = \
	i386-arch.c			\
	i386-arch.h			\
	x86-arch.h			\
	x86_64-arch.h			\
	x86_64-arch.c			\
	x86-linux-ptrace.c		\
	x86-linux-ptrace.h		\
	x86-ptrace.c			\
	x86-ptrace.h
	mdb-server-linux.c \
	mdb-server-window.c

if SERVER_ONLY
platform_server_sources = $(platform_sources)
else
platform_server_sources =
endif

mdb_server_SOURCES = \
	mdb-server.c \
	mdb-server.h \
	$(platform_server) \
	$(platform_server_sources)

mdb_server_LDFLAGS = \
	-static

mdb_server_LDADD = \
	libmonodebuggerserver.la

CLEANFILES = lib*.a lib*.dll
