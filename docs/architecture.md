# Устройство

## Схема

```text
MCP-клиент ⇄ stdin/stdout ⇄ Program.Main
                              │
                        McpServer (JSON-RPC 2.0)
                              │
                        ToolRegistry ── 21 инструмент
                         │        │
     IndexManager ────────┘        └────── HelpIndexer / HelpDatabase
     FileScanner, MetadataParser            (справка платформы 1С)
     BslAstParser (tree-sitter)
                         │
                   SQLite-индекс
             <корень>/.index/onec_search.db
```

### Program

Разбирает аргументы, регистрирует нативные библиотеки, строит `ConfigRoots`
(основная конфигурация + расширения), создаёт `ToolRegistry` и читает строки
stdin в цикле. Каждая строка — JSON-RPC-сообщение; ответ пишется в stdout.
Диагностика — только в stderr.

### McpServer

Реализует `initialize`, `notifications/initialized`, `tools/list`, `tools/call`,
`ping`. `serverInfo.name` — `1c-search`, версия берётся из метаданных сборки.

### ConfigRoots

Описывает одну или несколько конфигураций (основная + расширения). Для
расширения имя определяется по `Configuration.xml` (реквизит `ObjectBelonging`
и имя), кешируется в индексе. `IndexRoot` — каталог с базой индекса.

### Индекс

SQLite с FTS5:
`indexed_files`, `symbols` (+ `symbols_fts`), `calls`, `objects`,
`object_items`, `object_synonyms`, `regions`, `meta`, `symbol_weights`,
`domain_aliases`. Инкрементальная синхронизация сравнивает `mtime` и размер
файлов, переиндексирует только изменившиеся. Для поиска используется FTS5
с триграммами, поэтому подстроки ищутся без префиксных костылей.

### BslAstParser

Обёртка над tree-sitter: `ts_parser_parse_string` строит дерево, обход извлекает
процедуры, функции, параметры, вызовы, регионы, директивы компиляции.
Требует `libtree-sitter-bsl.so`; при его отсутствии сервер работает в урезанном
режиме (текстовый поиск и справка доступны).

### Справка 1С

`HelpIndexer` ищет платформу (`FindPlatformPath`: `/opt/1cv8*/x86_64/<версия>`,
`/opt/1C/1cv8*/x86_64/<версия>`, Windows-пути), определяет каталог с `.hbk`
(в Linux-раскладке — корень каталога версии), разбирает файлы справки и строит
отдельный индекс. Инструменты справки регистрируются только при найденной
платформе.

## Нативные библиотеки

| Библиотека | Источник |
|---|---|
| `libe_sqlite3.so` | NuGet `SQLitePCLRaw.lib.e_sqlite3`, приезжает в `runtimes/<rid>/native/` |
| `libtree-sitter-bsl.so` | `scripts/build-tree-sitter.sh`: рантайм tree-sitter + грамматика BSL |

Обе ищутся рядом с приложением, поэтому `dist/`, `.deb` и бинарный архив
самодостаточны.
