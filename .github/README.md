# ConfasterMCP — порт под Linux

MCP-сервер «1c-search» из состава [Confaster](https://confaster.ru/) — инструмента
поиска по выгрузкам конфигураций 1С. Оригинал распространяется как Windows-сборка;
этот репозиторий содержит порт сервера под Linux: рабочий исходник, сборка, скрипты.

Порт создан декомпиляцией `ConfasterMCP.dll` оригинального дистрибутива
(см. `ORIGINAL.md`) с последующей доводкой под Linux.

## Статус

| Возможность | Состояние |
|---|---|
| 17 инструментов поиска по коду (`search_code`, `sync_index`, `stats`, …) | работает |
| Справка 1С (`search_1c_help`, `get_1c_help_topic`, `list_1c_help_versions`, `reindex_1c_help`) | работает, платформа ищется в `/opt/1cv8` и `/opt/1C/1cv8` |
| Символьные инструменты (`find_symbol`, `get_symbol_context`, `find_references`, `impact_analysis`) | работает через собранную `libtree-sitter-bsl.so` (`scripts/build-tree-sitter.sh`) |
| GUI-приложение Confaster.exe | не портируется — только MCP-сервер |

## Требования

- .NET 8 runtime (`Microsoft.NETCore.App` 8.0+): `dotnet --list-runtimes`.
- `gcc` — для сборки нативной библиотеки грамматики при сборке порта.
- Выгрузка конфигурации 1С (каталог с `.xml`/`.bsl`) — то же, что подаётся оригинальному серверу на Windows.

## Сборка и запуск

```bash
scripts/build.sh                                    # сборка в dist/
scripts/run-mcp.sh --export-root /путь/к/выгрузке   # запуск сервера (stdio)
```

Сервер говорит по MCP поверх stdin/stdout и сам создаёт индекс
`<выгрузка>/.index/onec_search.db`. Нативная `libe_sqlite3.so` для linux-x64
входит в сборку (`dist/runtimes/`), ничего доустанавливать не нужно.

Аргументы сервера: `--export-root <путь>`, `--extension-root <имя|путь>`
(несколько), `--disabled-tool <имя>` (несколько).

## Подключение клиента

Пример для любого MCP-клиента, работающего через stdio:

```json
{
  "mcpServers": {
    "1c-search": {
      "command": "/путь/к/Confaster/scripts/run-mcp.sh",
      "args": ["--export-root", "/путь/к/выгрузке/конфигурации"]
    }
  }
}
```

## Устройство репозитория

- `src/ConfasterMCP/` — исходники порта. Отличия от оригинала: csproj на net8.0,
  `PackageReference` вместо `HintPath`, добавлен `System.Text.Encoding.CodePages`,
  поиск платформы 1С и раскладка файлов справки с учётом Linux
  (`HelpIndexer.FindPlatformPath`, `HelpIndexer.GetHelpDirectory`).
- `scripts/fetch-original.sh` — скачать последний дистрибутив и распаковать в `original/`.
- `ORIGINAL.md` — источник, хеши загруженной копии.
- `original/`, `dist/` — в git не входят.

## tree-sitter

Символьные инструменты требуют нативную библиотеку грамматики BSL. Сервер ищет
`libtree-sitter-bsl.so` рядом с `ConfasterMCP.dll` (и в системных путях); без неё
остальные инструменты работают как обычно. `scripts/build.sh` собирает её автоматически
(нужен `gcc`), отдельный вызов: `scripts/build-tree-sitter.sh [каталог-назначения]`.
Собирается единой библиотекой из runtime tree-sitter и генерированной грамматики
[alkoleft/tree-sitter-bsl](https://github.com/alkoleft/tree-sitter-bsl), версии
зафиксированы в скрипте.
