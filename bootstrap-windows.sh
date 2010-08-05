#!/bin/bash -e
CURDIR="`pwd`"
MINGW=i386-mingw32msvc
CROSS_DIR=/opt/cross/$MINGW
COPY_DLLS=""
INSTALL_DESTDIR="$CURDIR/BUILD/windows"
TEMPORARY_PKG_CONFIG_DIR=/tmp/$RANDOM-pkg-config-$RANDOM
ORIGINAL_PATH="$PATH"

export CPPFLAGS_FOR_EGLIB CFLAGS_FOR_EGLIB CPPFLAGS_FOR_LIBGC CFLAGS_FOR_LIBGC

function cleanup ()
{
    if [ -d "$TEMPORARY_PKG_CONFIG_DIR" ]; then
	rm -rf "$TEMPORARY_PKG_CONFIG_DIR"
    fi
}

function setup ()
{
    local pcname

    CROSS_BIN_DIR="$CROSS_DIR/bin"
    CROSS_DLL_DIR="$CROSS_DIR/bin"
    CROSS_PKG_CONFIG_DIR=$CROSS_DIR/lib/pkgconfig
    PATH=$CROSS_BIN_DIR:$PATH

    export PATH
    NOCONFIGURE=yes
    export NOCONFIGURE

    if [ -d "$CROSS_PKG_CONFIG_DIR" ]; then
	install -d -m 755 "$TEMPORARY_PKG_CONFIG_DIR"
	for pc in "$CROSS_PKG_CONFIG_DIR"/*.pc; do
	    pcname="`basename $pc`"
	    sed -e "s;^prefix=.*;prefix=$CROSS_DIR;g" < $pc > "$TEMPORARY_PKG_CONFIG_DIR"/$pcname
	done
	CROSS_PKG_CONFIG_DIR="$TEMPORARY_PKG_CONFIG_DIR"
    fi
}

function build ()
{
    ./autogen.sh 

    BUILD="`./config.guess`"

    if [ -f ./Makefile ]; then
	make distclean
	rm -rf autom4te.cache
    fi

    if [ ! -d "$CURDIR/BUILD" ]; then
	mkdir "$CURDIR/BUILD"
    fi

    if [ ! -d "$CURDIR/BUILD/cross-windows" ]; then
	mkdir "$CURDIR/BUILD/cross-windows"
    fi

    if [ ! -d "$CURDIR/BUILD/host" ]; then
	mkdir "$CURDIR/BUILD/host"
    fi

    cd "$CURDIR/BUILD/cross-windows"
    rm -rf *
    ../../configure --with-crosspkgdir=$CROSS_PKG_CONFIG_DIR --build=$BUILD --target=$MINGW --host=$MINGW --with-backend=server-only
    make
    cd "$CURDIR"

    cd "$CURDIR/BUILD/host"
    rm -rf *
    ../../configure --with-backend=remote-only
    make
    cd "$CURDIR"

    rm -rf autom4te.cache
    unset PATH
    PATH="$ORIGINAL_PATH"
    export PATH
}

function doinstall ()
{
    if [ -d "$INSTALL_DIR" ]; then
	rm -rf "$INSTALL_DIR"
    fi
    cd "$CURDIR/BUILD/cross-windows"
    make DESTDIR="$INSTALL_DESTDIR" USE_BATCH_FILES=yes install

    cd "$CURDIR/BUILD/host"
    make DESTDIR="$INSTALL_DESTDIR" USE_BATCH_FILES=yes install
}

function usage ()
{
    cat <<EOF
Usage: build-mingw32.sh [OPTIONS]

where OPTIONS are:

 -d DIR     Sets the location of directory where MINGW is installed [$CROSS_DIR]
 -m MINGW   Sets the MINGW target name to be passed to configure [$MINGW]
EOF

    exit 1
}

trap cleanup 0

pushd . > /dev/null

while getopts "d:m:h" opt; do
    case "$opt" in
	d) CROSS_DIR="$OPTARG" ;;
	m) MINGW="$OPTARG" ;;
	*) usage ;;
    esac
done

setup
build
doinstall

popd > /dev/null
