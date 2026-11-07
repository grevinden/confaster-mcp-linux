# Документация ConfasterMCP для Linux

Порт MCP-сервера Confaster (1c-search): поиск по исходникам конфигураций 1С
с любого MCP-клиента на Linux. Поддерживаются выгрузка Конфигуратора
(`*.xml` + `*.bsl`) и проект 1C:EDT (`src/.../Module.bsl`).

| Документ | О чём |
|---|---|
| [install.md](install.md) | установка из `.deb`, бинарного архива или из исходников |
| [build.md](build.md) | сборка, зависимости, нативная библиотека tree-sitter |
| [usage.md](usage.md) | аргументы запуска, подключение MCP-клиентов, расширения |
| [tools.md](tools.md) | все инструменты: параметры и назначение |
| [architecture.md](architecture.md) | устройство порта, индекс, компоненты |
| [porting.md](porting.md) | отличия от апстрима, аудит потерь, подтверждение паритета |
| [releases.md](releases.md) | версии, проверка новой версии апстрима, сборка релиза |
| [troubleshooting.md](troubleshooting.md) | диагностика типовых проблем |

Короткая шпаргалка:

```bash
sudo dpkg -i confaster-mcp_1.0.0-port.4_amd64.deb     # установка
confaster-mcp --export-root /путь/к/выгрузке            # запуск (stdio)
```

Индекс создаётся в `<выгрузка>/.index/onec_search.db` автоматически.
