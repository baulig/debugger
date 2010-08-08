if CSHARP_LIBRARY
csharp_dirs = interface languages classes backend symbolwriter frontend test build
else
csharp_dirs =
endif

if BUILD_SYSDEPS
sysdeps_dir = sysdeps
else
sysdeps_dir =
endif

SUBDIRS = $(sysdeps_dir) $(csharp_dirs)

DIST_SUBDIRS= doc $(SUBDIRS)

pkgconfigdir = $(libdir)/pkgconfig
pkgconfig_DATA = mono-debugger.pc mono-debugger-frontend.pc

EXTRA_DIST = \
	mono-debugger.pc.in mono-debugger-frontend.pc.in

