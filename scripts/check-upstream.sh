#!/usr/bin/env bash
# Проверка: вышла ли новая версия апстрима Confaster.
# Скачивает свежий Confaster.zip во временный каталог, читает версию и коммит
# из ConfasterMCP.dll и сравнивает с VERSION репозитория.
#
# Код возврата: 0 — та же версия, 10 — новая версия апстрима (нужна
# перекомпиляция, см. docs/releases.md), 1 — ошибка.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP="$(mktemp -d /tmp/opencode/check-upstream.XXXXXX)"
trap 'rm -rf "$TMP"' EXIT

CURRENT="$(tr -d '[:space:]' < "$ROOT/VERSION")"
echo "Версия порта: $CURRENT"

curl -fSL --retry 2 -o "$TMP/Confaster.zip" "https://confaster.ru/download/"
SHA="$(sha256sum "$TMP/Confaster.zip" | cut -d' ' -f1)"
echo "sha256 архива: $SHA"

unzip -q "$TMP/Confaster.zip" -d "$TMP/x"
DLL="$(find "$TMP/x" -name ConfasterMCP.dll | head -1)"
[ -n "$DLL" ] || { echo "В архиве нет ConfasterMCP.dll" >&2; exit 1; }

IVER="$(strings "$DLL" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+\+[0-9a-f]{40}' | head -1)"
[ -n "$IVER" ] || { echo "Не удалось прочитать AssemblyInformationalVersion" >&2; exit 1; }
UP_VER="${IVER%%+*}"
UP_COMMIT="${IVER##*+}"
echo "Апстрим: $UP_VER+$UP_COMMIT"

BASE="${CURRENT%%-port.*}"
if [ "$BASE" != "$UP_VER" ]; then
    echo
    echo "Доступна новая версия апстрима: $UP_VER (порт собран на $BASE)."
    echo "Что делать: docs/releases.md — передекомпилировать, перенести правки порта,"
    echo "поднять VERSION до $UP_VER-port.1, прогнать scripts/smoke-test.sh и сравнение"
    echo "tools/list с оригиналом."
    exit 10
fi
echo "Базовая версия совпадает. Сверьте хеш архива с CHANGELOG.md — правки внутри $UP_VER требуют передекомпиляции."
