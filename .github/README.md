# ConfasterMCP для Linux

[![CI](https://github.com/grevinden/confaster-mcp-linux/actions/workflows/ci.yml/badge.svg)](https://github.com/grevinden/confaster-mcp-linux/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/grevinden/confaster-mcp-linux)](https://github.com/grevinden/confaster-mcp-linux/releases)

Порт MCP-сервера **1c-search** из состава [Confaster](https://confaster.ru/) под
Linux: поиск по исходникам конфигураций 1С — текст, символы, вызовы, объекты
метаданных, справка платформы. Работает с любым MCP-клиентом.

Поддерживаются оба формата исходников: полная выгрузка из Конфигуратора
(`*.xml` + `*.bsl`) и проект 1C:EDT (`src/.../Module.bsl`). Формат определяется
автоматически по корню, указанному в `--export-root`; их можно смешивать,
например основная конфигурация из Конфигуратора, расширение — из EDT.

| | |
|---|---|
| Апстрим | ConfasterMCP `1.0.0+0c31141d` (дистрибутив от 2026-11-07) |
| Версия порта | `1.0.0-port.7` |
| Паритет | 17/17 инструментов оригинала совпадают по описаниям и схемам, плюс 4 инструмента справки |
| Справка 1С | `8.3.27.2342` входит в пакет: объектная модель, встроенный язык и язык запросов; платформа 1С на машине не нужна |
| Пакеты | `.deb` (amd64, arm64) и бинарные архивы в [Releases](https://github.com/grevinden/confaster-mcp-linux/releases) |

## Быстрый старт

```bash
sudo dpkg -i confaster-mcp_1.0.0-port.7_amd64.deb
```

Подключение в MCP-клиенте:

```json
{
  "mcpServers": {
    "1c-search": {
      "command": "/usr/bin/confaster-mcp",
      "args": ["--export-root", "/путь/к/выгрузке/конфигурации"]
    }
  }
}
```

Расширения — дополнительными `--extension-root <путь>`. Индекс создаётся сам
в `<выгрузка>/.index/onec_search.db`.

## Из исходников

```bash
git clone https://github.com/grevinden/confaster-mcp-linux.git
cd confaster-mcp-linux
scripts/build.sh
scripts/run-mcp.sh --export-root /путь/к/выгрузке
```

Нужны .NET 8 SDK и gcc (для нативной библиотеки tree-sitter).

## Документация

Полное руководство — в каталоге [docs/](docs/README.md):

- [Установка](docs/install.md) · [Сборка](docs/build.md) · [Запуск и подключение](docs/usage.md)
- [Инструменты](docs/tools.md) · [Устройство](docs/architecture.md)
- [Портирование и паритет](docs/porting.md)
- [Релизы и обновление апстрима](docs/releases.md) · [Диагностика](docs/troubleshooting.md)

История версий — [CHANGELOG.md](CHANGELOG.md).

## Устройство репозитория

```text
src/ConfasterMCP/        декомпилированный исходник порта (.NET 8)
data/1c-help/            файлы справки платформы 1С, входят в пакеты
scripts/                 сборка, пакеты, смоук-тест, сверка с оригиналом
packaging/               обёртка /usr/bin и man-страница
docs/                    документация
.github/                 эта страница, workflows CI и Release
VERSION                  версия порта (<апстрим>-port.N)
ORIGINAL.md              источник, хеши оригинального дистрибутива
```

`original/` (дистрибутив) и `build/`, `dist/` в git не входят и
восстанавливаются скриптами. Файлы в `data/1c-help/` — издание 1С, права
принадлежат правообладателям платформы (см. `data/1c-help/<версия>/README.md`).

## Происхождение

Исходники порта — декомпилированный `ConfasterMCP.dll` с доработкой под Linux.
Права на оригинальный продукт принадлежат авторам Confaster; этот репозиторий —
сообществный порт, распространяется как есть, без гарантий. Графическая оболочка
`Confaster.exe` (WinForms + WebView2) в область порта не входит — переносится
только MCP-сервер, интерфейсом служит MCP-клиент.
