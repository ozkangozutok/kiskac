#!/bin/bash
# Kıskaç macOS sürümünü yayınlar: Developer ID ile imzalar, notarytool profili varsa Apple'a
# notarize ettirir, Kiskac-macOS.zip üretir ve GitHub'da vX.Y.Z sürümü oluşturur. Uygulamadaki
# güncelleyici en son sürümün Kiskac-macOS.zip dosyasını indirir.
#
# Kullanım: ./macos/release.sh [sürüm-notları.md]
# Gerekenler:
#   - Keychain'de "Developer ID Application" sertifikası
#   - İsteğe bağlı notarytool profili (bir kez, parolayı sorar): xcrun notarytool store-credentials kiskac-notary \
#         --apple-id <apple-id> --team-id XZPCTAZZ9W
#     Profil yoksa notarize atlanır; ilk elle kurulumda Gatekeeper uyarısı çıkar, güncellemeler etkilenmez.
#   - gh (GitHub CLI) oturumu
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
profile="${NOTARY_PROFILE:-kiskac-notary}"
version="$(tr -d '[:space:]' < "$root/VERSION")"
tag="v$version"
notes="${1:-}"

if git -C "$root" rev-parse -q --verify "refs/tags/$tag" >/dev/null || gh release view "$tag" >/dev/null 2>&1; then
    echo "$tag zaten var. Önce kökteki VERSION dosyasını artır." >&2
    exit 1
fi
if [ -n "$(git -C "$root" status --porcelain)" ]; then
    echo "Çalışma dizininde commit edilmemiş değişiklik var." >&2
    exit 1
fi

identity="${SIGN_IDENTITY:-$(security find-identity -v -p codesigning | awk -F'"' '/Developer ID Application/ {print $2; exit}')}"
[ -n "$identity" ] || { echo "Developer ID Application sertifikası bulunamadı." >&2; exit 1; }
notarize=0
if xcrun notarytool history --keychain-profile "$profile" >/dev/null 2>&1; then
    notarize=1
else
    echo "Uyarı: notarytool profili '$profile' yok, notarize atlanıyor."
fi

SIGN_IDENTITY="$identity" "$here/build.sh"

out="$root/bin/release"
rm -rf "$out"
mkdir -p "$out"
zip="$out/Kiskac-macOS.zip"

if [ "$notarize" = 1 ]; then
    echo "Notarize ediliyor…"
    ditto -c -k --keepParent "$root/bin/Kiskac.app" "$zip"
    xcrun notarytool submit "$zip" --keychain-profile "$profile" --wait
    xcrun stapler staple "$root/bin/Kiskac.app"
    rm "$zip"
    spctl --assess --type execute -v "$root/bin/Kiskac.app"
fi
ditto -c -k --keepParent "$root/bin/Kiskac.app" "$zip"

# Kiskac.exe'yi sürüm yayınlanınca GitHub Actions (.github/workflows/windows.yml) derleyip ekler.
assets=("$zip")

if [ -z "$notes" ]; then
    notes="$out/notes.md"
    {
        echo "Kıskaç $version"
        echo
        echo "Kıskaç kendini otomatik günceller (menü › Güncellemeleri denetle…). Kiskac.exe birkaç dakika içinde GitHub Actions tarafından eklenir."
        echo
        for f in "${assets[@]}"; do
            echo "**SHA256 ($(basename "$f")):** \`$(shasum -a 256 "$f" | cut -d' ' -f1)\`"
        done
    } > "$notes"
fi

git -C "$root" push origin HEAD
gh release create "$tag" --target "$(git -C "$root" rev-parse HEAD)" --title "Kıskaç $tag" --notes-file "$notes" "${assets[@]}"
