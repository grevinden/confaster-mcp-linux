#!/usr/bin/env bash
# Сравнивает набор инструментов порта с оригинальным ConfasterMCP:
# имена, описания, inputSchema. Справочные инструменты порта
# (search_1c_help и т.д.) исключаются из сравнения — оригинал на Linux
# их не регистрирует, т.к. не находит платформу 1С.
#
# Использование: scripts/compare-upstream.sh [путь к оригинальному ConfasterMCP.dll]
# Оригинал по умолчанию: original/Libs/ConfasterMCP/ConfasterMCP.dll
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ORIG="${1:-$ROOT/original/Libs/ConfasterMCP/ConfasterMCP.dll}"
PORT="$ROOT/dist/ConfasterMCP.dll"
[ -f "$ORIG" ] || { echo "Нет оригинала: $ORIG (scripts/fetch-original.sh)" >&2; exit 1; }
[ -f "$PORT" ] || { echo "Нет порта: $PORT (scripts/build.sh)" >&2; exit 1; }

TMP="$(mktemp -d /tmp/opencode/compare.XXXXXX)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/root" "$TMP/lib"

# Оригинал несёт только e_sqlite3.dll (Windows). Для запуска на Linux
# подсовываем системный libsqlite3 через LD_LIBRARY_PATH.
SQLITE="$(ldconfig -p 2>/dev/null | awk '/libsqlite3\.so\.0 /{print $NF; exit}')"
[ -n "$SQLITE" ] || { echo "Не найден libsqlite3.so.0 — нужен пакет libsqlite3-0" >&2; exit 1; }
ln -sf "$SQLITE" "$TMP/lib/libe_sqlite3.so"

dump() { # <dll> <out> [libdir]
    python3 - "$1" "$TMP/root" "$2" "${3:-}" <<'PY'
import json, os, select, subprocess, sys, tempfile, time
dll, root, out = sys.argv[1:4]
env = dict(os.environ)
if len(sys.argv) > 4 and sys.argv[4]:
    env["LD_LIBRARY_PATH"] = sys.argv[4] + ":" + env.get("LD_LIBRARY_PATH", "")
err = tempfile.TemporaryFile()
p = subprocess.Popen(["dotnet", dll, "--export-root", root],
                     stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=err, text=True, bufsize=1, env=env)
def send(o):
    p.stdin.write(json.dumps(o) + "\n"); p.stdin.flush()
def read_id(want, timeout=60):
    end = time.time() + timeout
    while time.time() < end:
        r, _, _ = select.select([p.stdout], [], [], 0.5)
        if not r: continue
        line = p.stdout.readline()
        if not line: break
        try: m = json.loads(line)
        except json.JSONDecodeError: continue
        if m.get("id") == want: return m
    err.seek(0); sys.stderr.write(err.read().decode("utf-8", "replace")[-1500:])
    raise SystemExit(f"нет ответа на id={want}")
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"compare","version":"0"}}})
init = read_id(1)["result"]
send({"jsonrpc":"2.0","method":"notifications/initialized"})
send({"jsonrpc":"2.0","id":2,"method":"tools/list"})
tools = read_id(2)["result"]["tools"]
p.stdin.close(); p.wait(timeout=15)
json.dump({"init": init, "tools": tools}, open(out, "w"), ensure_ascii=False, sort_keys=True)
PY
}

dump "$ORIG" "$TMP/up.json" "$TMP/lib"
dump "$PORT" "$TMP/port.json"

python3 - "$TMP/up.json" "$TMP/port.json" <<'PY'
import json, sys
up_d, port_d = (json.load(open(f)) for f in sys.argv[1:3])
up = {t["name"]: t for t in up_d["tools"]}
port = {t["name"]: t for t in port_d["tools"]}
help_tools = {"search_1c_help", "get_1c_help_topic", "list_1c_help_versions", "reindex_1c_help"}
only_up = sorted(set(up) - set(port))
only_port = sorted(set(port) - set(up) - help_tools)
shared = sorted(set(up) & set(port))
diff = [n for n in shared if json.dumps(up[n], sort_keys=True) != json.dumps(port[n], sort_keys=True)]
print(f"оригинал: {len(up)} инструментов, порт: {len(port)}")
print(f"общих: {len(shared)}, идентичных: {len(shared) - len(diff)}, различающихся: {len(diff)}")
print("только в оригинале:", only_up or "нет")
print("только в порте (не справочные):", only_port or "нет")
for n in diff:
    fields = [k for k in up[n] if up[n].get(k) != port[n].get(k)]
    print("  РАСХОЖДЕНИЕ", n, fields)
if only_up or diff:
    raise SystemExit("PAритет НЕ подтверждён")
print("ПАРИТЕТ OK")
PY
