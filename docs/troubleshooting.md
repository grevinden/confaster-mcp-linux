# Диагностика

Сервер пишет только в stderr, префиксы: `MCP_LOG:` — события, `HELP_STATUS:` —
состояние индекса справки. stdout занят JSON-RPC, туда ничего не логируется.

## В `tools/list` нет инструментов поиска

Сервер запущен без `--export-root` или каталог не существует. Проверьте
аргументы. При несуществующем корне регистрируются только инструменты справки
(или ничего, если платформа 1С не найдена).

## Нет четырёх инструментов справки

Справка входит в поставку. Проверьте, что каталог с ней на месте:

```text
<каталог приложения>/1c-help/<версия>/        (tar.gz, сборка из исходников)
/usr/share/confaster-mcp/1c-help/<версия>/    (.deb)
```

В каталоге версии должны лежать `shcntx_ru.hbk`, `shlang_ru.hbk`,
`shquery_ru.hbk`. Порядок поиска: `CONFASTER_1C_HELP_PATH`, `1c-help/` рядом
с бинарником, `/usr/share/confaster-mcp/1c-help`, установленная платформа 1С.
Если задана `CONFASTER_1C_HELP_PATH`, она должна указывать на существующий
каталог с `.hbk`. Состояние индекса видно по строкам `HELP_STATUS:` в stderr.

## В индексе нет языка запросов или встроенного языка

Строки `HELP_STATUS:parsed:` показывают, сколько тем дала каждая книга:

```text
HELP_STATUS:parsed:syntax:25520
HELP_STATUS:parsed:language:32
HELP_STATUS:parsed:query:119
```

Если строк `language` и `query` нет или счётчики близки к нулю, проверьте, что
рядом с `shcntx_ru.hbk` лежат `shlang_ru.hbk` и `shquery_ru.hbk`. Полная
проверка индекса — свежий индекс во временном каталоге, все три раздела, поиск,
переиндексация:

```bash
scripts/check-help-index.py                                   # dist/ConfasterMCP.dll
scripts/check-help-index.py /usr/lib/confaster-mcp/ConfasterMCP
```

## Тринадцать страниц справки не попадают в индекс

Индексируется 25 671 тема из 25 684. Остальные тринадцать сжаты deflate с
предустановленным словарём упаковщика 1С, а `DeflateStream` такой словарь
задать не может: поток обрывается, текст получается битым, и запись
отбрасывается по сверке с `uncompressed size`. В `shquery_ru.hbk` это
`LitHum`, `LitType`, `STRING`, `DATEADD`, `INDEXBY`, `present.html`,
`builtin_functions.html`; в `shlang_ru.hbk` — `struct_TryCatch`, `8`,
`struct_While`, `SavingSettings`, `expression_priorities.html`, `struct_Raise`.
Все ключевые слова языка запросов (`ВЫБРАТЬ`, `ГДЕ`, `СОЕДИНЕНИЕ`,
`СГРУППИРОВАТЬ ПО` и др.) в индексе есть.

## Индекс справки оказался в текущем каталоге

Порт хранит его в `$XDG_CONFIG_HOME/Confaster/HelpIndex`, а при пустой
переменной — в `~/.config/Confaster/HelpIndex`. Каталог `Confaster/` в текущем
каталоге процесса означает запуск сборки старше `1.0.0-port.7`.

## «tree-sitter-bsl native library failed to load»

Рядом с приложением нет `libtree-sitter-bsl.so`. Пересоберите:
`scripts/build-tree-sitter.sh` (нужен gcc) и положите библиотеку в каталог
с `ConfasterMCP.dll`. Пока её нет, текстовый поиск и справка работают,
символьные инструменты недоступны.

## Ошибки SQLite

Порт несёт `libe_sqlite3.so` в `runtimes/linux-x64/native/`; если его нет —
бинарь собран неправильно (обычно запускают не тот DLL, не из `dist/` или
`.deb`). Для запуска *оригинального* DLL на Linux нужен системный
`libsqlite3.so.0` и симлинк `libe_sqlite3.so` (это делает
`scripts/compare-upstream.sh`).

## Ошибка загрузки ICU

```text
Couldn't find a valid ICU package installed on the system
```

Установите `libicu` (`apt-get install libicu74` или аналог для дистрибутива).
В `.deb` зависимость прописана.

## Индекс не обновляется

- `sync_index` вернул `busy`/ошибку блокировки — параллельно работает другая
  копия сервера на том же индексе; закройте её;
- повреждён индекс — остановите сервер и удалите `<корень>/.index/onec_search.db*`,
  затем запустите снова (индекс пересоздастся);
- права на запись в `<корень>/.index/` — сервер должен иметь право писать
  рядом с выгрузкой. Для read-only выгрузки используйте `switch_export_root`
  на копию.

## Инструмент пропал

Запущен с `--disabled-tool <имя>` или `--no-index-admin` (скрывает
`sync_index`, `switch_export_root`, `reindex_1c_help`).

## Медленная работа

`benchmark` покажет задержки по инструментам. Первый запуск всегда дольше:
строится индекс и индекс справки. Дальше инкрементальная синхронизация трогает
только изменённые файлы.

## Проверка «жив ли протокол»

```bash
scripts/smoke-test.sh dist/ConfasterMCP.dll
# serverInfo: {'name': '1c-search', 'version': '1.0.0'}
# tools: 21 | core: 17 | help: 4
# SMOKE OK
```
