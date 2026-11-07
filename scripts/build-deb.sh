#!/usr/bin/env bash
# Собирает .deb с портом: self-contained публикация + нативная библиотека
# tree-sitter + обёртка /usr/bin/confaster-mcp + документация.
#
# Использование: scripts/build-deb.sh [amd64|arm64]   (по умолчанию amd64)
# Требуется: dotnet 8 SDK, gcc (для arm64 — aarch64-linux-gnu-gcc), dpkg-deb.
# Результат: build/confaster-mcp_<версия>_<арх>.deb
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
ARCH="${1:-amd64}"
case "$ARCH" in
    amd64) RID=linux-x64;  CC=gcc ;;
    arm64) RID=linux-arm64; CC=aarch64-linux-gnu-gcc ;;
    *) echo "Неизвестная архитектура: $ARCH (amd64|arm64)" >&2; exit 1 ;;
esac

STAGE="$ROOT/build/deb/confaster-mcp_${VERSION}_${ARCH}"
APP="$STAGE/usr/lib/confaster-mcp"
rm -rf "$STAGE"
mkdir -p "$APP" "$STAGE/usr/bin" "$STAGE/usr/share/doc/confaster-mcp" \
         "$STAGE/usr/share/man/man1" "$STAGE/DEBIAN"

dotnet publish "$ROOT/src/ConfasterMCP" -c Release -r "$RID" \
    --self-contained true -p:PublishReadyToRun=true -o "$APP"
CC="$CC" "$ROOT/scripts/build-tree-sitter.sh" "$APP"

install -m 0755 "$ROOT/packaging/confaster-mcp" "$STAGE/usr/bin/confaster-mcp"
install -m 0644 "$ROOT/CHANGELOG.md" "$STAGE/usr/share/doc/confaster-mcp/CHANGELOG.md"
if [ -f "$ROOT/.github/README.md" ]; then
    install -m 0644 "$ROOT/.github/README.md" "$STAGE/usr/share/doc/confaster-mcp/README.md"
fi
gzip -9c "$ROOT/packaging/confaster-mcp.1" > "$STAGE/usr/share/man/man1/confaster-mcp.1.gz"
chmod 0644 "$STAGE/usr/share/man/man1/confaster-mcp.1.gz"

cat > "$STAGE/usr/share/doc/confaster-mcp/copyright" <<'EOF'
ConfasterMCP — порт MCP-сервера Confaster (1c-search) для Linux.

Исходный продукт: Confaster, https://confaster.ru/ — права принадлежат авторам.
Сервер получен декомпиляцией оригинального дистрибутива и доработкой под Linux.
Порт распространяется как есть, без гарантий; см. README и docs репозитория.
EOF

INSTALLED_SIZE="$(du -sk "$STAGE/usr" | cut -f1)"
cat > "$STAGE/DEBIAN/control" <<EOF
Package: confaster-mcp
Version: $VERSION
Section: devel
Priority: optional
Architecture: $ARCH
Maintainer: Anton Rastyazhenko <rastyazhenko.1c@gmail.com>
Homepage: https://github.com/grevinden/confaster-mcp-linux
Installed-Size: $INSTALLED_SIZE
Depends: libc6 (>= 2.31), libgcc-s1, libstdc++6, libicu78 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70
Description: MCP-сервер поиска по конфигурациям 1С (порт Confaster для Linux)
 Сервер 1c-search: текстовый и символьный поиск по коду BSL/XML,
 объекты метаданных, вызовы и точки влияния, справка платформы 1С.
 Сборка self-contained, dotnet-runtime не требуется.
EOF

mkdir -p "$ROOT/build"
dpkg-deb --root-owner-group --build "$STAGE" \
    "$ROOT/build/confaster-mcp_${VERSION}_${ARCH}.deb"
echo "Готово: $ROOT/build/confaster-mcp_${VERSION}_${ARCH}.deb"
