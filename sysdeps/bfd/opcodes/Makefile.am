noinst_LTLIBRARIES = libopcodes.la

if BFD_TARGET_X64_LINUX
opcode_sources = i386-dis.c
endif
if BFD_TARGET_I386_LINUX
opcode_sources = i386-dis.c
endif
if BFD_TARGET_POWERPC_DARWIN
opcode_sources = i386-dis.c ppc-dis.c ppc-opc.c
endif
if BFD_TARGET_I386_DARWIN
opcode_sources = i386-dis.c
endif
if BFD_TARGET_I386_WINDOWS
opcode_sources = i386-dis.c
endif

INCLUDES = -D_GNU_SOURCE @bfd_cflags@ -I$(srcdir)/.. -I$(srcdir)/../include

libopcodes_la_SOURCES =  $(opcode_sources) dis-buf.c disassemble.c dis-init.c
libopcodes_la_LDFLAGS = -export-dynamic

EXTRA_DIST = opintl.h sysdep.h
