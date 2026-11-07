# Портирование: отличия от апстрима и паритет

## Границы порта

Предмет порта — **MCP-сервер** ConfasterMCP (1c-search). GUI-оболочка
`Confaster.exe` в область порта не входит и её отсутствие потерей не считается.
Критерий приёмки: сервер на Linux не должен уступать оригиналу ни в одном
инструменте — ни по схеме, ни по фактическому результату.

## Метод проверки

1. `scripts/fetch-original.sh` — свежий дистрибутив с confaster.ru в `original/`.
2. Декомпиляция `Libs/ConfasterMCP/ConfasterMCP.dll` в `src/ConfasterMCP`
   (ilspycmd) и правки под Linux.
3. `scripts/compare-upstream.sh` — сверка `tools/list`: имена, описания,
   `inputSchema`.
4. `scripts/compare-behavior.sh <выгрузка>` — одинаковые вызовы основных
   инструментов (до 17 на выгрузке с метаданными) к оригиналу и порту,
   сравнение фактических JSON-ответов.
5. Функциональные прогоны на реальном коде: проект 1C:EDT
   (65 модулей, 2 141 символ), расширения, справка 1С.

## Паритет

Схемы инструментов:

```text
оригинал: 17 инструментов, порт: 21
общих: 17, идентичных: 17, различающихся: 0
только в оригинале: нет
только в порте (не справочные): нет
ПАРИТЕТ OK
```

Четыре «лишних» — справочные инструменты. В оригинале они есть, но на Linux
не регистрируются, потому что оригинал не ищет платформу 1С в Linux-раскладке.
Порт эту возможность добавляет, а не теряет.

Фактическое поведение (`scripts/compare-behavior.sh`) проверено на двух видах
выгрузок: дамп Конфигуратора с метаданными и проект 1C:EDT.

```text
dump Конфигуратора (Транспорт, 3 объекта, 8 файлов):
  сравнено инструментов: 17  (включая list_objects, get_object_structure,
  find_function_in_object, impact_analysis, switch_export_root)
  ПОВЕДЕНИЕ СОВПАДАЕТ

проект 1C:EDT (65 модулей, 2 141 символ, объектных метаданных нет):
  сравнено инструментов: 15
  ПОВЕДЕНИЕ СОВПАДАЕТ
```

Исключаются только служебные поля: время синхронизации, размер БД, задержки
benchmark и абсолютные пути корня.

Справочные инструменты отдельной сверки не требуют: оригинал на Linux их не
регистрирует. В порте проверены вручную — индекс платформы 8.3.27.2342,
25 520 тем, `search_1c_help` и `get_1c_help_topic` возвращают текст темы.

## Статус инструментов на Linux

| Инструмент | Статус |
|---|---|
| `semantic_find`, `search_code`, `find_symbol`, `smart_find` | работает, ответы совпадают с оригиналом |
| `get_symbol_context`, `get_file_context`, `get_module_functions` | работает, ответы совпадают |
| `find_references`, `impact_analysis`, `get_function_context` | работает, ответы совпадают |
| `list_objects`, `get_object_structure`, `find_function_in_object` | работает на дампе Конфигуратора, ответы совпадают |
| `stats`, `sync_index`, `benchmark`, `switch_export_root` | работает, ответы совпадают (кроме служебных полей) |
| `search_1c_help`, `get_1c_help_topic`, `list_1c_help_versions`, `reindex_1c_help` | работает в порте; оригинал на Linux их не даёт |

Нерабочих инструментов нет. Единственная зависимость — `libtree-sitter-bsl.so`
рядом с приложением; она входит в `.deb` и архив, при сборке из исходников её
готовит `scripts/build-tree-sitter.sh`.

## Отличия от оригинала

| Файл / часть | Изменение |
|---|---|
| `ConfasterMCP.csproj` | `net8.0`, зависимости через `PackageReference`, nullable-шум декомпилятора в `NoWarn` |
| `Properties/AssemblyInfo.cs` | удалён служебный атрибут `RefSafetyRulesAttribute` от декомпилятора |
| `ConfasterMCP.Help/HelpIndexer.cs` | `FindPlatformPath` ищет справку в порядке `CONFASTER_1C_HELP_PATH` → `1c-help/` рядом с бинарником → `/usr/share/confaster-mcp/1c-help` → установленная платформа; `GetHelpDirectory` берёт `bin/`, если там `.hbk`, иначе корень версии |
| `data/1c-help/` | файлы справки платформы 1С в поставке |
| `libtree-sitter-bsl.so` | собрана из исходников грамматики вместо Windows-DLL |
| пакетирование | `.deb` и бинарный архив, self-contained + ReadyToRun |

Это аддитивные правки: ни один код-путь оригинала не удалён.

## Оптимизации

- self-contained публикация: не требует установленного dotnet-runtime;
- `PublishReadyToRun` — меньше времени до первого ответа;
- единая `libtree-sitter-bsl.so` (рантайм + грамматика) — один `DllImport`;
- индекс FTS5 с триграммами и инкрементальной синхронизацией;
- поиск каталога справки кешируется, справка индексируется один раз.

## Как перепроверить

```bash
scripts/fetch-original.sh                 # свежий оригинал в original/
scripts/build.sh                          # порт в dist/
scripts/compare-upstream.sh               # сверка tools/list
scripts/compare-behavior.sh <выгрузка>    # сверка фактических ответов
scripts/smoke-test.sh                     # протокольный смоук-тест
```
