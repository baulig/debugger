if SERVER_ONLY
csharp_dirs =
else
csharp_dirs = interface languages classes backend symbolwriter frontend test build
endif

if REMOTE_ONLY
sysdeps_dir =
else
sysdeps_dir = sysdeps
endif

SUBDIRS = $(sysdeps_dir) $(csharp_dirs)

DIST_SUBDIRS= doc $(SUBDIRS)

pkgconfigdir = $(libdir)/pkgconfig
pkgconfig_DATA = mono-debugger.pc mono-debugger-frontend.pc

EXTRA_DIST = \
	mono-debugger.pc.in mono-debugger-frontend.pc.in

