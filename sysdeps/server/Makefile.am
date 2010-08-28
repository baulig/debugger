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
	i386-arch.cpp			\
	x86_64-arch.cpp			\
	x86-arch.cpp			\
	x86-arch.h			\
	arm-arch.cpp			\
	arm-arch.h			\
	mdb-server-linux.cpp		\
	mdb-server-window.cpp

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
	x86-arch.cpp \
	x86-arch.h \
	windows-inferior.cpp
platform_server = \
	bfd-reader.cpp \
	mdb-server-windows.cpp
platform_libs = \
	-lmswsock -lws2_32 -lpsapi -lkernel32
endif
if PLATFORM_X86_LINUX
platform_sources = \
	x86-arch.cpp \
	x86-arch.h \
	ptrace-inferior.cpp
platform_server = \
	bfd-reader.cpp \
	mdb-server-linux.cpp
platform_libs = \
	-lpthread
endif
if PLATFORM_ARM_LINUX
platform_sources = \
	arm-arch.cpp \
	arm-arch.h \
	ptrace-inferior.cpp
platform_server = \
	bfd-reader.cpp \
	mdb-server-linux.cpp
mdb_server_ldflags = -entry=main
else
mdb_server_ldflags =
endif

server_sources = \
	$(platform_sources)	\
	debugger-mutex.c	\
	debugger-mutex.h	\
	breakpoints.cpp		\
	breakpoints.h		\
	mdb-inferior.cpp	\
	mdb-inferior.h		\
	mdb-arch.cpp		\
	mdb-arch.h		\
	mdb-process.h

server_cppflags = \
	-I$(top_srcdir)/sysdeps/bfd \
	-I$(top_srcdir)/sysdeps/bfd/include \
	-I$(top_srcdir)/sysdeps/bfd/opcodes \
	@SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@ \
	$(eglib_cflags)

EXTRA_libmonodebuggerserver_la_SOURCES = $(extra_sources)
libmonodebuggerserver_la_CPPFLAGS = $(server_cppflags)
libmonodebuggerserver_la_SOURCES = $(server_sources)
libmonodebuggerserver_la_LDFLAGS = -no-undefined -export-dynamic -shared
libmonodebuggerserver_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ $(eglib_libs) $(platform_libs) \
	../../sysdeps/bfd/libbfd.la

mdb_server_CPPFLAGS = $(server_cppflags) -DMDB_SERVER -fno-rtti -fno-exceptions
EXTRA_mdb_server_SOURCES = $(extra_sources)

mdb_server_SOURCES = \
	mdb-server.cpp \
	mdb-server.h \
	$(server_sources) \
	$(platform_server)

mdb_server_LDFLAGS = \
	-static -no-undefined $(mdb_server_ldflags)

mdb_server_LDADD = \
	@SERVER_DEPENDENCIES_LIBS@ $(eglib_static_libs) $(platform_libs) \
	../../sysdeps/bfd/libbfd-static.la

CLEANFILES = lib*.a lib*.dll
