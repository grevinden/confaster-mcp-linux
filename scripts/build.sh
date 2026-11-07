#!/usr/bin/env bash
# Собирает порт в dist/ (framework-dependent, для установленного .NET 8).
# Использование: scripts/build.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet publish "$ROOT/src/ConfasterMCP" -c Release -o "$ROOT/dist"
"$ROOT/scripts/build-tree-sitter.sh" "$ROOT/dist" || \
  echo "Внимание: libtree-sitter-bsl.so не собрана — символьные инструменты будут помечены как недоступны." >&2
echo
echo "Готово: $ROOT/dist/ConfasterMCP.dll"
echo "Запуск:  scripts/run-mcp.sh --export-root /путь/к/выгрузке/конфигурации"
