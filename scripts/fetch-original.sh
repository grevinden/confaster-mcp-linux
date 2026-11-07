#!/usr/bin/env bash
# Скачивает последний дистрибутив Confaster и распаковывает его в original/.
# Употребление: scripts/fetch-original.sh
set -euo pipefail

URL="https://confaster.ru/download/"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/original"
TMP="$(mktemp -d /tmp/opencode/confaster-fetch.XXXXXX)"
trap 'rm -rf "$TMP"' EXIT

echo "Скачиваю $URL ..."
curl -fSL -o "$TMP/Confaster.zip" "$URL"

SHA="$(sha256sum "$TMP/Confaster.zip" | cut -d' ' -f1)"
echo "sha256: $SHA"

unzip -o -q "$TMP/Confaster.zip" -d "$TMP/extracted"

rm -rf "$DEST"
mkdir -p "$DEST"
cp -r "$TMP/extracted/." "$DEST/"
echo "$SHA  Confaster.zip  $(date -u +%FT%TZ)" > "$DEST/CHECKSUM"

echo "Распаковано в $DEST"
