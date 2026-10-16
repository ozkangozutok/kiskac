#!/bin/bash
# Kıskaç Linux derleme betiği. Gerekenler (Debian/Ubuntu):
#   sudo apt install build-essential cmake ninja-build qt6-base-dev libxcb1-dev libxcb-keysyms1-dev
# Çıktı: bin/linux/kiskac
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
build="$root/bin/linux-build"
cmake -S "$here" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build "$build"
mkdir -p "$root/bin/linux"
cp "$build/kiskac" "$root/bin/linux/kiskac"
"$root/bin/linux/kiskac" --export-icon "$root/bin/linux/kiskac.png" -platform offscreen || true
echo "OK: $root/bin/linux/kiskac ($("$root/bin/linux/kiskac" --version))"
