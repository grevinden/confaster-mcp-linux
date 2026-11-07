#!/usr/bin/env bash
# Собирает бинарный архив с self-contained публикацией порта.
#
# Использование: scripts/build-tarball.sh [amd64|arm64]   (по умолчанию amd64)
# Результат: build/confaster-mcp_<версия>_linux-<арх>.tar.gz
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
ARCH="${1:-amd64}"
case "$ARCH" in
    amd64) RID=linux-x64;  CC=gcc ;;
    arm64) RID=linux-arm64; CC=aarch64-linux-gnu-gcc ;;
    *) echo "Неизвестная архитектура: $ARCH (amd64|arm64)" >&2; exit 1 ;;
esac

OUT="$ROOT/build/tarball/$RID"
rm -rf "$OUT"
dotnet publish "$ROOT/src/ConfasterMCP" -c Release -r "$RID" \
    --self-contained true -p:PublishReadyToRun=true -o "$OUT"
CC="$CC" "$ROOT/scripts/build-tree-sitter.sh" "$OUT"
cp -r "$ROOT/data/1c-help" "$OUT/1c-help"

mkdir -p "$ROOT/build"
tar -czf "$ROOT/build/confaster-mcp_${VERSION}_linux-${ARCH}.tar.gz" -C "$OUT" .
echo "Готово: $ROOT/build/confaster-mcp_${VERSION}_linux-${ARCH}.tar.gz"
