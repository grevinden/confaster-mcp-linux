# Сборка

## Зависимости

| Инструмент | Зачем |
|---|---|
| .NET 8 SDK | сборка управляемого кода |
| `gcc` | `libtree-sitter-bsl.so` |
| `curl`, `unzip` | загрузка исходников грамматики и оригинала |
| `dpkg-deb` | сборка `.deb` |

Для arm64 дополнительно `gcc-aarch64-linux-gnu`.

## Скрипты

| Скрипт | Что делает |
|---|---|
| `scripts/build.sh` | публикация в `dist/` + сборка `libtree-sitter-bsl.so` |
| `scripts/run-mcp.sh --export-root …` | запуск сервера из `dist/` (или `bin/Release`) |
| `scripts/build-tree-sitter.sh [каталог]` | только нативная библиотека |
| `scripts/smoke-test.sh [dll\|exe] [корень]` | протокольный смоук-тест |
| `scripts/compare-upstream.sh [оригинал.dll]` | сверка инструментов с оригиналом |
| `scripts/build-deb.sh [amd64\|arm64]` | `.deb` в `build/` |
| `scripts/build-tarball.sh [amd64\|arm64]` | бинарный `.tar.gz` в `build/` |
| `scripts/fetch-original.sh` | скачать оригинал в `original/` |
| `scripts/check-upstream.sh` | проверить, не вышла ли новая версия |

## Управляемая часть

`src/ConfasterMCP` — декомпилированный исходник оригинального сервера
(ilspycmd), приведённый к нормальной сборке:

- `TargetFramework` — `net8.0`, зависимости через `PackageReference`
  (`Microsoft.Data.Sqlite`, `System.Text.Encoding.CodePages`) вместо `HintPath`;
- правки `HelpIndexer` под Linux-раскладку справки (см. [porting.md](porting.md));
- `Properties/AssemblyInfo.cs` — атрибуты сборки без служебного
  `RefSafetyRulesAttribute`.

`scripts/build.sh` выполняет `dotnet publish -c Release -o dist`, кладёт рядом
нативную библиотеку и копирует `data/1c-help` в `dist/1c-help`. Запуск —
`dotnet dist/ConfasterMCP.dll` или apphost.

## Справка платформы 1С

`data/1c-help/<версия>/` — файлы справки, поставляемые вместе с портом:
`shcntx_ru.hbk`, `shlang_ru.hbk`, `shquery_ru.hbk` и английские дубли
`shlang_root.hbk`, `shquery_root.hbk`. Дубли дают текст повреждённых страниц
русских книг. Имя каталога — точный номер версии платформы (`8.3.27.2342`);
оно определяет версию индекса справки и префикс `topic_id`. Состав и источник
описаны в `data/1c-help/<версия>/README.md`.

Все сборочные скрипты копируют этот каталог в свои артефакты:

| Скрипт | Куда попадает справка |
|---|---|
| `scripts/build.sh` | `dist/1c-help/` рядом с `ConfasterMCP.dll` |
| `scripts/build-deb.sh` | `/usr/share/confaster-mcp/1c-help/` |
| `scripts/build-tarball.sh` | `1c-help/` рядом с бинарником |

Приложение ищет справку в порядке `CONFASTER_1C_HELP_PATH` → `1c-help/` рядом
с бинарником → `/usr/share/confaster-mcp/1c-help` → установленная платформа 1С.
Проверка индекса (свежий индекс во временном каталоге, три раздела, поиск,
переиндексация) — `scripts/check-help-index.py [путь к бинарнику]`; её же
выполняет CI.

## Нативная библиотека tree-sitter

Сервер грузит `tree-sitter-bsl` через `DllImport` и ожидает в одном модуле
и `tree_sitter_bsl()`, и функции рантайма `ts_*`. Поэтому библиотека собирается
слитно из двух частей:

- рантайм tree-sitter `v0.25.6` (`lib/src/lib.c`);
- грамматика BSL — [alkoleft/tree-sitter-bsl](https://github.com/alkoleft/tree-sitter-bsl),
  зафиксирован коммит `b38125de` (сгенерированный `src/parser.c`).

Версии зафиксированы в `scripts/build-tree-sitter.sh`; при обновлении грамматики
проверяйте совместимость ABI: C#-обёртка передаёт `TSNode` как непрозрачные
32 байта, это работает для текущей раскладки структуры.

## Пакеты

`scripts/build-deb.sh`:

1. `dotnet publish -r linux-x64|linux-arm64 --self-contained -p:PublishReadyToRun=true`
   (dotnet-runtime в зависимостях не нужен, старт быстрее);
2. `libtree-sitter-bsl.so` в каталог приложения;
3. `data/1c-help` в `/usr/share/confaster-mcp/1c-help`;
4. раскладка `/usr/bin`, `/usr/lib/confaster-mcp`, `/usr/share/doc`, man;
5. `dpkg-deb --build`.

`scripts/build-tarball.sh` делает то же без deb-обвязки: справка кладётся
каталогом `1c-help/` рядом с бинарником.
