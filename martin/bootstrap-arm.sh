#!/bin/sh

PREFIX=`pwd`/BUILD

if test -e mono/mini/mini.c; then
	echo Building
else
	echo You must run this from the Mono directory.
	exit 1
fi

if [ -z "$ANDROID_NDK_PATH" -o -z "$ANDROID_HOST_PLATFORM" ]; then
	echo You mustset the ANDROID_NDK_PATH and ANDROID_HOST_PLATFORM environment variables.
	exit 1
fi

BUILD_ARMEABI=0
BUILD_ARMEABI_V7A=0
BUILD_X86=0
BUILD_CLASSLIB=0

HAVE_BUILD=0

while getopts "B:" options; do
	case "$options" in
		B)
			HAVE_BUILD=1
			case $OPTARG in
				armeabi)
					BUILD_ARMEABI=1
					;;
				armeabi-v7a)
					BUILD_ARMEABI=1
					;;
				Linux | linux | Darwin | darwin)
					BUILD_X86=1
					;;
				classlib)
					BUILD_X86=1
					BUILD_CLASSLIB=1
					;;
			esac
			;;
	esac
done

DIST_NAME=""

if [ $HAVE_BUILD -eq 0 ]; then
	BUILD_ARMEABI=1
	BUILD_ARMEABI_V7A=1
	BUILD_X86=1
	BUILD_CLASSLIB=1
else
	if [ $BUILD_ARMEABI -eq 1 ]; then
		DIST_NAME="$DIST_NAME-armeabi"
	fi
	if [ $BUILD_ARMEABI -eq 1 ]; then
		DIST_NAME="$DIST_NAME-armeabi_v7a"
	fi
	if [ $BUILD_X86 -eq 1 ]; then
		DIST_NAME="$DIST_NAME-`uname`"
	fi
	if [ $BUILD_CLASSLIB -eq 1 ]; then
		DIST_NAME="$DIST_NAME-classlib"
	fi
fi

mkdir -p BUILD
mkdir -p BUILD/x86
mkdir -p BUILD/armeabi
mkdir -p BUILD/armeabi-v7a

#if ! patch -p1 < "`dirname $0`/mono.patch" ; then
#	echo "Patch did not apply cleanly!  Please fix."
#	exit 1
#fi

#autoreconf -i
#pushd eglib
#autoreconf -i
#popd

NOCONFIGURE=y ./autogen.sh

NDK=$ANDROID_NDK_PATH
TOOLCHAIN=$NDK/build/prebuilt/$ANDROID_HOST_PLATFORM/arm-eabi-4.4.0
NDK_PLATFORM_ARCH=$NDK/build/platforms/android-8/arch-arm

HACK_INC=`pwd`/hack-inc
mkdir -p "$HACK_INC/arch"
touch "$HACK_INC/arch/syslimits.h"
cp "/work/catron/mondroid/link.h" "$HACK_INC"

PATH="$PATH:$TOOLCHAIN/bin"
CXX="$TOOLCHAIN/bin/arm-eabi-g++ -nostdlib"
CXXCPP="$TOOLCHAIN/bin/arm-eabi-cpp -I$NDK_PLATFORM_ARCH/usr/include/"
CC="$TOOLCHAIN/bin/arm-eabi-gcc -nostdlib"
CPP="$TOOLCHAIN/bin/arm-eabi-cpp -I$NDK_PLATFORM_ARCH/usr/include/"
LD=$TOOLCHAIN/bin/arm-eabi-ld
AS=$TOOLCHAIN/bin/arm-eabi-as
AR=$TOOLCHAIN/bin/arm-eabi-ar
RANLIB=$TOOLCHAIN/bin/arm-eabi-ranlib
STRIP=$TOOLCHAIN/bin/arm-eabi-strip
ARM_CFLAGS="-DPAGE_SIZE=0x1000 -DS_IWRITE=S_IWUSR -D__POSIX_VISIBLE=201002 -DPLATFORM_ANDROID -Dlinux -D__linux__ -DSK_RELEASE -DNDEBUG -UDEBUG -fpic -g -I$NDK_PLATFORM_ARCH/usr/include/ -I$HACK_INC"
ARM_LDFLAGS="-Wl,-T,$TOOLCHAIN/arm-eabi/lib/ldscripts/armelf.x,-rpath-link=$NDK_PLATFORM_ARCH/usr/lib,-dynamic-linker=/system/bin/linker -L$NDK_PLATFORM_ARCH/usr/lib -ldl -lm -llog -lc"

if [ $BUILD_ARMEABI -eq 1 ]; then

	pushd BUILD/armeabi
	ARMEABI_CFLAGS="$ARM_CFLAGS -DARM_FPU_NONE=1 -D__ARM_ARCH_5__ -D__ARM_ARCH_5T__ -D__ARM_ARCH_5TE__ -march=armv5te"
	../../configure --enable-nls=no --disable-mcs-build --with-sigaltstack=no --with-tls=pthread \
		--with-glib=embedded --host=arm-eabi-linux --without-sgen --enable-static --disable-shared \
		CC="$CC" CXX="$CXX" CPP="$CPP" CXXCPP="$CXXCPP" LD=$LD AR=$AR AS=$AS \
		RANLIB=$RANLIB STRIP=$STRIP \
		CFLAGS="$ARMEABI_CFLAGS" CXXFLAGS="$ARMEABI_CFLAGS" \
		LDFLAGS="$ARM_LDFLAGS" PATH="$PATH" \
		mono_cv_uscore=yes --prefix="$PREFIX/armeabi/install" || exit 1
	make all install &
	popd

fi

if [ $BUILD_ARMEABI_V7A -eq 1 ]; then

	pushd BUILD/armeabi-v7a
	ARMEABI_V7A_CFLAGS="$ARM_CFLAGS -DARM_FPU_VFP=1 -mtune=cortex-a8 -march=armv7-a -mfpu=vfpv3"
	../../configure --enable-nls=no --disable-mcs-build --with-sigaltstack=no --with-tls=pthread \
		--with-glib=embedded --host=arm-eabi-linux \
		CC="$CC" CXX="$CXX" CPP="$CPP" CXXCPP="$CXXCPP" LD=$LD AR=$AR AS=$AS \
		RANLIB=$RANLIB STRIP=$STRIP \
		CFLAGS="$ARMEABI_V7A_CFLAGS" CXXFLAGS="$ARMEABI_V7A_CFLAGS" \
		LDFLAGS="$ARM_LDFLAGS" PATH="$PATH" \
		mono_cv_uscore=yes --prefix="$PREFIX/armeabi-v7a/install" || exit 1
	make all install &
	popd

fi

if [ $BUILD_X86 -eq 1 ]; then

	pushd BUILD/x86
	MONO_HOST_FLAGS="--target=i586 --host=i586-pc-linux-gnu CFLAGS=-m32 CXXFLAGS=-m32"
	if [ `uname` = "Darwin" ]; then
		MONO_HOST_FLAGS="--host=i386-apple-darwin10.0.0"
	fi

	if [ $BUILD_CLASSLIB -eq 0 ]; then
		MONO_HOST_FLAGS="$MONO_HOST_FLAGS --disable-mcs-build"
	fi
	../../configure --enable-nls=no --prefix="$PREFIX/x86/install" --with-monodroid=yes --with-profile4=no --with-moonlight=no --with-glib=embedded --with-mcs-docs=no $MONO_HOST_FLAGS || exit 1
	make all install &
	popd

fi

wait

#patch -R -p1 < "`dirname $0`/mono.patch" || exit 1

rm -Rf BUILD/dist
mkdir -p BUILD/dist
mkdir -p BUILD/dist/bin
cp BUILD/x86/install/bin/mono                      BUILD/dist/bin/mono-`uname`

if [ $BUILD_ARMEABI -eq 1 ]; then
	mkdir -p BUILD/dist/include
	cp -r BUILD/armeabi/install/include/mono-2.0/mono       BUILD/dist/include

	mkdir -p BUILD/dist/include/eglib
	cp    eglib/src/*.h                                     BUILD/dist/include/eglib

	mkdir -p BUILD/dist/lib/armeabi
	cp    BUILD/armeabi/install/lib/libmono-2.0.a           BUILD/dist/lib/armeabi/libmono-2.0.a
	cp    BUILD/armeabi/install/lib/libmono-2.0.so          BUILD/dist/lib/armeabi/libmono-2.0.so

	mkdir -p BUILD/dist/lib/armeabi/include
	cp -r BUILD/armeabi/eglib/src/*.h                       BUILD/dist/lib/armeabi/include
fi

if [ $BUILD_ARMEABI_V7A -eq 1 ]; then
	mkdir -p BUILD/dist/lib/armeabi-v7a
	cp    BUILD/armeabi-v7a/install/lib/libmono-2.0.a       BUILD/dist/lib/armeabi-v7a/libmono-2.0.a
	cp    BUILD/armeabi-v7a/install/lib/libmono-2.0.so      BUILD/dist/lib/armeabi-v7a/libmono-2.0.so

	mkdir -p BUILD/dist/lib/armeabi-v7a/include
	cp -r BUILD/armeabi-v7a/eglib/src/*.h                   BUILD/dist/lib/armeabi-v7a/include
fi

# Only distribute x86 binaries built on Linux, as OSX-built binaries are
# Mach-O, not ELF
if [ $BUILD_X86 -eq 1 -a `uname` = "Linux" ]; then
	mkdir -p BUILD/dist/lib/x86
	cp    BUILD/x86/install/lib/libmono-2.0.a               BUILD/dist/lib/x86/libmono-2.0.a
	cp    BUILD/x86/install/lib/libmono-2.0.so              BUILD/dist/lib/x86/libmono-2.0.so
                                                         
	mkdir -p BUILD/dist/lib/x86/include                    
	cp -r BUILD/x86/eglib/src/*.h                           BUILD/dist/lib/x86/include
fi

if [ $BUILD_X86 -eq 1 -a $BUILD_CLASSLIB -eq 1 ]; then
	mkdir -p BUILD/dist/lib/mono/2.1
	cp    mcs/class/lib/monodroid/*                         BUILD/dist/lib/mono/2.1

	mkdir -p BUILD/dist/docs/assemblies

	cp    `dirname $0`/smcs         BUILD/dist/bin
	chmod +x                        BUILD/dist/bin/smcs
	cp mcs/docs/netdocs.source      BUILD/dist/docs

	for a in BUILD/dist/lib/mono/2.1/*.dll; do
		an=`basename "$a" .dll`
		dd="BUILD/dist/docs/assemblies/$an"
		if [ "$an" = "mscorlib" ]; then
			an="corlib"
		elif [ "$an" = "System.Xml" ]; then
			an="System.XML"
		fi
		sd="mcs/class/$an/Documentation/en"
		if [ ! -d "$sd" ]; then
			continue
		fi
		rsync -auz "$sd/" "$dd"
	done
fi


(cd BUILD/dist && zip -r ../../dist-`date +%Y%m%dT%H%M%S`$DIST_NAME .)

