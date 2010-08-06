dnl See whether we need to use fopen-bin.h rather than fopen-same.h.
AC_DEFUN([BFD_BINARY_FOPEN],
[AC_REQUIRE([AC_CANONICAL_TARGET])
case "${host}" in
changequote(,)dnl
*-*-msdos* | *-*-go32* | *-*-mingw32* | *-*-cygwin* | *-*-windows*)
changequote([,])dnl
  AC_DEFINE(USE_BINARY_FOPEN, 1, [Use b modifier when opening binary files?]) ;;
esac])dnl
# Autoconf M4 include file defining utility macros for complex Canadian
# cross builds.

dnl ####
dnl # _GCC_TOPLEV_NONCANONICAL_BUILD
dnl # $build_alias or canonical $build if blank.
dnl # Used when we would use $build_alias, but empty is not OK.
AC_DEFUN([_GCC_TOPLEV_NONCANONICAL_BUILD],
[AC_REQUIRE([AC_CANONICAL_BUILD]) []dnl
case ${build_alias} in
  "") build_noncanonical=${build} ;;
  *) build_noncanonical=${build_alias} ;;
esac
]) []dnl # _GCC_TOPLEV_NONCANONICAL_BUILD

dnl ####
dnl # _GCC_TOPLEV_NONCANONICAL_HOST
dnl # $host_alias, or $build_noncanonical if blank.
dnl # Used when we would use $host_alias, but empty is not OK.
AC_DEFUN([_GCC_TOPLEV_NONCANONICAL_HOST],
[AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_BUILD]) []dnl
case ${host_alias} in
  "") host_noncanonical=${build_noncanonical} ;;
  *) host_noncanonical=${host_alias} ;;
esac
]) []dnl # _GCC_TOPLEV_NONCANONICAL_HOST

dnl ####
dnl # _GCC_TOPLEV_NONCANONICAL_TARGET
dnl # $target_alias or $host_noncanonical if blank.
dnl # Used when we would use $target_alias, but empty is not OK.
AC_DEFUN([_GCC_TOPLEV_NONCANONICAL_TARGET],
[AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_HOST]) []dnl
case ${target_alias} in
  "") target_noncanonical=${host_noncanonical} ;;
  *) target_noncanonical=${target_alias} ;;
esac
]) []dnl # _GCC_TOPLEV_NONCANONICAL_TARGET

dnl ####
dnl # ACX_NONCANONICAL_BUILD
dnl # Like underscored version, but AC_SUBST's.
AC_DEFUN([ACX_NONCANONICAL_BUILD],
[AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_BUILD]) []dnl
AC_SUBST(build_noncanonical)
]) []dnl # ACX_NONCANONICAL_BUILD

dnl ####
dnl # ACX_NONCANONICAL_HOST
dnl # Like underscored version, but AC_SUBST's.
AC_DEFUN([ACX_NONCANONICAL_HOST],
[AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_HOST]) []dnl
AC_SUBST(host_noncanonical)
]) []dnl # ACX_NONCANONICAL_HOST

dnl ####
dnl # ACX_NONCANONICAL_TARGET
dnl # Like underscored version, but AC_SUBST's.
AC_DEFUN([ACX_NONCANONICAL_TARGET],
[AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_TARGET]) []dnl
AC_SUBST(target_noncanonical)
]) []dnl # ACX_NONCANONICAL_TARGET

dnl ####
dnl # GCC_TOPLEV_SUBDIRS
dnl # GCC & friends build 'build', 'host', and 'target' tools.  These must
dnl # be separated into three well-known subdirectories of the build directory:
dnl # build_subdir, host_subdir, and target_subdir.  The values are determined
dnl # here so that they can (theoretically) be changed in the future.  They
dnl # were previously reproduced across many different files.
dnl #
dnl # This logic really amounts to very little with autoconf 2.13; it will
dnl # amount to a lot more with autoconf 2.5x.
AC_DEFUN([GCC_TOPLEV_SUBDIRS],
[AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_TARGET]) []dnl
AC_REQUIRE([_GCC_TOPLEV_NONCANONICAL_BUILD]) []dnl

# post-stage1 host modules use a different CC_FOR_BUILD so, in order to
# have matching libraries, they should use host libraries: Makefile.tpl
# arranges to pass --with-build-libsubdir=$(HOST_SUBDIR).
# However, they still use the build modules, because the corresponding
# host modules (e.g. bison) are only built for the host when bootstrap
# finishes. So:
# - build_subdir is where we find build modules, and never changes.
# - build_libsubdir is where we find build libraries, and can be overridden.

# Prefix 'build-' so this never conflicts with target_subdir.
build_subdir="build-${build_noncanonical}"
AC_ARG_WITH(build-libsubdir,
[  --with-build-libsubdir=[DIR]  Directory where to find libraries for build system],
build_libsubdir="$withval",
build_libsubdir="$build_subdir")
# --srcdir=. covers the toplevel, while "test -d" covers the subdirectories
if ( test $srcdir = . && test -d gcc ) \
   || test -d $srcdir/../host-${host_noncanonical}; then
  host_subdir="host-${host_noncanonical}"
else
  host_subdir=.
fi
# No prefix.
target_subdir=${target_noncanonical}
AC_SUBST([build_libsubdir]) []dnl
AC_SUBST([build_subdir]) []dnl
AC_SUBST([host_subdir]) []dnl
AC_SUBST([target_subdir]) []dnl
]) []dnl # GCC_TOPLEV_SUBDIRS


####
# _NCN_TOOL_PREFIXES:  Some stuff that oughtta be done in AC_CANONICAL_SYSTEM 
# or AC_INIT.
# These demand that AC_CANONICAL_SYSTEM be called beforehand.
AC_DEFUN([_NCN_TOOL_PREFIXES],
[ncn_tool_prefix=
test -n "$host_alias" && ncn_tool_prefix=$host_alias-
ncn_target_tool_prefix=
test -n "$target_alias" && ncn_target_tool_prefix=$target_alias-
]) []dnl # _NCN_TOOL_PREFIXES

####
# NCN_STRICT_CHECK_TOOLS(variable, progs-to-check-for,[value-if-not-found],[path])
# Like plain AC_CHECK_TOOLS, but require prefix if build!=host.

AC_DEFUN([NCN_STRICT_CHECK_TOOLS],
[AC_REQUIRE([_NCN_TOOL_PREFIXES]) []dnl
AC_ARG_VAR([$1], [$1 for the host])

if test -n "[$]$1"; then
  ac_cv_prog_$1=[$]$1
elif test -n "$ac_cv_prog_$1"; then
  $1=$ac_cv_prog_$1
fi

if test -n "$ac_cv_prog_$1"; then
  for ncn_progname in $2; do
    AC_CHECK_PROG([$1], [${ncn_progname}], [${ncn_progname}], , [$4])
  done
fi

for ncn_progname in $2; do
  if test -n "$ncn_tool_prefix"; then
    AC_CHECK_PROG([$1], [${ncn_tool_prefix}${ncn_progname}], 
                  [${ncn_tool_prefix}${ncn_progname}], , [$4])
  fi
  if test -z "$ac_cv_prog_$1" && test $build = $host ; then
    AC_CHECK_PROG([$1], [${ncn_progname}], [${ncn_progname}], , [$4]) 
  fi
  test -n "$ac_cv_prog_$1" && break
done

if test -z "$ac_cv_prog_$1" ; then
  ifelse([$3],[], [set dummy $2
  if test $build = $host ; then
    $1="[$]2"
  else
    $1="${ncn_tool_prefix}[$]2"
  fi], [$1="$3"])
fi
]) []dnl # NCN_STRICT_CHECK_TOOLS

####
# NCN_STRICT_CHECK_TARGET_TOOLS(variable, progs-to-check-for,[value-if-not-found],[path])
# Like CVS Autoconf AC_CHECK_TARGET_TOOLS, but require prefix if build!=target.

AC_DEFUN([NCN_STRICT_CHECK_TARGET_TOOLS],
[AC_REQUIRE([_NCN_TOOL_PREFIXES]) []dnl
AC_ARG_VAR([$1], patsubst([$1], [_FOR_TARGET$], [])[ for the target])

if test -n "[$]$1"; then
  ac_cv_prog_$1=[$]$1
elif test -n "$ac_cv_prog_$1"; then
  $1=$ac_cv_prog_$1
fi

if test -n "$ac_cv_prog_$1"; then
  for ncn_progname in $2; do
    AC_CHECK_PROG([$1], [${ncn_progname}], [${ncn_progname}], , [$4])
  done
fi

if test -z "$ac_cv_prog_$1" && test -n "$with_build_time_tools"; then
  for ncn_progname in $2; do
    AC_MSG_CHECKING([for ${ncn_progname} in $with_build_time_tools])
    if test -x $with_build_time_tools/${ncn_progname}; then
      ac_cv_prog_$1=$with_build_time_tools/${ncn_progname}
      AC_MSG_RESULT(yes)
      break
    else
      AC_MSG_RESULT(no)
    fi
  done
fi

if test -z "$ac_cv_prog_$1"; then
  for ncn_progname in $2; do
    if test -n "$ncn_target_tool_prefix"; then
      AC_CHECK_PROG([$1], [${ncn_target_tool_prefix}${ncn_progname}], 
                    [${ncn_target_tool_prefix}${ncn_progname}], , [$4])
    fi
    if test -z "$ac_cv_prog_$1" && test $build = $target ; then
      AC_CHECK_PROG([$1], [${ncn_progname}], [${ncn_progname}], , [$4]) 
    fi
    test -n "$ac_cv_prog_$1" && break
  done
fi
  
if test -z "$ac_cv_prog_$1" ; then
  ifelse([$3],[], [set dummy $2
  if test $build = $target ; then
    $1="[$]2"
  else
    $1="${ncn_target_tool_prefix}[$]2"
  fi], [$1="$3"])
else
  $1="$ac_cv_prog_$1"
fi
]) []dnl # NCN_STRICT_CHECK_TARGET_TOOLS
  

# Backported from Autoconf 2.5x; can go away when and if
# we switch.  Put the OS path separator in $PATH_SEPARATOR.
AC_DEFUN([ACX_PATH_SEP], [
# The user is always right.
if test "${PATH_SEPARATOR+set}" != set; then
  echo "#! /bin/sh" >conf$$.sh
  echo  "exit 0"   >>conf$$.sh
  chmod +x conf$$.sh
  if (PATH="/nonexistent;."; conf$$.sh) >/dev/null 2>&1; then
    PATH_SEPARATOR=';'
  else
    PATH_SEPARATOR=: 
  fi
  rm -f conf$$.sh
fi
])


AC_DEFUN([ACX_TOOL_DIRS], [
AC_REQUIRE([ACX_PATH_SEP])
if test "x$exec_prefix" = xNONE; then
        if test "x$prefix" = xNONE; then
                gcc_cv_tool_prefix=$ac_default_prefix
        else
                gcc_cv_tool_prefix=$prefix
        fi
else
        gcc_cv_tool_prefix=$exec_prefix
fi

# If there is no compiler in the tree, use the PATH only.  In any
# case, if there is no compiler in the tree nobody should use
# AS_FOR_TARGET and LD_FOR_TARGET.
if test x$host = x$build && test -f $srcdir/gcc/BASE-VER; then
    gcc_version=`cat $srcdir/gcc/BASE-VER`
    gcc_cv_tool_dirs="$gcc_cv_tool_prefix/libexec/gcc/$target_noncanonical/$gcc_version$PATH_SEPARATOR"
    gcc_cv_tool_dirs="$gcc_cv_tool_dirs$gcc_cv_tool_prefix/libexec/gcc/$target_noncanonical$PATH_SEPARATOR"
    gcc_cv_tool_dirs="$gcc_cv_tool_dirs/usr/lib/gcc/$target_noncanonical/$gcc_version$PATH_SEPARATOR"
    gcc_cv_tool_dirs="$gcc_cv_tool_dirs/usr/lib/gcc/$target_noncanonical$PATH_SEPARATOR"
    gcc_cv_tool_dirs="$gcc_cv_tool_dirs$gcc_cv_tool_prefix/$target_noncanonical/bin/$target_noncanonical/$gcc_version$PATH_SEPARATOR"
    gcc_cv_tool_dirs="$gcc_cv_tool_dirs$gcc_cv_tool_prefix/$target_noncanonical/bin$PATH_SEPARATOR"
else
    gcc_cv_tool_dirs=
fi

if test x$build = x$target && test -n "$md_exec_prefix"; then
        gcc_cv_tool_dirs="$gcc_cv_tool_dirs$md_exec_prefix$PATH_SEPARATOR"
fi

]) []dnl # ACX_TOOL_DIRS

# ACX_HAVE_GCC_FOR_TARGET
# Check if the variable GCC_FOR_TARGET really points to a GCC binary.
AC_DEFUN([ACX_HAVE_GCC_FOR_TARGET], [
cat > conftest.c << \EOF
#ifdef __GNUC__
  gcc_yay;
#endif
EOF
if ($GCC_FOR_TARGET -E conftest.c | grep gcc_yay) > /dev/null 2>&1; then
  have_gcc_for_target=yes
else
  GCC_FOR_TARGET=${ncn_target_tool_prefix}gcc
  have_gcc_for_target=no
fi
rm conftest.c
])

# ACX_CHECK_INSTALLED_TARGET_TOOL(VAR, PROG)
# Searching for installed target binutils.  We need to take extra care,
# else we may find the wrong assembler, linker, etc., and lose.
#
# First try --with-build-time-tools, if specified.
#
# For build != host, we ask the installed GCC for the name of the tool it
# uses, and accept it if it is an absolute path.  This is because the
# only good choice for a compiler is the same GCC version that is being
# installed (or we couldn't make target libraries), and we assume that
# on the host system we'll have not only the same GCC version, but also
# the same binutils version.
#
# For build == host, search the same directories that the installed
# compiler will search.  We used to do this for the assembler, linker,
# and nm only; for simplicity of configuration, however, we extend this
# criterion to tools (such as ar and ranlib) that are never invoked by
# the compiler, to avoid mismatches.
#
# Also note we have to check MD_EXEC_PREFIX before checking the user's path
# if build == target.  This makes the most sense only when bootstrapping,
# but we also do so when build != host.  In this case, we hope that the
# build and host systems will have similar contents of MD_EXEC_PREFIX.
#
# If we do not find a suitable binary, then try the user's path.

AC_DEFUN([ACX_CHECK_INSTALLED_TARGET_TOOL], [
AC_REQUIRE([ACX_TOOL_DIRS])
AC_REQUIRE([ACX_HAVE_GCC_FOR_TARGET])
if test -z "$ac_cv_path_$1" ; then
  if test -n "$with_build_time_tools"; then
    AC_MSG_CHECKING([for $2 in $with_build_time_tools])
    if test -x $with_build_time_tools/$2; then
      $1=`cd $with_build_time_tools && pwd`/$2
      ac_cv_path_$1=[$]$1
      AC_MSG_RESULT([$ac_cv_path_$1])
    else
      AC_MSG_RESULT(no)
    fi
  elif test $build != $host && test $have_gcc_for_target = yes; then
    $1=`$GCC_FOR_TARGET --print-prog-name=$2`
    test [$]$1 = $2 && $1=
    test -n "[$]$1" && ac_cv_path_$1=[$]$1
  fi
fi
if test -z "$ac_cv_path_$1" && test -n "$gcc_cv_tool_dirs"; then
  AC_PATH_PROG([$1], [$2], [], [$gcc_cv_tool_dirs])
fi
if test -z "$ac_cv_path_$1" ; then
  NCN_STRICT_CHECK_TARGET_TOOLS([$1], [$2])
else
  $1=$ac_cv_path_$1
fi
]) []dnl # ACX_CHECK_INSTALLED_TARGET_TOOL

###
# AC_PROG_CPP_WERROR
# Used for autoconf 2.5x to force AC_PREPROC_IFELSE to reject code which
# triggers warnings from the preprocessor.  Will be in autoconf 2.58.
# For now, using this also overrides header checks to use only the
# preprocessor (matches 2.13 behavior; matching 2.58's behavior is a
# bit harder from here).
# Eventually autoconf will default to checking headers with the compiler
# instead, and we'll have to do this differently.

AC_DEFUN([AC_PROG_CPP_WERROR],
[AC_REQUIRE([AC_PROG_CPP])dnl
m4_define([AC_CHECK_HEADER],m4_defn([_AC_CHECK_HEADER_OLD]))
ac_c_preproc_warn_flag=yes])# AC_PROG_CPP_WERROR

# Test for GNAT.
# We require the gnatbind program, and a compiler driver that
# understands Ada.  We use the user's CC setting, already found,
# and possibly add $1 to the command-line parameters.
#
# Sets the shell variable have_gnat to yes or no as appropriate, and
# substitutes GNATBIND and GNATMAKE.
AC_DEFUN([ACX_PROG_GNAT],
[AC_REQUIRE([AC_CHECK_TOOL_PREFIX])
AC_REQUIRE([AC_PROG_CC])
AC_CHECK_TOOL(GNATBIND, gnatbind, no)
AC_CHECK_TOOL(GNATMAKE, gnatmake, no)
AC_CACHE_CHECK([whether compiler driver understands Ada],
		 acx_cv_cc_gcc_supports_ada,
[cat >conftest.adb <<EOF
procedure conftest is begin null; end conftest;
EOF
acx_cv_cc_gcc_supports_ada=no
# There is a bug in old released versions of GCC which causes the
# driver to exit successfully when the appropriate language module
# has not been installed.  This is fixed in 2.95.4, 3.0.2, and 3.1.
# Therefore we must check for the error message as well as an
# unsuccessful exit.
# Other compilers, like HP Tru64 UNIX cc, exit successfully when
# given a .adb file, but produce no object file.  So we must check
# if an object file was really produced to guard against this.
errors=`(${CC} $1[]m4_ifval([$1], [ ])-c conftest.adb) 2>&1 || echo failure`
if test x"$errors" = x && test -f conftest.$ac_objext; then
  acx_cv_cc_gcc_supports_ada=yes
fi
rm -f conftest.*])

if test x$GNATBIND != xno && test x$GNATMAKE != xno && test x$acx_cv_cc_gcc_supports_ada != xno; then
  have_gnat=yes
else
  have_gnat=no
fi
])

dnl 'make compare' can be significantly faster, if cmp itself can
dnl skip bytes instead of using tail.  The test being performed is
dnl "if cmp --ignore-initial=2 t1 t2 && ! cmp --ignore-initial=1 t1 t2"
dnl but we need to sink errors and handle broken shells.  We also test
dnl for the parameter format "cmp file1 file2 skip1 skip2" which is
dnl accepted by cmp on some systems.
AC_DEFUN([ACX_PROG_CMP_IGNORE_INITIAL],
[AC_CACHE_CHECK([how to compare bootstrapped objects], gcc_cv_prog_cmp_skip,
[ echo abfoo >t1
  echo cdfoo >t2
  gcc_cv_prog_cmp_skip='tail +16c $$f1 > tmp-foo1; tail +16c $$f2 > tmp-foo2; cmp tmp-foo1 tmp-foo2'
  if cmp t1 t2 2 2 > /dev/null 2>&1; then
    if cmp t1 t2 1 1 > /dev/null 2>&1; then
      :
    else
      gcc_cv_prog_cmp_skip='cmp $$f1 $$f2 16 16'
    fi
  fi
  if cmp --ignore-initial=2 t1 t2 > /dev/null 2>&1; then
    if cmp --ignore-initial=1 t1 t2 > /dev/null 2>&1; then
      :
    else
      gcc_cv_prog_cmp_skip='cmp --ignore-initial=16 $$f1 $$f2'
    fi
  fi
  rm t1 t2
])
do_compare="$gcc_cv_prog_cmp_skip"
AC_SUBST(do_compare)
])

dnl See whether we can include both string.h and strings.h.
AC_DEFUN([ACX_HEADER_STRING],
[AC_CACHE_CHECK([whether string.h and strings.h may both be included],
  gcc_cv_header_string,
[AC_TRY_COMPILE([#include <string.h>
#include <strings.h>], , gcc_cv_header_string=yes, gcc_cv_header_string=no)])
if test $gcc_cv_header_string = yes; then
  AC_DEFINE(STRING_WITH_STRINGS, 1, [Define if you can safely include both <string.h> and <strings.h>.])
fi
])

dnl See if stdbool.h properly defines bool and true/false.
dnl Check whether _Bool is built-in.
AC_DEFUN([ACX_HEADER_STDBOOL],
[AC_CACHE_CHECK([for working stdbool.h],
  ac_cv_header_stdbool_h,
[AC_TRY_COMPILE([#include <stdbool.h>],
[bool foo = false;],
ac_cv_header_stdbool_h=yes, ac_cv_header_stdbool_h=no)])
if test $ac_cv_header_stdbool_h = yes; then
  AC_DEFINE(HAVE_STDBOOL_H, 1,
  [Define if you have a working <stdbool.h> header file.])
fi
AC_CACHE_CHECK(for built-in _Bool, gcc_cv_c__bool,
[AC_TRY_COMPILE(,
[_Bool foo;],
gcc_cv_c__bool=yes, gcc_cv_c__bool=no)
])
if test $gcc_cv_c__bool = yes; then
  AC_DEFINE(HAVE__BOOL, 1, [Define if the \`_Bool' type is built-in.])
fi
])

dnl See if hard links work and if not, try to substitute $1 or simple copy.
AC_DEFUN([ACX_PROG_LN],
[AC_MSG_CHECKING(whether ln works)
AC_CACHE_VAL(acx_cv_prog_LN,
[rm -f conftestdata_t
echo >conftestdata_f
if ln conftestdata_f conftestdata_t 2>/dev/null
then
  acx_cv_prog_LN=ln
else
  acx_cv_prog_LN=no
fi
rm -f conftestdata_f conftestdata_t
])dnl
if test $acx_cv_prog_LN = no; then
  LN="ifelse([$1],,cp,[$1])"
  AC_MSG_RESULT([no, using $LN])
else
  LN="$acx_cv_prog_LN"
  AC_MSG_RESULT(yes)
fi
AC_SUBST(LN)dnl
])

dnl GCC_TARGET_TOOL(PROGRAM, TARGET-VAR, HOST-VAR, IN-TREE-TOOL, LANGUAGE)
AC_DEFUN([GCC_TARGET_TOOL],
[AC_MSG_CHECKING(where to find the target $1)
if test "x${build}" != "x${host}" ; then
  if expr "x[$]$2" : "x/" > /dev/null; then
    # We already found the complete path
    ac_dir=`dirname [$]$2`
    AC_MSG_RESULT(pre-installed in $ac_dir)
  else
    # Canadian cross, just use what we found
    AC_MSG_RESULT(pre-installed)
  fi
else
  ifelse([$4],,,
  [ok=yes
  case " ${configdirs} " in
    *" patsubst([$4], [/.*], []) "*) ;;
    *) ok=no ;;
  esac
  ifelse([$5],,, 
  [case ,${enable_languages}, in
    *,$5,*) ;;
    *) ok=no ;;
  esac])
  if test $ok = yes; then
    # An in-tree tool is available and we can use it
    $2='$$r/$(HOST_SUBDIR)/$4'
    AC_MSG_RESULT(just compiled)
  el])if expr "x[$]$2" : "x/" > /dev/null; then
    # We already found the complete path
    ac_dir=`dirname [$]$2`
    AC_MSG_RESULT(pre-installed in $ac_dir)
  elif test "x$target" = "x$host"; then
    # We can use an host tool
    $2='$($3)'
    AC_MSG_RESULT(host tool)
  else
    # We need a cross tool
    AC_MSG_RESULT(pre-installed)
  fi
fi
AC_SUBST($2)])


dnl Locate a program and check that its version is acceptable.
dnl ACX_PROG_CHECK_VER(var, name, version-switch,
dnl                    version-extract-regexp, version-glob)
AC_DEFUN([ACX_CHECK_PROG_VER],[
  AC_CHECK_PROG([$1], [$2], [$2])
  if test -n "[$]$1"; then
    # Found it, now check the version.
    AC_CACHE_CHECK([for modern $2],
                   [gcc_cv_prog_$2_modern],
                   [ac_prog_version=`eval [$]$1 $3 2>&1 |
                                     sed -n 's/^.*patsubst([[$4]],/,\/).*$/\1/p'`

                    [case $ac_prog_version in
                      '')  gcc_cv_prog_$2_modern=no;;
                      $5)  gcc_cv_prog_$2_modern=yes;;
                      *)   gcc_cv_prog_$2_modern=no;;
                    esac]
                   ])
  else
    gcc_cv_prog_$2_modern=no
  fi
  if test $gcc_cv_prog_$2_modern = no; then
    $1="${CONFIG_SHELL-/bin/sh} $ac_aux_dir/missing $2"
  fi
])

dnl Support the --with-pkgversion configure option.
dnl ACX_PKGVERSION(default-pkgversion)
AC_DEFUN([ACX_PKGVERSION],[
  AC_ARG_WITH(pkgversion,
    AS_HELP_STRING([--with-pkgversion=PKG],
                   [Use PKG in the version string in place of "$1"]),
    [case "$withval" in
      yes) AC_MSG_ERROR([package version not specified]) ;;
      no)  PKGVERSION= ;;
      *)   PKGVERSION="($withval) " ;;
     esac],
    PKGVERSION="($1) "
  )
  AC_SUBST(PKGVERSION)
])

dnl Support the --with-bugurl configure option.
dnl ACX_BUGURL(default-bugurl)
AC_DEFUN([ACX_BUGURL],[
  AC_ARG_WITH(bugurl,
    AS_HELP_STRING([--with-bugurl=URL],
                   [Direct users to URL to report a bug]),
    [case "$withval" in
      yes) AC_MSG_ERROR([bug URL not specified]) ;;
      no)  BUGURL=
	   ;;
      *)   BUGURL="$withval"
	   ;;
     esac],
     BUGURL="$1"
  )
  case ${BUGURL} in
  "")
    REPORT_BUGS_TO=
    REPORT_BUGS_TEXI=
    ;;
  *)
    REPORT_BUGS_TO="<$BUGURL>"
    REPORT_BUGS_TEXI=@uref{`echo "$BUGURL" | sed 's/@/@@/g'`}
    ;;
  esac;
  AC_SUBST(REPORT_BUGS_TO)
  AC_SUBST(REPORT_BUGS_TEXI)
])

dnl ####
dnl # ACX_CHECK_CYGWIN_CAT_WORKS
dnl # On Cygwin hosts, check that the cat command ignores 
dnl # carriage returns as otherwise builds will not work.
dnl # See binutils PR 4334 for more details.
AC_DEFUN([ACX_CHECK_CYGWIN_CAT_WORKS],[
AC_MSG_CHECKING([to see if cat works as expected])
echo a >cygwin-cat-check
if test `cat cygwin-cat-check` == a ; then
  rm cygwin-cat-check
  AC_MSG_RESULT(yes)
else
  rm cygwin-cat-check
  AC_MSG_RESULT(no)
  AC_MSG_ERROR([The cat command does not ignore carriage return characters.
  Please either mount the build directory in binary mode or run the following
  commands before running any configure script:
set -o igncr
export SHELLOPTS 
  ])
fi
])
dnl @synopsis GCC_HEADER_STDINT [( HEADER-TO-GENERATE [, HEADERS-TO-CHECK])]
dnl
dnl the "ISO C9X: 7.18 Integer types <stdint.h>" section requires the
dnl existence of an include file <stdint.h> that defines a set of
dnl typedefs, especially uint8_t,int32_t,uintptr_t.
dnl Many older installations will not provide this file, but some will
dnl have the very same definitions in <inttypes.h>. In other enviroments
dnl we can use the inet-types in <sys/types.h> which would define the
dnl typedefs int8_t and u_int8_t respectivly.
dnl
dnl This macros will create a local "_stdint.h" or the headerfile given as
dnl an argument. In many cases that file will pick the definition from a
dnl "#include <stdint.h>" or "#include <inttypes.h>" statement, while
dnl in other environments it will provide the set of basic 'stdint's defined:
dnl int8_t,uint8_t,int16_t,uint16_t,int32_t,uint32_t,intptr_t,uintptr_t
dnl int_least32_t.. int_fast32_t.. intmax_t
dnl which may or may not rely on the definitions of other files.
dnl
dnl Sometimes the stdint.h or inttypes.h headers conflict with sys/types.h,
dnl so we test the headers together with sys/types.h and always include it
dnl into the generated header (to match the tests with the generated file).
dnl Hopefully this is not a big annoyance.
dnl
dnl If your installed header files require the stdint-types you will want to
dnl create an installable file mylib-int.h that all your other installable
dnl header may include. So, for a library package named "mylib", just use
dnl      GCC_HEADER_STDINT(mylib-int.h)
dnl in configure.in and install that header file in Makefile.am along with
dnl the other headers (mylib.h).  The mylib-specific headers can simply
dnl use "#include <mylib-int.h>" to obtain the stdint-types.
dnl
dnl Remember, if the system already had a valid <stdint.h>, the generated
dnl file will include it directly. No need for fuzzy HAVE_STDINT_H things...
dnl
dnl @author  Guido Draheim <guidod@gmx.de>, Paolo Bonzini <bonzini@gnu.org>

AC_DEFUN([GCC_HEADER_STDINT],
[m4_define(_GCC_STDINT_H, m4_ifval($1, $1, _stdint.h))

inttype_headers=`echo inttypes.h sys/inttypes.h $2 | sed -e 's/,/ /g'`

acx_cv_header_stdint=stddef.h
acx_cv_header_stdint_kind="(already complete)"
for i in stdint.h $inttype_headers; do
  unset ac_cv_type_uintptr_t
  unset ac_cv_type_uintmax_t
  unset ac_cv_type_int_least32_t
  unset ac_cv_type_int_fast32_t
  unset ac_cv_type_uint64_t
  _AS_ECHO_N([looking for a compliant stdint.h in $i, ])
  AC_CHECK_TYPE(uintmax_t,[acx_cv_header_stdint=$i],continue,[#include <sys/types.h>
#include <$i>])
  AC_CHECK_TYPE(uintptr_t,,[acx_cv_header_stdint_kind="(mostly complete)"], [#include <sys/types.h>
#include <$i>])
  AC_CHECK_TYPE(int_least32_t,,[acx_cv_header_stdint_kind="(mostly complete)"], [#include <sys/types.h>
#include <$i>])
  AC_CHECK_TYPE(int_fast32_t,,[acx_cv_header_stdint_kind="(mostly complete)"], [#include <sys/types.h>
#include <$i>])
  AC_CHECK_TYPE(uint64_t,,[acx_cv_header_stdint_kind="(lacks uint64_t)"], [#include <sys/types.h>
#include <$i>])
  break
done
if test "$acx_cv_header_stdint" = stddef.h; then
  acx_cv_header_stdint_kind="(lacks uintmax_t)"
  for i in stdint.h $inttype_headers; do
    unset ac_cv_type_uintptr_t
    unset ac_cv_type_uint32_t
    unset ac_cv_type_uint64_t
    _AS_ECHO_N([looking for an incomplete stdint.h in $i, ])
    AC_CHECK_TYPE(uint32_t,[acx_cv_header_stdint=$i],continue,[#include <sys/types.h>
#include <$i>])
    AC_CHECK_TYPE(uint64_t,,,[#include <sys/types.h>
#include <$i>])
    AC_CHECK_TYPE(uintptr_t,,,[#include <sys/types.h>
#include <$i>])
    break
  done
fi
if test "$acx_cv_header_stdint" = stddef.h; then
  acx_cv_header_stdint_kind="(u_intXX_t style)"
  for i in sys/types.h $inttype_headers; do
    unset ac_cv_type_u_int32_t
    unset ac_cv_type_u_int64_t
    _AS_ECHO_N([looking for u_intXX_t types in $i, ])
    AC_CHECK_TYPE(u_int32_t,[acx_cv_header_stdint=$i],continue,[#include <sys/types.h>
#include <$i>])
    AC_CHECK_TYPE(u_int64_t,,,[#include <sys/types.h>
#include <$i>])
    break
  done
fi
if test "$acx_cv_header_stdint" = stddef.h; then
  acx_cv_header_stdint_kind="(using manual detection)"
fi

test -z "$ac_cv_type_uintptr_t" && ac_cv_type_uintptr_t=no
test -z "$ac_cv_type_uint64_t" && ac_cv_type_uint64_t=no
test -z "$ac_cv_type_u_int64_t" && ac_cv_type_u_int64_t=no
test -z "$ac_cv_type_int_least32_t" && ac_cv_type_int_least32_t=no
test -z "$ac_cv_type_int_fast32_t" && ac_cv_type_int_fast32_t=no

# ----------------- Summarize what we found so far

AC_MSG_CHECKING([what to include in _GCC_STDINT_H])

case `AS_BASENAME(_GCC_STDINT_H)` in
  stdint.h) AC_MSG_WARN([are you sure you want it there?]) ;;
  inttypes.h) AC_MSG_WARN([are you sure you want it there?]) ;;
  *) ;;
esac

AC_MSG_RESULT($acx_cv_header_stdint $acx_cv_header_stdint_kind)

# ----------------- done included file, check C basic types --------

# Lacking an uintptr_t?  Test size of void *
case "$acx_cv_header_stdint:$ac_cv_type_uintptr_t" in
  stddef.h:* | *:no) AC_CHECK_SIZEOF(void *) ;;
esac

# Lacking an uint64_t?  Test size of long
case "$acx_cv_header_stdint:$ac_cv_type_uint64_t:$ac_cv_type_u_int64_t" in
  stddef.h:*:* | *:no:no) AC_CHECK_SIZEOF(long) ;;
esac

if test $acx_cv_header_stdint = stddef.h; then
  # Lacking a good header?  Test size of everything and deduce all types.
  AC_CHECK_SIZEOF(int)
  AC_CHECK_SIZEOF(short)
  AC_CHECK_SIZEOF(char)

  AC_MSG_CHECKING(for type equivalent to int8_t)
  case "$ac_cv_sizeof_char" in
    1) acx_cv_type_int8_t=char ;;
    *) AC_MSG_ERROR(no 8-bit type, please report a bug)
  esac
  AC_MSG_RESULT($acx_cv_type_int8_t)

  AC_MSG_CHECKING(for type equivalent to int16_t)
  case "$ac_cv_sizeof_int:$ac_cv_sizeof_short" in
    2:*) acx_cv_type_int16_t=int ;;
    *:2) acx_cv_type_int16_t=short ;;
    *) AC_MSG_ERROR(no 16-bit type, please report a bug)
  esac
  AC_MSG_RESULT($acx_cv_type_int16_t)

  AC_MSG_CHECKING(for type equivalent to int32_t)
  case "$ac_cv_sizeof_int:$ac_cv_sizeof_long" in
    4:*) acx_cv_type_int32_t=int ;;
    *:4) acx_cv_type_int32_t=long ;;
    *) AC_MSG_ERROR(no 32-bit type, please report a bug)
  esac
  AC_MSG_RESULT($acx_cv_type_int32_t)
fi

# These tests are here to make the output prettier

if test "$ac_cv_type_uint64_t" != yes && test "$ac_cv_type_u_int64_t" != yes; then
  case "$ac_cv_sizeof_long" in
    8) acx_cv_type_int64_t=long ;;
  esac
  AC_MSG_CHECKING(for type equivalent to int64_t)
  AC_MSG_RESULT(${acx_cv_type_int64_t-'using preprocessor symbols'})
fi

# Now we can use the above types

if test "$ac_cv_type_uintptr_t" != yes; then
  AC_MSG_CHECKING(for type equivalent to intptr_t)
  case $ac_cv_sizeof_void_p in
    2) acx_cv_type_intptr_t=int16_t ;;
    4) acx_cv_type_intptr_t=int32_t ;;
    8) acx_cv_type_intptr_t=int64_t ;;
    *) AC_MSG_ERROR(no equivalent for intptr_t, please report a bug)
  esac
  AC_MSG_RESULT($acx_cv_type_intptr_t)
fi

# ----------------- done all checks, emit header -------------
AC_CONFIG_COMMANDS(_GCC_STDINT_H, [
if test "$GCC" = yes; then
  echo "/* generated for " `$CC --version | sed 1q` "*/" > tmp-stdint.h
else
  echo "/* generated for $CC */" > tmp-stdint.h
fi

sed 's/^ *//' >> tmp-stdint.h <<EOF

  #ifndef GCC_GENERATED_STDINT_H
  #define GCC_GENERATED_STDINT_H 1

  #include <sys/types.h>
EOF

if test "$acx_cv_header_stdint" != stdint.h; then
  echo "#include <stddef.h>" >> tmp-stdint.h
fi
if test "$acx_cv_header_stdint" != stddef.h; then
  echo "#include <$acx_cv_header_stdint>" >> tmp-stdint.h
fi

sed 's/^ *//' >> tmp-stdint.h <<EOF
  /* glibc uses these symbols as guards to prevent redefinitions.  */
  #ifdef __int8_t_defined
  #define _INT8_T
  #define _INT16_T
  #define _INT32_T
  #endif
  #ifdef __uint32_t_defined
  #define _UINT32_T
  #endif

EOF

# ----------------- done header, emit basic int types -------------
if test "$acx_cv_header_stdint" = stddef.h; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    #ifndef _UINT8_T
    #define _UINT8_T
    #ifndef __uint8_t_defined
    #define __uint8_t_defined
    typedef unsigned $acx_cv_type_int8_t uint8_t;
    #endif
    #endif

    #ifndef _UINT16_T
    #define _UINT16_T
    #ifndef __uint16_t_defined
    #define __uint16_t_defined
    typedef unsigned $acx_cv_type_int16_t uint16_t;
    #endif
    #endif

    #ifndef _UINT32_T
    #define _UINT32_T
    #ifndef __uint32_t_defined
    #define __uint32_t_defined
    typedef unsigned $acx_cv_type_int32_t uint32_t;
    #endif
    #endif

    #ifndef _INT8_T
    #define _INT8_T
    #ifndef __int8_t_defined
    #define __int8_t_defined
    typedef $acx_cv_type_int8_t int8_t;
    #endif
    #endif

    #ifndef _INT16_T
    #define _INT16_T
    #ifndef __int16_t_defined
    #define __int16_t_defined
    typedef $acx_cv_type_int16_t int16_t;
    #endif
    #endif

    #ifndef _INT32_T
    #define _INT32_T
    #ifndef __int32_t_defined
    #define __int32_t_defined
    typedef $acx_cv_type_int32_t int32_t;
    #endif
    #endif
EOF
elif test "$ac_cv_type_u_int32_t" = yes; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* int8_t int16_t int32_t defined by inet code, we do the u_intXX types */
    #ifndef _INT8_T
    #define _INT8_T
    #endif
    #ifndef _INT16_T
    #define _INT16_T
    #endif
    #ifndef _INT32_T
    #define _INT32_T
    #endif

    #ifndef _UINT8_T
    #define _UINT8_T
    #ifndef __uint8_t_defined
    #define __uint8_t_defined
    typedef u_int8_t uint8_t;
    #endif
    #endif

    #ifndef _UINT16_T
    #define _UINT16_T
    #ifndef __uint16_t_defined
    #define __uint16_t_defined
    typedef u_int16_t uint16_t;
    #endif
    #endif

    #ifndef _UINT32_T
    #define _UINT32_T
    #ifndef __uint32_t_defined
    #define __uint32_t_defined
    typedef u_int32_t uint32_t;
    #endif
    #endif
EOF
else
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* Some systems have guard macros to prevent redefinitions, define them.  */
    #ifndef _INT8_T
    #define _INT8_T
    #endif
    #ifndef _INT16_T
    #define _INT16_T
    #endif
    #ifndef _INT32_T
    #define _INT32_T
    #endif
    #ifndef _UINT8_T
    #define _UINT8_T
    #endif
    #ifndef _UINT16_T
    #define _UINT16_T
    #endif
    #ifndef _UINT32_T
    #define _UINT32_T
    #endif
EOF
fi

# ------------- done basic int types, emit int64_t types ------------
if test "$ac_cv_type_uint64_t" = yes; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* system headers have good uint64_t and int64_t */
    #ifndef _INT64_T
    #define _INT64_T
    #endif
    #ifndef _UINT64_T
    #define _UINT64_T
    #endif
EOF
elif test "$ac_cv_type_u_int64_t" = yes; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* system headers have an u_int64_t (and int64_t) */
    #ifndef _INT64_T
    #define _INT64_T
    #endif
    #ifndef _UINT64_T
    #define _UINT64_T
    #ifndef __uint64_t_defined
    #define __uint64_t_defined
    typedef u_int64_t uint64_t;
    #endif
    #endif
EOF
elif test -n "$acx_cv_type_int64_t"; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* architecture has a 64-bit type, $acx_cv_type_int64_t */
    #ifndef _INT64_T
    #define _INT64_T
    typedef $acx_cv_type_int64_t int64_t;
    #endif
    #ifndef _UINT64_T
    #define _UINT64_T
    #ifndef __uint64_t_defined
    #define __uint64_t_defined
    typedef unsigned $acx_cv_type_int64_t uint64_t;
    #endif
    #endif
EOF
else
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* some common heuristics for int64_t, using compiler-specific tests */
    #if defined __STDC_VERSION__ && (__STDC_VERSION__-0) >= 199901L
    #ifndef _INT64_T
    #define _INT64_T
    #ifndef __int64_t_defined
    typedef long long int64_t;
    #endif
    #endif
    #ifndef _UINT64_T
    #define _UINT64_T
    typedef unsigned long long uint64_t;
    #endif

    #elif defined __GNUC__ && defined (__STDC__) && __STDC__-0
    /* NextStep 2.0 cc is really gcc 1.93 but it defines __GNUC__ = 2 and
       does not implement __extension__.  But that compiler doesn't define
       __GNUC_MINOR__.  */
    # if __GNUC__ < 2 || (__NeXT__ && !__GNUC_MINOR__)
    # define __extension__
    # endif

    # ifndef _INT64_T
    # define _INT64_T
    __extension__ typedef long long int64_t;
    # endif
    # ifndef _UINT64_T
    # define _UINT64_T
    __extension__ typedef unsigned long long uint64_t;
    # endif

    #elif !defined __STRICT_ANSI__
    # if defined _MSC_VER || defined __WATCOMC__ || defined __BORLANDC__

    #  ifndef _INT64_T
    #  define _INT64_T
    typedef __int64 int64_t;
    #  endif
    #  ifndef _UINT64_T
    #  define _UINT64_T
    typedef unsigned __int64 uint64_t;
    #  endif
    # endif /* compiler */

    #endif /* ANSI version */
EOF
fi

# ------------- done int64_t types, emit intptr types ------------
if test "$ac_cv_type_uintptr_t" != yes; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* Define intptr_t based on sizeof(void*) = $ac_cv_sizeof_void_p */
    #ifndef __uintptr_t_defined
    typedef u$acx_cv_type_intptr_t uintptr_t;
    #endif
    #ifndef __intptr_t_defined
    typedef $acx_cv_type_intptr_t  intptr_t;
    #endif
EOF
fi

# ------------- done intptr types, emit int_least types ------------
if test "$ac_cv_type_int_least32_t" != yes; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* Define int_least types */
    typedef int8_t     int_least8_t;
    typedef int16_t    int_least16_t;
    typedef int32_t    int_least32_t;
    #ifdef _INT64_T
    typedef int64_t    int_least64_t;
    #endif

    typedef uint8_t    uint_least8_t;
    typedef uint16_t   uint_least16_t;
    typedef uint32_t   uint_least32_t;
    #ifdef _UINT64_T
    typedef uint64_t   uint_least64_t;
    #endif
EOF
fi

# ------------- done intptr types, emit int_fast types ------------
if test "$ac_cv_type_int_fast32_t" != yes; then
  dnl NOTE: The following code assumes that sizeof (int) > 1.
  dnl Fix when strange machines are reported.
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* Define int_fast types.  short is often slow */
    typedef int8_t       int_fast8_t;
    typedef int          int_fast16_t;
    typedef int32_t      int_fast32_t;
    #ifdef _INT64_T
    typedef int64_t      int_fast64_t;
    #endif

    typedef uint8_t      uint_fast8_t;
    typedef unsigned int uint_fast16_t;
    typedef uint32_t     uint_fast32_t;
    #ifdef _UINT64_T
    typedef uint64_t     uint_fast64_t;
    #endif
EOF
fi

if test "$ac_cv_type_uintmax_t" != yes; then
  sed 's/^ *//' >> tmp-stdint.h <<EOF

    /* Define intmax based on what we found */
    #ifdef _INT64_T
    typedef int64_t       intmax_t;
    #else
    typedef long          intmax_t;
    #endif
    #ifdef _UINT64_T
    typedef uint64_t      uintmax_t;
    #else
    typedef unsigned long uintmax_t;
    #endif
EOF
fi

sed 's/^ *//' >> tmp-stdint.h <<EOF

  #endif /* GCC_GENERATED_STDINT_H */
EOF

if test -r ]_GCC_STDINT_H[ && cmp -s tmp-stdint.h ]_GCC_STDINT_H[; then
  rm -f tmp-stdint.h
else
  mv -f tmp-stdint.h ]_GCC_STDINT_H[
fi

], [
GCC="$GCC"
CC="$CC"
acx_cv_header_stdint="$acx_cv_header_stdint"
acx_cv_type_int8_t="$acx_cv_type_int8_t"
acx_cv_type_int16_t="$acx_cv_type_int16_t"
acx_cv_type_int32_t="$acx_cv_type_int32_t"
acx_cv_type_int64_t="$acx_cv_type_int64_t"
acx_cv_type_intptr_t="$acx_cv_type_intptr_t"
ac_cv_type_uintmax_t="$ac_cv_type_uintmax_t"
ac_cv_type_uintptr_t="$ac_cv_type_uintptr_t"
ac_cv_type_uint64_t="$ac_cv_type_uint64_t"
ac_cv_type_u_int64_t="$ac_cv_type_u_int64_t"
ac_cv_type_u_int32_t="$ac_cv_type_u_int32_t"
ac_cv_type_int_least32_t="$ac_cv_type_int_least32_t"
ac_cv_type_int_fast32_t="$ac_cv_type_int_fast32_t"
ac_cv_sizeof_void_p="$ac_cv_sizeof_void_p"
])

])
