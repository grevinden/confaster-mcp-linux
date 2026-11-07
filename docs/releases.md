# Релизы и обновление под новую версию апстрима

## Версионирование

- `VERSION` в корне — единственный источник версии, формат `<апстрим>-port.<N>`.
- Тег GitHub — `v` + `VERSION`, например `v1.0.0-port.5`.
- `CHANGELOG.md` — история: таблица соответствия версий и изменения по релизам.
- Точный апстрим определяется по `AssemblyInformationalVersion` из
  `ConfasterMCP.dll`: `1.0.0+0c31141d…` — версия 1.0.0 и коммит апстрима.
- Новая версия апстрима → база версии меняется (`1.0.1-port.1`);
  исправления только в порту при том же апстриме → растёт `port.N`.

## Проверка новой версии

```bash
scripts/check-upstream.sh
```

Скрипт скачивает актуальный `Confaster.zip`, читает версию и коммит и сверяет
с `VERSION`. Код возврата `10` — версия апстрима новая. Даже если номер версии
тот же, сверяйте `sha256` архива с записью в `ORIGINAL.md`/`CHANGELOG.md`:
апстрим может пересобрать ту же версию.

**Проверка версии всегда требует перекомпиляции проекта.** Порт не подгружает
код апстрима во время работы: исходник лежит в репозитории (`src/`), поэтому
новый апстрим — это новый исходник, новая сборка, новые пакеты.

## Порядок обновления

1. **Скачать оригинал**

   ```bash
   scripts/fetch-original.sh
   scripts/check-upstream.sh
   ```

2. **Передекомпилировать**

   ```bash
   ~/.dotnet/tools/ilspycmd -p -o /tmp/redecomp/src \
       original/Libs/ConfasterMCP/ConfasterMCP.dll
   ```

3. **Перенести исходник и вернуть правки порта.** Правок ровно три файла
   (см. [porting.md](porting.md)): `ConfasterMCP.csproj`,
   `Properties/AssemblyInfo.cs`, `ConfasterMCP.Help/HelpIndexer.cs`.
   Рабочий приём: сохранить эти три файла, заменить остальной `src/ConfasterMCP`
   свежей декомпиляцией, вернуть файлы на место. Если апстрим менял
   `HelpIndexer`, правку про Linux-раскладку нужно наложить заново.

4. **Собрать и сверить**

   ```bash
   scripts/build.sh
   scripts/compare-upstream.sh          # 17/17 идентичных схем, 0 расхождений
   scripts/compare-behavior.sh <выгрузка>  # фактические ответы совпадают
   scripts/smoke-test.sh
   ```

   Дополнительно: прогнать `sync_index` и `find_symbol` на реальной выгрузке,
   проверить инструменты справки.

5. **Записать версию**

   ```bash
   echo 1.0.1-port.1 > VERSION
   # CHANGELOG.md: новая секция + строка в таблице соответствия
   ```

6. **Закоммитить и выпустить**

   ```bash
   git add -A && git commit -m "feat: апстрим 1.0.1+<commit>"
   git tag v1.0.1-port.1
   git push origin main v1.0.1-port.1
   ```

## CI/CD

| Workflow | Когда | Что делает |
|---|---|---|
| `.github/workflows/ci.yml` | push в `main`, pull request | `scripts/build.sh`, смоук-тест, `.deb` и tar.gz как артефакты сборки |
| `.github/workflows/release.yml` | тег `v*` | проверяет совпадение тега и `VERSION`, собирает `.deb` amd64/arm64 и tar.gz, публикует GitHub Release |

Артефакты релиза:

```text
confaster-mcp_<версия>_amd64.deb
confaster-mcp_<версия>_arm64.deb
confaster-mcp_<версия>_linux-amd64.tar.gz
confaster-mcp_<версия>_linux-arm64.tar.gz
```

Локальная сборка тех же артефактов: `scripts/build-deb.sh`,
`scripts/build-tarball.sh` (в `build/`).
