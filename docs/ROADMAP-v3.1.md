# ROADMAP v3.1: Puryx 3.0.0 → 3.1.0 «рыночная готовность»

> **Статус:** план к исполнению (составлен 23.10.2026 по итогам рыночного анализа октября 2026: CCleaner 7, MS PC Manager, Wise, Glary, WizTree, BleachBit, PrivaZer).
> **Каденция:** как в v3.0 — день = 1–4 атомарных коммита, дневной гейт (build 0/0 + все тесты зелёные), CHANGELOG [Unreleased] пополняется, релиз с reviewer-гейтом. Механика пушей — `docs/RELEASE-CADENCE.md`.
> **Базовая линия:** master = 1c873bc, CI зелёный, 475/475 локально (445 в CI с фильтром Integration).

## 0. Контекст и приоритеты (из рыночного анализа)

| Гэп | Приоритет | Обоснование |
|---|---|---|
| EN-локализация | **P0** | RU-only сужает аудиторию на порядки (у CCleaner 50+ языков). Механика, низкий риск |
| MFT-скан NTFS | **P0** | WizTree-класс: секунды вместо минут. Главный технический козырь. Высокий риск → ранняя фаза |
| Авто-обновление in-app | P1 | Пользователь не ходит на GitHub за exe |
| Трей-режим | P1 | Резидентность + фон для авто-обновления |
| Гранулярные правила браузеров | P1 | Keep-list cookies — opt-in, БЕЗ ущерба safety-философии |
| Deep-pass .chk/.tmp/.dmp | P2 | Advanced Cleaner у Wise |
| README: EN + приватность-позиционирование | P2 | Продажа отличий (OSS, без бандлов/телеметрии) |

**Не входит (осознанные решения):** Registry Cleaner (Microsoft предупреждает — наша защита от репутации CCleaner), Driver/Software Updater (вектор бандлов), обещания «ускорения», телеметрия, платные версии.

**Порядок фаз:** L (локализация) → M (MFT) → A (авто-апдейт + трей) → B (браузеры) → F (P2) → R (релиз). Обоснование: L — механика с гарантированным прогрессом, и её строки нужны M/A-фазам; M — самый рискованный кусок, идёт вторым (падение дешёвое, аудиторию уже разблокировали).

## Фаза L — Локализация (Дни 1–6) · code: App → Core

**Цель:** интерфейс на EN/RU, авто-детект по ОС, переключение в настройках. Нейтральная культура = EN (ключи без суффикса), русская — `Strings.ru.resx` с паритетом ключей.
**Инфраструктура:** `SmartCleaner.Core/Localization/Localizer.cs` (ResourceManager, static GetString(key), кэш), resx-файлы в Core (Core-сообщения — те же ключи), XAML — через `{x:Static loc:Localizer.Key}` (switch языка = рестарт приложения, стандартная практика). Паритет ключей RU↔EN — инвариант-тест.

- **День 1 — инфраструктура + пилот (MainWindow/About/Settings)**
  Коммиты: 1) `feat(i18n): Localizer + resx EN/RU + паритет-инвариант` (Localizer, Strings.resx, Strings.ru.resx с пилотными ключами, тесты: ключ существует в обоих, auto-detect ru→ru/остальные→en); 2) `feat(i18n): переключение языка в настройках + пилот MainWindow/About` (~30 ключей, настройка через IConfigService, рестарт-подсказка).
  Гейт: полный; смоук: запуск с `LANG=en` и ru — титул/меню на нужном языке.
- **День 2 — Views срез 1** (8 страниц: Dashboard, DiskMap, LargeFiles, Duplicates, Startup, Uninstaller, Quarantine, Scheduler): `feat(i18n): страницы среза 1 EN/RU`.
- **День 3 — Views срез 2** (9: Compact, SystemDeepClean, Plugins, AiAssistant, RamOptimizer, NetworkOptimizer, PrivacyDebloat, ServicesOptimizer, CliInspector + PreviewDialog/DiscoveryView/SettingsView): `feat(i18n): страницы среза 2 EN/RU`.
- **День 4 — ViewModel-строки** (статусы, toast, MessageBox-тексты, tooltips кодом): `refactor(i18n): строки ViewModel через Localizer`.
- **День 5 — Core-сообщения** (BlockReason, CleaningError-тексты, статусы сервисов — русские строки Core → ключи): `refactor(i18n): сообщения Core через Localizer`. Осторожно: тесты, ассертящие русский текст, — обновить на ключи/EN.
- **День 6 — EN-README + закрытие фазы**: `docs(i18n): English README` (README.md → двуязычный: EN основной + ссылка README.ru.md, или наоборот — сохранить RU-основной, EN рядом), `docs(i18n): локализация в фичи-список`. Инвариант: `grep '[А-Яа-я]' SmartCleaner.App/Views/*.xaml` вне `{x:Static loc:` — пусто (допустимые исключения: бренды).

## Фаза M — MFT-сканирование (Дни 7–10) · code: Core

**Цель:** анализ NTFS-тома чтением $MFT за секунды (WizTree-класс), с корректностью (allocated size, hardlink-учёт, fixup) и фолбэком.
**Точки интеграции:** `DiskUsageScanner`, `DiskMapEngine`, `LargeFilesEngine`. MFT — ТОЛЬКО чтение (никаких новых деструктивных векторов; элевация при необходимости — через существующий протокол elevated с политикой read-only-контракта).

- **День 7 — ядро MFT-ридера**: `feat(mft): парсер записей MFT` — pure functions (fixup/USA-массив, $FILE_NAME/$DATA, parent-reference, sequence) + синтетические фикстуры-тесты (модель disktree.rs: TornRead/Corrupt/Deleted/NotARecord). `test(mft): синтетические записи` первым коммитом (TDD).
- **День 8 — том и дерево**: `feat(mft): чтение $MFT и сборка дерева` — открытие тома (разные ридеры: direct/io), калибровка, build_tree по parent-reference, allocated-size (информация о кластерах $DATA), hardlink-не-двойной-учёт (extra_links), корни. Интеграционный тест на живом системном диске (Integration-трейт): сравнение top-папок с directory-walk (допуск на расхождения в метаданных).
- **День 9 — интеграция + UI**: `feat(mft): MFT-режим в сканерах` — DiskUsageScanner/DiskMap/LargeFiles: авто-выбор (NTFS+права → MFT, иначе фолбэк), индикатор режима, `Verb="runas"`-запуск elevated-анализа ТОЛЬКО по явному выбору с честной причиной фолбэка. UI-строки — через Localizer (фаза L).
- **День 10 — бенчмарк-приёмка**: `test(mft): бенчмарк против directory-walk` (Integration, тайм-аут на утверждение): системный диск ≥200 ГБ — MFT-режим < 15 с против walk; отчёт чисел в CHANGELOG Unreleased. Границы: не входит — USN-журнал (мониторинг изменений), FAT/exFAT-MFT (не существует), сетевые.

## Фаза A — Авто-обновление и трей (Дни 11–13) · code: App + Core

- **День 11 — UpdateChecker**: `feat(update): проверка релизов GitHub` — GitHub Releases API (последний non-draft non-prerelease), сравнение Version, извлечение SHA256 из body заметок, `feat → +` нет телеметрии. Тесты на стабах (HttpMessageHandler-шов): новая версия/та же/битые notes/сетевая ошибка → честные статусы.
- **День 12 — апдейтер-флоу**: `feat(update): установка обновления` — диалог «доступна X.Y.Z», скачивание в temp, верификация SHA256, rename-swap (текущий exe → .old, новый на место, запуск, .old-очистка при старте), опция «проверять при старте» (выключена по умолчанию — приватность). Portable/installed оба режима. Опять же только HTTPS + пиннинг хэша. Тесты swap-логики (файлы-фантомы).
- **День 13 — трей-режим**: `feat(tray): минимизация в трей` — TrayIcon (иконка Puryx), minimize→tray, контекстное меню (Открыть/Выход), опция в настройках; авто-проверка обновлений раз в сутки при резидентности (если включена).

## Фаза B — Браузеры (День 14) · code: Core

- **День 14 — гранулярные правила**: `feat(browsers): keep-list cookies и гранулярные категории` — BrowserScanner: категории с переключателями (кэш/история/cookies/сессии), cookies — RiskCategory.UserData + opt-in с подтверждением; keep-list доменов (whitelist-механика уже есть — переиспользовать). Тесты: cook­ies не чистятся без явного opt-in (инвариант безопасности).

## Фаза F — P2-финиш (День 15)

- **День 15**: `feat(system): deep-pass .chk/.tmp/.dmp` (SystemScanner: расширенный набор мусорных расширений, отдельная подкатегория с риском SafeToDelete); `docs(readme): приватность-позиционирование` (секция «Почему безопаснее CCleaner»: OSS/бандлы/телеметрия/registry-cleaner-отказ со ссылкой на предупреждение Microsoft).

## Фаза R — Релиз v3.1.0 (День 16)

- **День 16**: `docs(changelog): 3.1.0` (Unreleased → секция), `chore(release): bump 3.1.0`, тег, portable-артефакт, SHA256, reviewer-гейт (сверка CHANGELOG ↔ git log v3.0.0..HEAD, версии, README-счётчики), публикация (стратегию пуша выбирает пользователь, прецедент v3.0.0). Плюс: Scoop-манифест update (autoupdate подтянет сам — проверит checkver), winget-манифесты 3.1.0 (packaging/winget/3.1.0/ по образцу 3.0.0).

## Итог

16 дней + 2 буферных = **18 дней-пушей**. Версия: v3.1.0 (minor: новые возможности — локализация, MFT, авто-апдейт).

## Чек-лист приёмки v3.1.0

- [ ] EN/RU: все видимые строки через Localizer; инвариант паритета ключей зелёный; смоук-запуск обеих локалей
- [ ] MFT: системный диск ≥200 ГБ < 15 с; allocated/hardlink-корректность (сверка с walk); фолбэк без прав честен
- [ ] Авто-обновление: проверка+скачивание+swap+рестарт e2e-смоук; SHA256-верификация обязательна; выключено по умолчанию
- [ ] Трей: minimize→tray, меню, выход
- [ ] Браузеры: cookies чистятся ТОЛЬКО по явному opt-in (тест-инвариант)
- [ ] CI зелёный на теге; CHANGELOG ↔ факт; счётчики README = факт
- [ ] Scoop autoupdate подхватил 3.1.0 (проверка checkver после публикации релиза)
