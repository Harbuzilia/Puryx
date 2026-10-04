# Исследование: публикация Puryx в winget (и альтернатива Scoop)

> ROADMAP, День 28. Контекст: Puryx — portable single-file exe (~70 МБ, .NET 8, WPF), без подписи кода, публичный репо `github.com/Harbuzilia/Puryx`, целевая версия сабмита — 3.0.0.
>
> Дата исследования: 04.10.2026. Все ключевые утверждения подтверждены источниками (раздел «Источники», доступность ссылок проверена на дату исследования).

---

## Ответы на открытые вопросы

### 1. Команда и формат вывода `winget validate`

Команда (документация Microsoft Learn, страница «validate command»):

```
winget validate [--manifest] <manifest> [<options>]
```

Для локального манифеста:

```
winget validate --manifest <путь-к-файлу-манифеста-или-папке>
```

Полезные опции: `--nowarn` / `--ignore-warnings` (подавить предупреждения), `--verbose-logs`, `--disable-interactivity`.

Формат вывода (подтверждён реальными сессиями в issue/дискуссиях winget-cli):

- успех: `Manifest validation succeeded.`
- успех с предупреждениями: `Manifest validation succeeded with warnings.` + строки `Manifest Warning: <описание>` (например, использование restricted-полей типа `Agreements`; замечено, что предупреждения могут давать ненулевой exit code — issue #3405)
- ошибка: `Manifest validation failed.` + строки `Manifest Error: <описание с указанием поля/строки>`

### 2. Актуальная версия схемы манифеста

Версия схемы манифеста ≠ версия winget CLI.

- Каталог схем `winget-pkgs` (`doc/manifest/schema/`) на дату исследования содержит: 1.0.0, 1.1.0, 1.2.0, 1.4.0, 1.5.0, 1.6.0, 1.7.0, 1.9.0, 1.10.0, **1.12.0**, **1.28.0**. Самая свежая задокументированная схема — **1.28.0**.
- Актуальный клиент winget — ветка 1.28 (в issue #6245 от сентября 2026 виден `winget --info` → `Windows Package Manager v1.28.240`).
- Однако **PR-шаблон winget-pkgs требует: «Manifest conforms to the 1.12 schema»** — т.е. для сабмита целимся в схему **1.12.0** (выпущена с клиентом WinGet 1.12, поддерживается широким парком клиентов).
- Что нового в 1.28.0 относительно 1.12.0 (README схемы 1.28): добавлены `Icons` (не авторское поле — заполняется пайплайном валидации, в PR добавлять нельзя) и `DesiredStateConfiguration`. Клиент 1.28 поддерживает не все поля схемы 1.28.
- **Важно:** singleton-формат (весь манифест одним YAML) **deprecated в community-репо**: клиент его поддерживает, но в winget-pkgs принимаются только multi-file манифесты (version + defaultLocale + installer + опциональные locale-файлы), один пакет-версия на PR.

### 3. Формат заголовка PR «New package»

Из `.github/PULL_REQUEST_TEMPLATE.md` репозитория microsoft/winget-pkgs:

```
New package: Publisher.Name version X.Y.Z
Update: Publisher.Name to X.Y.Z
```

Для нас: **`New package: Harbuzilia.Puryx version 3.0.0`**.

Чеклист PR (тот же шаблон): подписанный Microsoft CLA (одноразово), нет других открытых PR на тот же манифест, PR меняет ровно один манифест, локально пройдены `winget validate --manifest <path>` и `winget install --manifest <path>`, манифест соответствует схеме 1.12.

### 4. Требования к portable-типу (`InstallerType: portable`)

- Portable поддерживается с WinGet 1.3; zip-архивы — с WinGet 1.5 (schema 1.12.0, installer.md).
- Обязательные поля инсталлера: `Architecture`, `InstallerType`, `InstallerUrl`, `InstallerSha256`.
- `InstallerSha256` — обязателен; считается `winget hash <файл>`. Несовпадение хэша на валидации → лейбл `Error-Hash-Mismatch`.
- `InstallerUrl`: только HTTPS (`Validation-HTTP-Error`), URL должен вести **напрямую на release-локацию издателя** (политика 1.1.4: «must be the ISV's release location»; download-сайты запрещены), редиректы/«vanity URL» запрещены (`Validation-Indirect-URL`). GitHub Releases нашего репозитория — легитимная release-локация.
- URL должен быть стабильным на версию: если файл по URL подменили без смены манифеста — `Validation-Hash-Verification-Failed`; каждое обновление хэша = новый манифест.
- «Голый» .exe напрямую как portable — поддерживается (но .com — нет; PWA и шрифты вне этой схемы). Если файл — portable exe, в манифесте обязан быть `InstallerType: portable`, а не `exe` (doc/Validation.md).
- Требование «silent install» из политик community-репо к portable фактически неприменимо: установщик не запускается, файл копируется; silent-ключи не нужны.
- `PortableCommandAlias` — опциональный алиас команды (валиден только для portable / `NestedInstallerType: portable`).
- **Подпись кода не требуется.** В политиках репозитория требования подписи нет; подпись обязательна только для MSIX (schema docs, `SignatureSha256`). НО:
  - Сабмит проходит «Installers Scan» — прогон по нескольким антивирусам; неподписанный exe неизвестного издателя имеет повышенный риск детекта как PUA → лейбл `Binary-Validation-Error`, при ложном срабатывании файл отправляют на разбор в Microsoft Defender (wdsi filesubmission).
  - Утилита очистки диска — категория повышенного внимания AV (доступ/удаление файлов). Это риск, а не блокер.
  - SmartScreen на машинах пользователей будет показывать предупреждение для неподписанного exe — UX-проблема, не барьер сабмита.

---

## Механика сабмита в microsoft/winget-pkgs

По шагам из официальной инструкции Microsoft Learn «Submit your manifest to the repository»:

1. **Локальная валидация**: `winget validate --manifest <путь>`.
2. **Тест установки в Windows Sandbox**: `.\Tools\SandboxTest.ps1 <путь-к-манифесту>` из клона winget-pkgs.
3. **Fork** `microsoft/winget-pkgs`, клонирование с частичной историей: `git clone --filter=blob:none --no-checkout <fork>`.
4. **Sparse checkout**: `git sparse-checkout set manifests\h\Harbuzilia` (первая буква publisher — нижний регистр).
5. `git checkout`, затем `git checkout -b <branch>`.
6. Файлы в структуру **`manifests/h/Harbuzilia/Puryx/3.0.0/`** — multi-file:
   - `Harbuzilia.Puryx.yaml` (version: `PackageIdentifier`, `PackageVersion`, `DefaultLocale`, `ManifestType: version`, `ManifestVersion`)
   - `Harbuzilia.Puryx.installer.yaml` (`ManifestType: installer`, список `Installers`)
   - `Harbuzilia.Puryx.locale.en-US.yaml` (`ManifestType: defaultLocale`)
   - `PackageIdentifier`/`PackageVersion` обязаны совпадать с путём папок.
7. `git commit`, `git push`.
8. **PR в microsoft/winget-pkgs**, заголовок `New package: Harbuzilia.Puryx version 3.0.0`.

Что происходит дальше (пайплайн валидации, лейблы):

- Автоматика: проверка схемы/путей (`Manifest-Validation-Error`, `Manifest-Path-Error`), репутация и доступность URL (`URL-Validation-Error`, `Validation-HTTP-Error`, `Validation-Indirect-URL`), скачивание и сверка хэша (`Error-Hash-Mismatch`, `Validation-Hash-Verification-Failed`), статический и динамический антивирусный скан (`Binary-Validation-Error`, `Validation-Defender-Error`), установка/удаление под admin и non-admin (`Validation-Installation-Error`, `Validation-Uninstall-Error`, `Validation-Executable-Error`).
- Лейблы статуса: `Azure-Pipeline-Passed` → (новые пакеты — ручной ревью модератора) → `Validation-Completed` → мерж. Ошибки → `Needs-Author-Feedback`; без реакции автора за 10 дней бот закрывает PR.
- Обновления версий у уже принятых пакетов при чистом прогоне часто мержатся автоматически; первый «New package» всегда ждёт модератора.
- Первый PR потребует подписать **Microsoft CLA** (бот подскажет, одноразово).

## Механика обновлений

- Каждая новая версия = новая папка `manifests/h/Harbuzilia/Puryx/<версия>/` и новый PR («Update: Harbuzilia.Puryx to X.Y.Z»), одна версия на PR.
- Помощник подтверждён источником: **wingetcreate** (`github.com/microsoft/winget-create`, ставится `winget install wingetcreate`):
  - `wingetcreate new` — интерактивная генерация multi-file манифеста с нуля (сама считает SHA256, определяет тип инсталлера);
  - `wingetcreate update <id> -u <urls> -v <version> -t <GitHub PAT> --submit` — обновление манифеста и автосоздание PR;
  - официально документирован сценарий в CI/CD: примеры GitHub Actions — microsoft/terminal, microsoft/PowerToys, oh-my-posh, microsoft/edit. Т.е. релизный workflow Puryx может сам сабмитить новую версию в winget-pkgs.
- Альтернативный генератор из репо winget-pkgs: `Tools/YamlCreate.ps1`.

## Альтернатива: Scoop

- Scoop-бакет — это просто git-репо с JSON-манифестами. **Свой бакет не требует чьего-либо одобрения**: пользователи подключают его командой `scoop bucket add puryx https://github.com/Harbuzilia/scoop-bucket`.
- Шаблон `ScoopInstaller/BucketTemplate` даёт из коробки: CI-проверки манифестов, **autoupdate каждые 4 часа** (автообновление версий и хэшей из релизов), автозакрытие типовых issue (404, hash mismatch).
- Попадание в официальные бакеты сложнее: Main — в основном консольные инструменты с критериями отбора; GUI-приложения идут в Extras, где есть ожидания по известности. Свой бакет — нулевой гейткипинг, работает с первого дня.
- Минус: меньшая «встроенность» (Scoop надо установить и подключить бакет), winget предустановлен в Windows 10/11.

---

## Вердикт

### Что нужно для сабмита в winget

1. Опубликованный GitHub Release **v3.0.0** с exe-ассетом со стабильным именем (например, `SmartCleaner.App.exe`) → прямой HTTPS URL вида `https://github.com/Harbuzilia/Puryx/releases/download/v3.0.0/SmartCleaner.App.exe`.
2. SHA256 ассета (`winget hash SmartCleaner.App.exe`).
3. Multi-file манифест (генерируется `wingetcreate new`; черновик-основа — `packaging/winget/Harbuzilia.Puryx.yaml`, singleton по схеме 1.12.0).
4. Подписанный Microsoft CLA (одноразово, при первом PR).
5. Локально: `winget validate --manifest` + `winget install --manifest` (желательно ещё SandboxTest.ps1).

### Что блокирует сейчас

1. **Релиз v3.0.0 не опубликован** — нет финальных `InstallerUrl`/`InstallerSha256`. Единственный жёсткий блокер.
2. Отсутствие подписи кода — не блокер политики, но риск AV/PUA-детекта (`Binary-Validation-Error`) и SmartScreen-предупреждения у пользователей (решение по подписи — см. `docs/CODE-SIGNING.md`).
3. Singleton-формат deprecated в winget-pkgs — для сабмита нужна конвертация в multi-file (решается wingetcreate за минуты).

### Рекомендуемый путь

1. **Сейчас**: свой Scoop-бакет по `BucketTemplate` — публикация без ревью, autoupdate из коробки, охват Scoop-аудитории.
2. **На релизе v3.0.0**: `wingetcreate new` → multi-file манифест → PR `New package: Harbuzilia.Puryx version 3.0.0` в winget-pkgs.
3. **После принятия**: в release-CI добавить шаг `wingetcreate update --submit` (GitHub PAT в секретах) — обновления в winget уйдут в автомат.
4. **Параллельно**: вернуться к вопросу подписи кода для снижения AV/SmartScreen рисков.

---

## Источники

- `winget validate` — usage и опции: <https://learn.microsoft.com/en-us/windows/package-manager/winget/validate>
- Формат вывода validate: <https://github.com/microsoft/winget-cli/discussions/991>, <https://github.com/microsoft/winget-cli/issues/3405>
- Актуальный клиент v1.28.x: <https://github.com/microsoft/winget-cli/issues/6245>
- Сабмит, sparse checkout, валидационный пайплайн, лейблы: <https://learn.microsoft.com/en-us/windows/package-manager/package/repository>
- Политики репозитория (1.1.4 — URL издателя; без требования подписи): <https://learn.microsoft.com/en-us/windows/package-manager/package/windows-package-manager-policies>
- Формат заголовка PR и чеклист: <https://github.com/microsoft/winget-pkgs/blob/master/.github/PULL_REQUEST_TEMPLATE.md>
- Каталог схем (список версий): <https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema>
- Схема 1.12.0, installer (portable с 1.3, обязательность InstallerSha256, подпись только для MSIX): <https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.12.0/installer.md>
- Схема 1.12.0, singleton (deprecation warning): <https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.12.0/singleton.md>
- Схема 1.28.0, README (diff от 1.12, multi-file требование): <https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/README.md>
- «portable exe должен быть portable, а не exe»: <https://github.com/microsoft/winget-pkgs/blob/master/doc/Validation.md>
- wingetcreate (команды new/update/submit, CI/CD): <https://github.com/microsoft/winget-create>
- Scoop: бакеты и свой бакет: <https://github.com/ScoopInstaller/Scoop/wiki/Buckets>
- Scoop BucketTemplate (CI + autoupdate): <https://github.com/ScoopInstaller/BucketTemplate>
