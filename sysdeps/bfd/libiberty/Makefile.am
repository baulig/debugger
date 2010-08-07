WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

INCLUDES = -D_GNU_SOURCE -I$(srcdir)/../include

noinst_LTLIBRARIES = libiberty.la libiberty-static.la

all_sources = \
	concat.c hashtab.c hex.c objalloc.c safe-ctype.c \
	xexit.c xmalloc.c xstrerror.c fnmatch.c basename.c \
	xstrdup.c lbasename.c lrealpath.c asprintf.c vasprintf.c \
	vsprintf.c vsnprintf.c _doprnt.c unlink-if-ordinary.c \
	cplus-dem.c cp-demint.c cp-demangle.c stpcpy.c

libiberty_la_SOURCES = $(all_sources)
libiberty_la_LDFLAGS = -export-dynamic

libiberty_static_la_SOURCES = $(all_sources)
libiberty_static_la_LDFLAGS = -static
