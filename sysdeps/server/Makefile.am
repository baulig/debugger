bin_PROGRAMS = mdb-server

if USING_EGLIB
eglib_cflags = -I$(top_srcdir)/sysdeps/eglib/src -I../eglib/src
eglib_static_libs = ../eglib/src/libeglib-static.la
eglib_libs = ../eglib/src/libeglib.la
else
eglib_cflags = $(GLIB_CFLAGS)
eglib_libs = $(GLIB_LIBS)
endif

if LIBRARY_BACKEND
server_libraries = libmonodebuggerserver.la
else
server_libraries =
endif

lib_LTLIBRARIES = $(server_libraries)

extra_sources = \
	i386-arch.c			\
	i386-arch.h			\
	x86-arch.h			\
	x86_64-arch.h			\
	x86_64-arch.c			\
	x86-linux-ptrace.c		\
	x86-linux-ptrace.h		\
	x86-ptrace.c			\
	x86-ptrace.h			\
	mdb-server-linux.c		\
	mdb-server-window.c

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
	x86-arch.c \
	martin-windows.c
platform_server = \
	mdb-server-bfd.c \
	mdb-server-windows.c
platform_libs = \
	-lmswsock -lws2_32 -lpsapi -lkernel32
endif
if PLATFORM_X86_LINUX
platform_sources = \
	x86-arch.c \
	x86-ptrace.c \
	thread-db.c \
	thread-db.h
platform_server = \
	mdb-server-bfd.c \
	mdb-server-linux.c
platform_libs = \
	-lpthread \
	-lthread_db
endif
if PLATFORM_ARM_LINUX
platform_sources = \
	arm-arch.c \
	arm-ptrace.c
platform_server = \
	mdb-server-bfd.c \
	mdb-server-linux.c
endif

server_sources = \
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

server_cppflags = \
	-I$(top_srcdir)/sysdeps/bfd \
	-I$(top_srcdir)/sysdeps/bfd/include \
	-I$(top_srcdir)/sysdeps/bfd/opcodes \
	@SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@ \
	$(eglib_cflags) -DTRANSPORT_DEBUG

EXTRA_libmonodebuggerserver_la_SOURCES = $(extra_sources)
libmonodebuggerserver_la_CPPFLAGS = $(server_cppflags)
libmonodebuggerserver_la_SOURCES = $(server_sources)
libmonodebuggerserver_la_LDFLAGS = -no-undefined -export-dynamic -shared
libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ $(eglib_libs) $(platform_libs) \
	../../sysdeps/bfd/libbfd.la

mdb_server_CPPFLAGS = $(server_cppflags) -DMDB_SERVER
EXTRA_mdb_server_SOURCES = $(extra_sources)

mdb_server_SOURCES = \
	mdb-server.c \
	mdb-server.h \
	$(server_sources) \
	$(platform_server)

mdb_server_LDFLAGS = \
	-export-dynamic -static -no-undefined \
	-entry=main

mdb_server_LDADD = \
	@SERVER_DEPENDENCIES_LIBS@ $(eglib_static_libs) $(platform_libs) \
	../../sysdeps/bfd/libbfd-static.la

CLEANFILES = lib*.a lib*.dll
