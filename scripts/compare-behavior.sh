#!/usr/bin/env bash
# Поведенческая сверка: одинаковые вызовы инструментов к оригинальному серверу
# и к порту на одной и той же выгрузке. Сравниваются фактические JSON-ответы;
# служебные поля (время синхронизации, размер БД, задержки benchmark) исключены.
#
# Использование: scripts/compare-behavior.sh <корень-выгрузки> [оригинал.dll]
#
# ВНИМАНИЕ: скрипт пересоздаёт <корень>/.index для каждого прогона (это кеш,
# он восстанавливается). Для чужих проектов передавайте копию выгрузки.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPORT="${1:?использование: scripts/compare-behavior.sh <корень-выгрузки> [оригинал.dll]}"
ORIG="${2:-$ROOT/original/Libs/ConfasterMCP/ConfasterMCP.dll}"
PORT="$ROOT/dist/ConfasterMCP.dll"
[ -d "$EXPORT" ] || { echo "Нет каталога $EXPORT" >&2; exit 1; }
[ -f "$ORIG" ] || { echo "Нет оригинала $ORIG (scripts/fetch-original.sh)" >&2; exit 1; }
[ -f "$PORT" ] || { echo "Нет порта $PORT (scripts/build.sh)" >&2; exit 1; }

TMP="$(mktemp -d "${TMPDIR:-/tmp}/confaster-behavior.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/lib"
SQLITE="$(ldconfig -p 2>/dev/null | awk '/libsqlite3\.so\.0 /{print $NF; exit}')"
[ -n "$SQLITE" ] || { echo "Нужен libsqlite3.so.0 (пакет libsqlite3-0)" >&2; exit 1; }
ln -sf "$SQLITE" "$TMP/lib/libe_sqlite3.so"

SYMBOL="$(grep -rhoP '(?<=^(Процедура|Функция|Procedure|Function) )\w+' "$EXPORT" --include='*.bsl' 2>/dev/null | head -1 || true)"
[ -n "$SYMBOL" ] || { echo "В выгрузке не найдено ни одной процедуры/функции (*.bsl)" >&2; exit 1; }

run_dump() { # <метка> <libdir> <out.json> <команда...>
    local label="$1" libdir="$2" out="$3"; shift 3
    rm -rf "$EXPORT/.index"
    echo "[$label] индексация и вызовы..."
    EXPORT_ROOT="$EXPORT" SYMBOL_NAME="$SYMBOL" python3 - "$label" "$libdir" "$out" "$@" <<'PY'
import json, os, select, subprocess, sys, tempfile, time

label, libdir, out = sys.argv[1:4]
cmd = sys.argv[4:]
export = os.environ["EXPORT_ROOT"]
symbol = os.environ["SYMBOL_NAME"]
env = dict(os.environ)
if libdir:
    env["LD_LIBRARY_PATH"] = libdir + ":" + env.get("LD_LIBRARY_PATH", "")
cmd = cmd + ["--export-root", export]
err = tempfile.TemporaryFile()
p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=err, text=True, bufsize=1, env=env)

def send(i, name, args):
    p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": i, "method": "tools/call",
                              "params": {"name": name, "arguments": args}}) + "\n")
    p.stdin.flush()

def read_tool(i, timeout=180):
    return json.loads(read(i, timeout)["content"][0]["text"])

def read(i, timeout=180):
    end = time.time() + timeout
    while time.time() < end:
        r, _, _ = select.select([p.stdout], [], [], 0.5)
        if not r:
            continue
        line = p.stdout.readline()
        if not line:
            break
        try:
            m = json.loads(line)
        except json.JSONDecodeError:
            continue
        if m.get("id") == i:
            return m["result"]
    err.seek(0)
    sys.stderr.write(err.read().decode("utf-8", "replace")[-2000:])
    raise SystemExit(f"[{label}] нет ответа на id={i}")

p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                          "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                                     "clientInfo": {"name": "behavior", "version": "0"}}}) + "\n")
p.stdin.flush(); read(1)
p.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n")
p.stdin.flush()

results = {}
send(2, "sync_index", {})
results["sync_index"] = read_tool(2)
send(3, "stats", {})
results["stats"] = read_tool(3)
send(4, "find_symbol", {"query": symbol})
res = read_tool(4)
results["find_symbol"] = res

file = None
line = None
symbols = res.get("symbols") or []
if symbols:
    file = symbols[0].get("file")
    line = symbols[0].get("start_line")

calls = [
    ("search_code", {"query": symbol, "limit": 10}),
    ("smart_find", {"query": symbol, "include_code": True}),
    ("semantic_find", {"query": symbol, "limit": 5}),
    ("find_references", {"symbol": symbol, "limit": 20}),
    ("get_function_context", {"function_name": symbol}),
    ("list_objects", {"limit": 10}),
    ("benchmark", {"iterations": 3}),
]
if file and line:
    calls += [
        ("get_symbol_context", {"file": file, "line": line}),
        ("get_file_context", {"file": file, "line": line}),
        ("get_module_functions", {"module_path": file, "limit": 50}),
    ]
for i, (name, args) in enumerate(calls, start=10):
    send(i, name, args)
    results[name] = read_tool(i)

p.stdin.close(); p.wait(timeout=30)
json.dump(results, open(out, "w"), ensure_ascii=False, indent=1, sort_keys=True)
print(f"[{label}] вызовов: {len(results)}")
PY
}

run_dump "оригинал" "$TMP/lib" "$TMP/orig.json" dotnet "$ORIG"
run_dump "порт" "" "$TMP/port.json" dotnet "$PORT"

python3 - "$TMP/orig.json" "$TMP/port.json" "$EXPORT" <<'PY'
import json, sys
orig = json.load(open(sys.argv[1]))
port = json.load(open(sys.argv[2]))
root = sys.argv[3]

VOLATILE = {"last_sync_at", "sync_duration_ms", "db_size_mb", "min_ms",
            "avg_ms", "p95_ms", "max_ms", "elapsed_ms", "duration_ms"}

def normalize(v):
    if isinstance(v, dict):
        return {k: normalize(x) for k, x in v.items() if k not in VOLATILE}
    if isinstance(v, list):
        return [normalize(x) for x in v]
    if isinstance(v, str):
        return v.replace(root, "<EXPORT>")
    return v

def diff(a, b, path=""):
    out = []
    if type(a) is not type(b):
        return [f"{path}: тип {type(a).__name__} != {type(b).__name__}"]
    if isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a: out.append(f"{path}.{k}: только в порте")
            elif k not in b: out.append(f"{path}.{k}: только в оригинале")
            else: out += diff(a[k], b[k], f"{path}.{k}")
    elif isinstance(a, list):
        if len(a) != len(b):
            out.append(f"{path}: длина {len(a)} != {len(b)}")
        for i, (x, y) in enumerate(zip(a, b)):
            out += diff(x, y, f"{path}[{i}]")
    elif a != b:
        out.append(f"{path}: {a!r} != {b!r}")
    return out

ao, bo = normalize(orig), normalize(port)
issues = []
for tool in sorted(set(ao) | set(bo)):
    if tool not in ao: issues.append(f"{tool}: нет в оригинале"); continue
    if tool not in bo: issues.append(f"{tool}: нет в порте"); continue
    issues += [f"{tool}{d}" for d in diff(ao[tool], bo[tool])]

print(f"сравнено инструментов: {len(set(ao) & set(bo))}")
if issues:
    print("РАСХОЖДЕНИЯ:")
    for x in issues[:40]:
        print(" ", x)
    raise SystemExit(1)
print("ПОВЕДЕНИЕ СОВПАДАЕТ")
PY
