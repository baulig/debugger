#!/bin/sh

PREFIX=`pwd`/BUILD

if test -e sysdeps/server/mdb-server.c; then
	echo Building
else
	echo You must run this from the Mono directory.
	exit 1
fi

if [ -z "$ANDROID_NDK_PATH" -o -z "$ANDROID_HOST_PLATFORM" ]; then
	echo You mustset the ANDROID_NDK_PATH and ANDROID_HOST_PLATFORM environment variables.
	exit 1
fi

mkdir -p BUILD
mkdir -p BUILD/host
mkdir -p BUILD/arm

autoreconf -i
pushd sysdeps/eglib
autoreconf -i
popd
pushd sysdeps/bfd
autoreconf -i
popd

NDK=$ANDROID_NDK_PATH
TOOLCHAIN=$NDK/build/prebuilt/$ANDROID_HOST_PLATFORM/arm-eabi-4.2.1
NDK_PLATFORM_ARCH=$NDK/build/platforms/android-4/arch-arm

HACK_INC=`pwd`/hack-inc
mkdir -p "$HACK_INC/arch"
touch "$HACK_INC/arch/syslimits.h"
cp "`dirname $0`/../../link.h" "$HACK_INC"

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
CFLAGS="-DARM_FPU_NONE=1 -DPAGE_SIZE=0x400 -DS_IWRITE=S_IWUSR -D__POSIX_VISIBLE=201002 -D__ARM_ARCH_5__ -D__ARM_ARCH_5T__ -D__ARM_ARCH_5TE__ -DPLATFORM_ANDROID -Dlinux -D__linux__ -DSK_RELEASE -DNDEBUG -UDEBUG -march=armv5te -fpic -g -I$NDK_PLATFORM_ARCH/usr/include/ -I$HACK_INC"
CXXFLAGS="-DARM_FPU_NONE=1 -DPAGE_SIZE=0x400 -DS_IWRITE=S_IWUSR -D__POSIX_VISIBLE=201002 -D__ARM_ARCH_5__ -D__ARM_ARCH_5T__ -D__ARM_ARCH_5TE__ -DPLATFORM_ANDROID -Dlinux -D__linux__ -DSK_RELEASE -DNDEBUG -UDEBUG -march=armv5te -fpic -g -I$NDK_PLATFORM_ARCH/usr/include/ -I$HACK_INC"
LDFLAGS="-Wl,-T,$TOOLCHAIN/arm-eabi/lib/ldscripts/armelf.x,-rpath-link=$NDK_PLATFORM_ARCH/usr/lib,-dynamic-linker=/system/bin/linker -L$NDK_PLATFORM_ARCH/usr/lib -ldl -lm -llog -lc"

#pushd BUILD/arm
#/work/sources/binutils-2.20/bfd/configure --host=arm-eabi-linux CC="$CC" CXX="$CXX" CPP="$CPP" CXXCPP="$CXXCPP" LD=$LD AR=$AR AS=$AS RANLIB=$R#ANLIB STRIP=$STRIP CFLAGS="$CFLAGS" CXXFLAGS="$CXXFLAGS" LDFLAGS="$LDFLAGS" PATH="$PATH" --prefix="$PREFIX/arm/install"
#make all install

pushd BUILD/arm
../../configure --with-backend=server-only --host=arm-eabi-linux CC="$CC" CXX="$CXX" CPP="$CPP" CXXCPP="$CXXCPP" LD=$LD AR=$AR AS=$AS RANLIB=$RANLIB STRIP=$STRIP CFLAGS="$CFLAGS" CXXFLAGS="$CXXFLAGS" LDFLAGS="$LDFLAGS" PATH="$PATH" --prefix="$PREFIX/arm/install"
make all install
popd

