#!/bin/sh
# Points the Nix package at a published release, such as: packaging/nix/update.sh 0.2.0
set -eu

version=$1
url="https://github.com/CodeByDylan/Talesmith/releases/download/v$version/talesmith-$version-linux-x64.tar.gz"
hash=$(nix store prefetch-file --json "$url" | sed -n 's/.*"hash":"\([^"]*\)".*/\1/p')
printf '{\n  "version": "%s",\n  "hash": "%s"\n}\n' "$version" "$hash" > "$(dirname "$0")/release.json"
