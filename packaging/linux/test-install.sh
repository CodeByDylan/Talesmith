#!/bin/sh
# Installs a Linux release the way a user does, as root in a clean container, then checks that the exported Lantern Grove runs and that
# the editor opens its window. The archive is installed from the packages its launcher names as missing.
# Usage: test-install.sh deb|archive <folder with the release files>
set -eu

kind=$1
release=$(readlink -f "$2")

ids=$(. /etc/os-release && echo "$ID ${ID_LIKE:-}")
case " $ids " in
    *" debian "* | *" ubuntu "*) family=apt ;;
    *" fedora "*) family=dnf ;;
    *" arch "*) family=pacman ;;
    *) echo "No package manager is known for $ids" >&2; exit 1 ;;
esac

install() {
    case $family in
        apt) DEBIAN_FRONTEND=noninteractive apt-get install -y -q "$@" ;;
        dnf) dnf install -y -q "$@" ;;
        pacman) pacman -S --noconfirm --needed -q "$@" ;;
    esac
}

case $family in
    apt) apt-get update -q ;;
    pacman) pacman -Syu --noconfirm -q ;;
esac
install tar gzip findutils

echo "Running Lantern Grove before anything is installed"
mkdir -p /tmp/game
tar -xzf "$release"/lantern-grove-*-linux-x64.tar.gz -C /tmp/game
game=$(find /tmp/game -type f -name 'Lantern Grove' | head -n 1)
"$game" --benchmark --frames 120 --warmup 30 --no-render --report /tmp/game/benchmark.json
test -s /tmp/game/benchmark.json

if [ "$kind" = deb ]; then
    install "$release"/talesmith_*_amd64.deb
    editor=talesmith
else
    tar -xzf "$release"/talesmith-*-linux-x64.tar.gz -C /opt
    editor=$(echo /opt/talesmith-*-linux-x64/talesmith)
    if "$editor" --check > /tmp/check.txt; then
        cat /tmp/check.txt
        echo "The check found nothing missing on a bare system" >&2
        exit 1
    fi
    cat /tmp/check.txt
    # The check prints a line such as "  sudo dnf install fontconfig libX11"; the words after the manager's command are the packages.
    # shellcheck disable=SC2046
    set -- $(sed -n 's/^  sudo //p' /tmp/check.txt)
    shift 2
    install "$@"
fi
"$editor" --check

case $family in
    apt) install xvfb xauth xdotool ;;
    dnf) install xorg-x11-server-Xvfb xorg-x11-xauth xdotool ;;
    pacman) install xorg-server-xvfb xorg-xauth xdotool ;;
esac

sh "$(dirname "$0")/test-start.sh" "$editor"
