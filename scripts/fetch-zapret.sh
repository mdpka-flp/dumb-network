#!/usr/bin/env bash
# Downloads the official bol-van/zapret release and extracts only the files
# this client needs into backend/zapret/<version>/.
set -euo pipefail

VERSION="${ZAPRET_VERSION:-v72.13}"
REPO="https://github.com/bol-van/zapret"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/backend/zapret/$VERSION"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

crate="zapret-$VERSION.tar.gz"
echo "Downloading $crate ..."
curl -fL --retry 3 --max-time 300 -o "$TMP/$crate" "$REPO/releases/download/$VERSION/$crate"
curl -fL --retry 3 --max-time 60 -o "$TMP/sha256sum.txt" "$REPO/releases/download/$VERSION/sha256sum.txt"

echo "Extracting ..."
tar -xzf "$TMP/$crate" -C "$TMP"
SRC="$TMP/zapret-$VERSION"

# Verify every file we install against the release checksums.
(
  cd "$TMP"
  grep -E "zapret-$VERSION/(binaries/(linux-x86_64|linux-arm64)/nfqws|binaries/windows-x86_64/(winws.exe|WinDivert.dll|WinDivert64.sys|cygwin1.dll))" sha256sum.txt
) > "$TMP/selected.sha256"
(cd "$TMP" && sha256sum -c --quiet selected.sha256)

echo "Installing to $DEST ..."
mkdir -p "$DEST/linux-x64" "$DEST/linux-arm64" "$DEST/windows-x64" "$DEST/files"

install -m 755 "$SRC/binaries/linux-x86_64/nfqws" "$DEST/linux-x64/nfqws"
install -m 755 "$SRC/binaries/linux-arm64/nfqws" "$DEST/linux-arm64/nfqws"
for f in winws.exe WinDivert.dll WinDivert64.sys cygwin1.dll; do
  install -m 644 "$SRC/binaries/windows-x86_64/$f" "$DEST/windows-x64/$f"
done

rm -rf "$DEST/files/fake"
cp -R "$SRC/files/fake" "$DEST/files/fake"
install -m 644 "$SRC/docs/LICENSE.txt" "$DEST/LICENSE.txt"
printf '%s\n' "$VERSION" > "$DEST/VERSION"

echo "Done: $DEST"
du -sh "$DEST"
