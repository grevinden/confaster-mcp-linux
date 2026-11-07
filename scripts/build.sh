#!/usr/bin/env bash
# Собирает порт в dist/ (framework-dependent, для установленного .NET 8).
# Использование: scripts/build.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet publish "$ROOT/src/ConfasterMCP" -c Release -o "$ROOT/dist"
echo
echo "Готово: $ROOT/dist/ConfasterMCP.dll"
echo "Запуск:  scripts/run-mcp.sh --export-root /путь/к/выгрузке/конфигурации"
