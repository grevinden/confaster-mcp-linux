# Портирование: отличия от апстрима и аудит потерь

## Метод

1. `scripts/fetch-original.sh` — свежий дистрибутив с confaster.ru в `original/`.
2. Декомпиляция `Libs/ConfasterMCP/ConfasterMCP.dll` в `src/ConfasterMCP`
   (ilspycmd) и правки под Linux.
3. `scripts/compare-upstream.sh` — запуск оригинала и порта, сверка
   `tools/list`: имена, описания, `inputSchema`.
4. Функциональные прогоны на реальном коде: индексация проекта 1C:EDT
   (65 модулей, 2 141 символ), `search_code`, `find_symbol`,
   `find_references`, справка 1С.

## Найденная и устранённая потеря

Первая версия порта была собрана из **устаревшей** сборки `ConfasterMCP.dll`
(`sha256 2e4ba670…`). В актуальном дистрибутиве лежит `624bfd22…`
(`AssemblyInformationalVersion 1.0.0+0c31141d`), где добавлена поддержка
расширений и обновлены схемы инструментов. Порт потерял:

- аргумент `--extension-root` и разбивку индекса на основную конфигурацию
  и расширения;
- параметр `configuration` у `search_code`, `list_objects`,
  `get_object_structure`, `get_function_context`, `get_module_functions`,
  `get_symbol_context`, `get_file_context`, `find_function_in_object`;
- обновлённые описания и схемы инструментов.

Исходник передекомпилирован с `624bfd22…`, правки порта наложены заново.
Текущий результат сверки:

```text
оригинал: 17 инструментов, порт: 21
общих: 17, идентичных: 17, различающихся: 0
только в оригинале: нет
только в порте (не справочные): нет
ПАРИТЕТ OK
```

Четыре «лишних» инструмента — `search_1c_help`, `get_1c_help_topic`,
`list_1c_help_versions`, `reindex_1c_help`: в оригинале они есть, но на Linux
не регистрируются, потому что оригинал не умеет искать платформу 1С в
Linux-раскладке.

## Что изменено относительно оригинала

| Файл / часть | Изменение |
|---|---|
| `ConfasterMCP.csproj` | `net8.0`, зависимости через `PackageReference`, включён nullable-шум декомпилятора в `NoWarn` |
| `Properties/AssemblyInfo.cs` | удалён служебный атрибут `RefSafetyRulesAttribute` от декомпилятора |
| `ConfasterMCP.Help/HelpIndexer.cs` | `FindPlatformPath` знает `/opt/1cv8/x86_64`, `/opt/1C/1cv8/x86_64`, `/opt/1cv8`, `/opt/1C/1cv8`; `GetHelpDirectory` берёт `bin/`, если там `.hbk`, иначе корень версии |
| `libtree-sitter-bsl.so` | собрана из исходников грамматики вместо Windows-DLL |
| пакетирование | `.deb` и бинарный архив, self-contained + ReadyToRun |

## Что не перенесено

`Confaster.exe` — графическая оболочка оригинального дистрибутива
(WinForms + WebView2, `Microsoft.WindowsDesktop.App`). На Linux не запускается
и без переписывания интерфейса не переносится. Функциональность поиска целиком
живёт в MCP-сервере, поэтому в порте интерфейсом служит любой MCP-клиент.
Оценка: потерь в серверной части нет, потерян только Windows-GUI.

## Оптимизации

- self-contained публикация: не требует установленного dotnet-runtime;
- `PublishReadyToRun` — меньше времени до первого ответа;
- единая `libtree-sitter-bsl.so` (рантайм + грамматика) — один `DllImport`;
- индекс FTS5 с триграммами и инкрементальной синхронизацией;
- поиск платформы 1С кешируется, справка индексируется один раз.

## Как перепроверить паритет

```bash
scripts/fetch-original.sh          # свежий оригинал в original/
scripts/build.sh                   # порт в dist/
scripts/compare-upstream.sh        # сверка tools/list
scripts/smoke-test.sh              # протокольный смоук-тест
```
