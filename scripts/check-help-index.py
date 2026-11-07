#!/usr/bin/env python3
"""Проверка индекса справки 1С: книги, категории, английский фолбэк и поиск.

Запуск: scripts/check-help-index.py [путь/к/ConfasterMCP.dll]

Индекс строится в изолированном HOME (временный каталог), рабочий
~/.config/Confaster/HelpIndex/help.db не трогается. Скрипт проверяет:
  * индекс содержит три книги — категории syntax, language, query;
  * поиск находит темы языка запросов и встроенного языка;
  * фразовый запрос с несколькими словами находит темы (откат на AND);
  * повреждённые страницы восстановлены английским дублем ((en), фолбэк);
  * get_1c_help_topic отдаёт непустой текст.

Возвращает 0 при успехе, 1 при провале — пригоден для CI.
"""
import json
import os
import select
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DLL = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "dist", "ConfasterMCP.dll")

MIN_TOPICS = {"syntax": 25000, "language": 36, "query": 125}

MIN_FALLBACK_TOPICS = 12


def fail(message):
    print("CHECK FAIL: " + message, file=sys.stderr)
    return 1


def main():
    if not os.path.isfile(DLL):
        return fail("нет %s — сначала scripts/build.sh" % DLL)
    home = tempfile.mkdtemp(prefix="confaster-help-check.")
    export = os.path.join(home, "export")
    os.makedirs(export)
    env = dict(os.environ)
    env["HOME"] = home
    env.pop("XDG_CONFIG_HOME", None)
    err = tempfile.TemporaryFile()
    command = (["dotnet", DLL] if DLL.endswith(".dll") else [DLL]) + ["--export-root", export]
    proc = subprocess.Popen(
        command,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err,
        text=True, bufsize=1, env=env,
    )
    counter = [0]

    def send(obj):
        proc.stdin.write(json.dumps(obj) + "\n")
        proc.stdin.flush()

    def read(want, timeout=120):
        end = time.time() + timeout
        while time.time() < end:
            ready, _, _ = select.select([proc.stdout], [], [], 0.5)
            if not ready:
                continue
            line = proc.stdout.readline()
            if not line:
                break
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                continue
            if msg.get("id") == want:
                return msg
        err.seek(0)
        sys.stderr.write(err.read().decode("utf-8", "replace")[-3000:])
        raise RuntimeError("нет ответа на id=%s" % want)

    def call(name, args, timeout=120):
        counter[0] += 1
        ident = counter[0] + 100
        send({"jsonrpc": "2.0", "id": ident, "method": "tools/call",
              "params": {"name": name, "arguments": args}})
        payload = read(ident, timeout)
        return json.loads(payload["result"]["content"][0]["text"])

    try:
        send({"jsonrpc": "2.0", "id": 1, "method": "initialize",
              "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                         "clientInfo": {"name": "help-check", "version": "0"}}})
        read(1)
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})

        version_info = {}
        started = time.time()
        while time.time() - started < 600:
            version_info = call("list_1c_help_versions", {}, timeout=60)
            if not version_info.get("is_indexing") and version_info.get("topic_count"):
                break
            time.sleep(5)
        topic_count = int(version_info.get("topic_count") or 0)
        if topic_count <= 0:
            return fail("индекс справки не построился: %s" % version_info)
        version = version_info.get("indexed_version")
        if not version:
            return fail("в индексе нет indexed_version: %s" % version_info)

        db_path = os.path.join(home, ".config", "Confaster", "HelpIndex", "help.db")
        if not os.path.isfile(db_path):
            return fail("нет файла индекса %s" % db_path)
        conn = sqlite3.connect("file:%s?mode=ro" % db_path, uri=True)
        counts = dict(conn.execute("SELECT category, COUNT(*) FROM topics GROUP BY category"))
        fallback_topics = conn.execute(
            "SELECT COUNT(*) FROM topics WHERE title LIKE '% (en)'").fetchone()[0]
        conn.close()

        problems = []
        for category, minimum in MIN_TOPICS.items():
            got = counts.get(category, 0)
            if got < minimum:
                problems.append("категория %s: %d тем, ожидалось не меньше %d" % (category, got, minimum))
        if sum(counts.values()) != topic_count:
            problems.append("topic_count=%d, а в базе %d" % (topic_count, sum(counts.values())))
        if fallback_topics < MIN_FALLBACK_TOPICS:
            problems.append("тем английского фолбэка %d, ожидалось не меньше %d"
                            % (fallback_topics, MIN_FALLBACK_TOPICS))

        err.seek(0)
        stderr_text = err.read().decode("utf-8", "replace")
        for expected in ("HELP_STATUS:fallback:language:5", "HELP_STATUS:fallback:query:7"):
            if expected not in stderr_text:
                problems.append("в stderr нет строки %s" % expected)

        query_lang = call("search_1c_help", {"query": "Синтаксис текста запросов", "limit": 3})
        if "%s/query/root" % version not in (query_lang.get("text") or ""):
            problems.append("поиск по языку запросов не находит %s/query/root" % version)

        filtered = call("search_1c_help", {"query": "ВЫБРАТЬ", "limit": 5, "category": "query"})
        if not filtered.get("total"):
            problems.append("category=query ничего не находит по «ВЫБРАТЬ»")
        for line in (filtered.get("text") or "").splitlines():
            if "topic_id:" in line and "/query/" not in line:
                problems.append("category=query вернул тему другого раздела: %s" % line.strip())
                break
        if "%s/query/KeyWordsSELECT" % version not in (filtered.get("text") or ""):
            problems.append("поиск «ВЫБРАТЬ» не находит тему «Ключевое слово ВЫБРАТЬ» (%s/query/KeyWordsSELECT)" % version)

        join = call("search_1c_help", {"query": "СОЕДИНЕНИЕ", "limit": 5, "category": "query"})
        if "%s/query/" % version not in (join.get("text") or ""):
            problems.append("поиск «СОЕДИНЕНИЕ» не находит тем языка запросов")

        keyword = call("get_1c_help_topic", {"topic_id": "%s/query/KeyWordsSELECT" % version})
        if len(keyword.get("content") or "") < 50:
            problems.append("get_1c_help_topic не отдал тему %s/query/KeyWordsSELECT" % version)

        english = call("get_1c_help_topic", {"topic_id": "%s/query/STRING" % version})
        if len(english.get("content") or "") < 100:
            problems.append("нет английского фолбэка для %s/query/STRING" % version)
        elif "String" not in (english.get("content") or ""):
            problems.append("тема %s/query/STRING не похожа на описание функции STRING" % version)
        if "(en)" not in (english.get("title") or ""):
            problems.append("у темы %s/query/STRING нет пометки (en): %r" % (version, english.get("title")))

        builtin = call("get_1c_help_topic", {"topic_id": "%s/query/builtin_functions" % version})
        if len(builtin.get("content") or "") < 100:
            problems.append("нет английского фолбэка для %s/query/builtin_functions" % version)

        datetime_search = call("search_1c_help", {"query": "DATETIME", "limit": 5, "category": "query"})
        if "%s/query/" % version not in (datetime_search.get("text") or ""):
            problems.append("поиск «DATETIME» не находит тем языка запросов")

        words = call("search_1c_help", {"query": "ВЫБРАТЬ ИЗ ГДЕ", "limit": 3})
        if not words.get("total"):
            problems.append("запрос из нескольких слов не находит ничего (откат на AND не работает)")

        lang = call("search_1c_help", {"query": "Логические операции", "limit": 3})
        if "%s/language/" % version not in (lang.get("text") or ""):
            problems.append("поиск по встроенному языку не находит %s/language/*" % version)

        topic = call("get_1c_help_topic", {"topic_id": "%s/query/root" % version})
        if len(topic.get("content") or "") < 100:
            problems.append("get_1c_help_topic не отдал текст темы %s/query/root" % version)
        if "&" in (topic.get("title") or ""):
            problems.append("в заголовке темы остались HTML-сущности: %r" % topic.get("title"))

        if problems:
            return fail("; ".join(problems))

        print("books:", ", ".join("%s=%d" % item for item in sorted(counts.items())),
              "| english fallback:", fallback_topics)
        print("help index:", version, "| topics:", topic_count, "| built in %.1f s" % (time.time() - started))

        # Повторная индексация должна дать тот же снимок (reindex_1c_help).
        before = version_info.get("indexed_at")
        call("reindex_1c_help", {})
        rebuilt = {}
        started = time.time()
        while time.time() - started < 600:
            rebuilt = call("list_1c_help_versions", {}, timeout=60)
            if not rebuilt.get("is_indexing") and rebuilt.get("indexed_at") not in (None, before):
                break
            time.sleep(5)
        if rebuilt.get("indexed_at") in (None, before):
            return fail("reindex_1c_help не перестроил индекс: %s" % rebuilt)
        if int(rebuilt.get("topic_count") or 0) != topic_count:
            return fail("после reindex_1c_help тем %s, до него %d" % (rebuilt.get("topic_count"), topic_count))
        conn = sqlite3.connect("file:%s?mode=ro" % db_path, uri=True)
        rebuilt_counts = dict(conn.execute("SELECT category, COUNT(*) FROM topics GROUP BY category"))
        conn.close()
        if rebuilt_counts != counts:
            return fail("после reindex_1c_help состав разделов изменился: %s" % rebuilt_counts)
        print("reindex: OK (%.1f s)" % (time.time() - started))
        print("CHECK OK")
        return 0
    finally:
        try:
            proc.stdin.close()
            proc.wait(timeout=20)
        except Exception:
            proc.kill()
        if proc.poll() is None:
            proc.kill()
        else:
            err.seek(0)
            for line in err.read().decode("utf-8", "replace").splitlines():
                if line.startswith("HELP_STATUS:error") or line.startswith("MCP_LOG: FATAL"):
                    print(line, file=sys.stderr)
        shutil.rmtree(home, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
