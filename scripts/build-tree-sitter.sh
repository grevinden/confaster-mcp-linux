#!/usr/bin/env bash
# Собирает libtree-sitter-bsl.so — единую нативную библиотеку
# (рантайм tree-sitter + грамматика BSL), которую грузит ConfasterMCP
# через DllImport("tree-sitter-bsl").
#
# Требуется gcc. Результаты: ./libtree-sitter-bsl.so и копия в dist/.
#
# Версии зафиксированы: C#-обёртка передаёт TSNode как непрозрачные 32 байта,
# связка проверена на grammar b38125d + runtime 0.25.6.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/dist}"
GRAMMAR_SHA=b38125de221486a6581bb01e06ab7e392d26430e   # alkoleft/tree-sitter-bsl, master
RUNTIME_VER=0.25.6                                      # tree-sitter/tree-sitter
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

curl -sfL "https://github.com/alkoleft/tree-sitter-bsl/archive/$GRAMMAR_SHA.tar.gz" | tar xz -C "$WORK"
curl -sfL "https://github.com/tree-sitter/tree-sitter/archive/refs/tags/v$RUNTIME_VER.tar.gz" | tar xz -C "$WORK"

gcc -c -O2 -fPIC "$WORK/tree-sitter-$RUNTIME_VER/lib/src/lib.c" \
    -I "$WORK/tree-sitter-$RUNTIME_VER/lib/include" \
    -I "$WORK/tree-sitter-$RUNTIME_VER/lib/src" \
    -o "$WORK/lib.o"
gcc -c -O2 -fPIC "$WORK/tree-sitter-bsl-$GRAMMAR_SHA/src/parser.c" \
    -I "$WORK/tree-sitter-bsl-$GRAMMAR_SHA/src" \
    -o "$WORK/parser.o"
gcc -shared -o "$ROOT/libtree-sitter-bsl.so" "$WORK/lib.o" "$WORK/parser.o"

mkdir -p "$OUT"
cp "$ROOT/libtree-sitter-bsl.so" "$OUT/"
echo "Готово: $OUT/libtree-sitter-bsl.so"
