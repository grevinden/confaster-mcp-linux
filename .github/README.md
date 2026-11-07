# ConfasterMCP для Linux

[![CI](https://github.com/grevinden/confaster-mcp-linux/actions/workflows/ci.yml/badge.svg)](https://github.com/grevinden/confaster-mcp-linux/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/grevinden/confaster-mcp-linux)](https://github.com/grevinden/confaster-mcp-linux/releases)

Порт MCP-сервера **1c-search** из состава [Confaster](https://confaster.ru/) под
Linux: поиск по выгрузкам конфигураций 1С — текст, символы, вызовы, объекты
метаданных, справка платформы. Работает с любым MCP-клиентом.

| | |
|---|---|
| Апстрим | ConfasterMCP `1.0.0+0c31141d` (дистрибутив от 2026-11-07) |
| Версия порта | `1.0.0-port.1` |
| Паритет | 17/17 инструментов оригинала совпадают по описаниям и схемам, плюс 4 инструмента справки |
| Пакеты | `.deb` (amd64, arm64) и бинарные архивы в [Releases](https://github.com/grevinden/confaster-mcp-linux/releases) |

## Быстрый старт

```bash
sudo dpkg -i confaster-mcp_1.0.0-port.1_amd64.deb
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
- [Портирование и аудит потерь](docs/porting.md)
- [Релизы и обновление апстрима](docs/releases.md) · [Диагностика](docs/troubleshooting.md)

История версий — [CHANGELOG.md](CHANGELOG.md).

## Устройство репозитория

```text
src/ConfasterMCP/        декомпилированный исходник порта (.NET 8)
scripts/                 сборка, пакеты, смоук-тест, сверка с оригиналом
packaging/               обёртка /usr/bin и man-страница
docs/                    документация
.github/                 эта страница, workflows CI и Release
VERSION                  версия порта (<апстрим>-port.N)
ORIGINAL.md              источник, хеши оригинального дистрибутива
```

`original/` (дистрибутив) и `build/`, `dist/` в git не входят и
восстанавливаются скриптами.

## Происхождение

Сервер получен декомпиляцией оригинального `ConfasterMCP.dll` и доработкой под
Linux. Права на оригинальный продукт принадлежат авторам Confaster; этот
репозиторий — сообществный порт, распространяется как есть, без гарантий.
Графическая оболочка `Confaster.exe` (WinForms + WebView2) не портирована —
она Windows-only, её роль выполняет MCP-клиент.
