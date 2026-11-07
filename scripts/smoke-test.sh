#!/usr/bin/env bash
# Смоук-тест MCP-сервера: initialize + tools/list.
# Использование: scripts/smoke-test.sh [dll] [export-root]
# Без второго аргумента создаётся временный пустой корень выгрузки.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DLL="${1:-$ROOT/dist/ConfasterMCP.dll}"
EXPORT="${2:-}"
[ -f "$DLL" ] || { echo "Нет $DLL — сначала scripts/build.sh" >&2; exit 1; }
case "$DLL" in
    *.dll) CMD=(dotnet "$DLL") ;;
    *)     CMD=("$DLL") ;;
esac
if [ -z "$EXPORT" ]; then
    EXPORT="$(mktemp -d ${TMPDIR:-/tmp}/confaster-smoke.XXXXXX)"
    trap 'rm -rf "$EXPORT"' EXIT
fi

python3 - "${CMD[@]}" "$EXPORT" <<'PY'
import json, os, select, subprocess, sys, tempfile, time

cmd, export = sys.argv[1:-1], sys.argv[-1]
err = tempfile.TemporaryFile()
p = subprocess.Popen(cmd + ["--export-root", export],
                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err,
                     text=True, bufsize=1)

def send(obj):
    p.stdin.write(json.dumps(obj) + "\n")
    p.stdin.flush()

def read_id(want, timeout=60):
    end = time.time() + timeout
    while time.time() < end:
        ready, _, _ = select.select([p.stdout], [], [], 0.5)
        if not ready:
            continue
        line = p.stdout.readline()
        if not line:
            break
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue
        if msg.get("id") == want:
            return msg
    err.seek(0)
    sys.stderr.write(err.read().decode("utf-8", "replace")[-2000:])
    raise SystemExit(f"нет ответа на id={want}")

send({"jsonrpc": "2.0", "id": 1, "method": "initialize",
      "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                 "clientInfo": {"name": "smoke", "version": "0"}}})
init = read_id(1)["result"]

send({"jsonrpc": "2.0", "method": "notifications/initialized"})
send({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
tools = read_id(2)["result"]["tools"]

p.stdin.close()
p.wait(timeout=15)

names = {t["name"] for t in tools}
core = {"semantic_find", "search_code", "find_symbol", "smart_find",
        "get_symbol_context", "get_file_context", "list_objects",
        "get_object_structure", "find_references", "impact_analysis",
        "get_function_context", "get_module_functions",
        "find_function_in_object", "stats", "sync_index", "benchmark",
        "switch_export_root"}
help_tools = {"search_1c_help", "get_1c_help_topic",
              "list_1c_help_versions", "reindex_1c_help"}

info = init.get("serverInfo", {})
print("serverInfo:", info)
print("tools:", len(tools), "| core:", len(names & core), "| help:", len(names & help_tools))

problems = []
if info.get("name") != "1c-search":
    problems.append(f"serverInfo.name={info.get('name')!r}, ожидалось '1c-search'")
missing = core - names
if missing:
    problems.append(f"нет основных инструментов: {sorted(missing)}")
if len(names & help_tools) not in (0, 4):
    problems.append(f"частичный набор справочных инструментов: {sorted(names & help_tools)}")
if problems:
    raise SystemExit("SMOKE FAIL: " + "; ".join(problems))
print("SMOKE OK")
PY
