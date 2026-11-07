#!/usr/bin/env bash
# Запуск MCP-сервера Confaster из сборки dist/.
# Использование: scripts/run-mcp.sh --export-root /путь/к/выгрузке [прочие аргументы]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DLL="$ROOT/dist/ConfasterMCP.dll"
[ -f "$DLL" ] || DLL="$ROOT/src/ConfasterMCP/bin/Release/net8.0/ConfasterMCP.dll"
[ -f "$DLL" ] || { echo "Сборка не найдена. Выполните scripts/build.sh" >&2; exit 1; }
exec dotnet "$DLL" "$@"
