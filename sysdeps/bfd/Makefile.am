## Process this file with automake to generate Makefile.in

SUBDIRS = include hosts libiberty opcodes

DEBUGDIR=${libdir}/debug

INCDIR = $(srcdir)/include
CSEARCH = -I. -I$(srcdir) -I$(INCDIR) -DDEBUGDIR=\"$(DEBUGDIR)\"

noinst_LTLIBRARIES = libbfd.la libbfd-static.la

WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

elf32_backend = elf32-i386.c elf32.c elf.c elflink.c elf-strtab.c elf-eh-frame.c dwarf1.c elf32-gen.c elf-vxworks.c elf-attrs.c elf-ifunc.c

if BFD_TARGET_X64_LINUX
bfd_backend = elf64-x86-64.c elf64.c elf64-gen.c $(elf32_backend) i386linux.c aout32.c peigen.c cofflink.c cpu-i386.c pe-i386.c pei-i386.c trad-core.c archive64.c verilog.c
endif
if BFD_TARGET_I386_LINUX
bfd_backend = $(elf32_backend) i386linux.c aout32.c efi-app-ia32.c peigen.c cofflink.c cpu-i386.c pe-i386.c pei-i386.c trad-core.c verilog.c
endif
if BFD_TARGET_POWERPC_DARWIN
bfd_backend = mach-o.c pef.c rs6000-core.c xsym.c cpu-rs6000.c cpu-powerpc.c
endif
if BFD_TARGET_I386_DARWIN
bfd_backend = mach-o.c pef.c xsym.c cpu-i386.c
endif
if BFD_TARGET_I386_WINDOWS
bfd_backend = pe-i386.c pei-i386.c xsym.c cpu-i386.c peigen.c cofflink.c $(elf32_backend) archive64.c verilog.c
endif

BFD_BACKEND = $(bfd_backend)
TDEFAULTS =  -DDEFAULT_VECTOR=@default_vec@ -DSELECT_VECS='@select_vecs@' -DSELECT_ARCHITECTURES='@select_archs@' @vec_defines@
bfd_cflags = @bfd_cflags@

INCLUDES = -D_GNU_SOURCE $(bfd_cflags) $(CSEARCH) $(CSWITCHES)

SOURCE_FILES = \
	archive.c archures.c bfd.c cache.c coffgen.c corefile.c \
	format.c init.c libbfd.c opncls.c reloc.c \
	section.c syms.c targets.c hash.c linker.c \
	srec.c binary.c tekhex.c ihex.c stabs.c stab-syms.c \
	merge.c dwarf2.c bfdio.c simple.c compress.c

libbfd_la_SOURCES = $(BFD_BACKEND) $(SOURCE_FILES)
libbfd_la_LDFLAGS = -export-dynamic
libbfd_la_LIBADD = libiberty/libiberty.la opcodes/libopcodes.la

libbfd_static_la_SOURCES = $(BFD_BACKEND) $(SOURCE_FILES)
libbfd_static_la_LDFLAGS = -static
libbfd_static_la_LIBADD = libiberty/libiberty-static.la opcodes/libopcodes-static.la

# When compiling archures.c and targets.c, supply the default target
# info from configure.

targets.lo: targets.c Makefile
	$(LIBTOOL) --mode=compile $(COMPILE) -c $(TDEFAULTS) $(srcdir)/targets.c

archures.lo: archures.c Makefile
	$(LIBTOOL) --mode=compile $(COMPILE) -c $(TDEFAULTS) $(srcdir)/archures.c

EXTRA_DIST = coff-i386.c peigen.c aout-target.h aoutx.h bfd.h bfd-in2.h bfd-in.h coffcode.h \
	coffswap.h elf32-target.h elf-bfd.h elfcode.h elfcore.h elflink.h genlink.h libaout.h \
	libbfd.h libbfd-in.h libcoff.h libcoff-in.h libecoff.h libpei.h peicode.h sysdep.h \
	targmatch.h version.h bfdver.h elf64-target.h
