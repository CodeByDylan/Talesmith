#!/bin/sh
# Starts the editor under Xvfb and passes once its window is open and it is still running ten seconds later. Needs xvfb-run and xdotool.
# Usage: test-start.sh <editor command>
set -eu

# shellcheck disable=SC2016
xvfb-run -a -s '-screen 0 1600x1000x24' sh -c '
    "$1" > /tmp/editor.log 2>&1 &
    pid=$!
    if ! timeout 120 xdotool search --sync --onlyvisible --name "^Talesmith$" > /dev/null; then
        echo "The editor opened no window" >&2
        cat /tmp/editor.log >&2
        kill "$pid" 2> /dev/null || true
        exit 1
    fi
    sleep 10
    if ! kill -0 "$pid" 2> /dev/null; then
        echo "The editor exited after opening its window" >&2
        cat /tmp/editor.log >&2
        exit 1
    fi
    kill "$pid"
    echo "The editor opened its window"
' sh "$1"
