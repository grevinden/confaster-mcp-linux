# Установка

## Требования

- Linux x86_64 или arm64, glibc 2.31+.
- `libicu` (для .NET; в `.deb` указан как зависимость).
- Исходники конфигурации 1С: полная выгрузка из Конфигуратора (`Configuration.xml`, `*.bsl`) или проект 1C:EDT (`src/.../Module.bsl`). Подробности — [usage.md](usage.md).

Платформа 1С не требуется: справка `8.3.27.2342` входит в пакет, инструменты
справки работают сразу после установки.

## Из пакета .deb

Пакет self-contained: dotnet-runtime не нужен.

```bash
sudo dpkg -i confaster-mcp_1.0.0-port.7_amd64.deb
# если dpkg сообщит о неудовлетворённых зависимостях:
sudo apt-get install -f
```

Ставится:

| Путь | Что |
|---|---|
| `/usr/bin/confaster-mcp` | обёртка запуска |
| `/usr/lib/confaster-mcp/` | приложение, .NET-рантайм, `libtree-sitter-bsl.so` |
| `/usr/share/confaster-mcp/1c-help/` | справка платформы 1С (`*.hbk`) |
| `/usr/share/doc/confaster-mcp/` | README, CHANGELOG, copyright |
| `/usr/share/man/man1/confaster-mcp.1.gz` | man-страница |

Проверка:

```bash
man confaster-mcp
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"probe","version":"0"}}}' \
  | timeout 5 confaster-mcp --export-root /путь/к/выгрузке
```

Ответ `initialize` содержит `serverInfo.name = "1c-search"`.

## Из бинарного архива

```bash
tar xzf confaster-mcp_1.0.0-port.7_linux-amd64.tar.gz -C ~/confaster-mcp
~/confaster-mcp/ConfasterMCP --export-root /путь/к/выгрузке
```

В архиве рядом с `ConfasterMCP` лежат `libtree-sitter-bsl.so` и каталог
`1c-help/` со справкой платформы 1С.

## Из исходников

```bash
git clone https://github.com/grevinden/confaster-mcp-linux.git
cd confaster-mcp-linux
scripts/build.sh
scripts/run-mcp.sh --export-root /путь/к/выгрузке
```

Подробности сборки — [build.md](build.md). Сборка пакетов:
`scripts/build-deb.sh [amd64|arm64]`, `scripts/build-tarball.sh [amd64|arm64]`.
