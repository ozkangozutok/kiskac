#!/bin/bash
# Kıskaç macOS derleme betiği. Yalnızca Xcode Komut Satırı Araçları gerekir (xcode-select --install).
# Çıktı: bin/Kiskac.app (Apple Silicon + Intel, evrensel)
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$root/bin"
app="$out/Kiskac.app"
tmp="$out/.build-macos"
archs="${ARCHS:-arm64 x86_64}"
min_os="14.0"

pkill -x Kiskac 2>/dev/null || true
rm -rf "$app" "$tmp"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$tmp"

sdk="$(xcrun --sdk macosx --show-sdk-path)"
bins=()
for arch in $archs; do
    echo "Derleniyor: $arch"
    xcrun swiftc -O -whole-module-optimization -swift-version 5 \
        -sdk "$sdk" -target "$arch-apple-macos$min_os" \
        -o "$tmp/Kiskac-$arch" "$here"/Sources/*.swift
    bins+=("$tmp/Kiskac-$arch")
done
lipo -create -output "$app/Contents/MacOS/Kiskac" "${bins[@]}"
cp "$here/Info.plist" "$app/Contents/Info.plist"

# Uygulama simgesi koddan (Sources/Logo.swift) üretilir.
"$app/Contents/MacOS/Kiskac" --export-iconset "$tmp/Kiskac.iconset"
iconutil -c icns -o "$app/Contents/Resources/Kiskac.icns" "$tmp/Kiskac.iconset"

# Varsayılan yerel (ad-hoc) imza. SIGN_IDENTITY verilirse (ör. Developer ID) güvenli zaman damgası da eklenir.
identity="${SIGN_IDENTITY:--}"
if [ "$identity" = "-" ]; then
    codesign --force --sign - --options runtime "$app"
else
    codesign --force --sign "$identity" --options runtime --timestamp "$app"
fi
codesign --verify --strict "$app"

rm -rf "$tmp"
echo "OK: $app"
