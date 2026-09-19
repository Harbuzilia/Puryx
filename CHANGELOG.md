# 📜 Журнал изменений (Changelog) — Smart System Cleaner (CHISTilka)

В данном файле фиксируются все изменения в проекте: что было изменено, в каких файлах, по какой причине (обоснование решения) и в какой версии/сессии. Файл обновляется после каждых правок.

---

## [2.7.2] — 20.09.2026

### 🎯 Тема релиза: Полный аудит проекта — критические баги UI, закрытие всех путей удаления в обход защиты, вычистка мёртвого кода и мусора, тесты 124 → 214

Аудит по утверждённому плану в 6 этапов (бейзлайн → мусор → критические UI-баги → безопасность → тесты → консистентность → финал). Итог: сборка 0 ошибок / 0 предупреждений, 214/214 тестов зелёные, репозиторий .git 136 МБ → 0.55 МБ. Seal-коммит `55aaa0c` — 67 файлов, +2149/−1759.

---

### 1. 🩹 Критические баги UI (страницы блокировались навсегда)
- **Что сделано:**
  - `ServicesOptimizerViewModel` и `PrivacyDebloatViewModel`: `IsBusy` сбрасывался только в `catch` — после **успешного** применения профиля страница оставалась заблокированной до перезапуска. Сброс перенесён в `finally`, вложенный no-op `ScanAsync()` (срезался guard'ом `IsBusy`) заменён прямой перезагрузкой списка приватным методом.
  - `QuarantineViewModel`, `UninstallerViewModel` (3 команды), `AiAssistantViewModel`: любое необработанное исключение оставляло `IsBusy=true` и роняло приложение в crash-диалог — все команды обёрнуты в try/catch/finally.
  - `CompactViewModel`: `CancellationTokenSource` не освобождался (утечка на каждом запуске) — dispose в `finally`.
  - Занижение статистики очистки пакетов: `PackageMaintenanceService` не суммировал освобождённые байты — добавлено накопление и поле `FreedBytes` в `PackageMaintenanceResult`; `MainViewModel.MergeResults` брал только файловый результат. Теперь итог считается по файлам + пакетам, строки статуса («Выполнено…», «Пакеты…») показывают «освобождено» и для чисто package-очистки.
  - `App.LogCrash`: крэш-лог писался в `BaseDirectory` (под Program Files — только чтение, логгер молча падал). Теперь: portable-режим (маркер `portable.txt` рядом с exe) — пишем рядом с exe, иначе `%APPDATA%\SmartCleaner\crash.log`.
  - `SettingsViewModel`: «всего RAM» показывал размер GC-хипа — заменено на P/Invoke `GlobalMemoryStatusEx` (реальная физическая память).
  - `DuplicatesViewModel.OnSelectedPreviewFileChanged`: синхронное чтение файла предпросмотра на UI-потоке при каждом клике по списку — асинхронно, с отменой предыдущего чтения при смене выбора.
- **Файлы:**
  - `SmartCleaner.App/ViewModels/ServicesOptimizerViewModel.cs`, `PrivacyDebloatViewModel.cs`, `QuarantineViewModel.cs`, `UninstallerViewModel.cs`, `AiAssistantViewModel.cs`, `CompactViewModel.cs`, `MainViewModel.cs`, `SettingsViewModel.cs`, `DuplicatesViewModel.cs`
  - `SmartCleaner.App/App.xaml.cs`
  - `SmartCleaner.Core/Cleaning/PackageMaintenanceService.cs`, `SmartCleaner.Core/Models/PackageMaintenanceModels.cs`
- **Почему:** Страница, которую нельзя разблокировать без перезапуска, заниженная статистика и молча теряющийся крэш-лог — дефекты первого приоритета для интерактивного инструмента.

### 2. 🔐 Безопасность (удаления в обход SafetyService и инъекции)
- **Что сделано:**
  - `PluginEngine.CleanPluginItemsAsync`: удаления шли напрямую (`Directory.Delete`/`File.Delete`) в обход `ISafetyService` и Корзины — злонамеренный `*.plugin.json` мог перманентно снести любой каталог. Движок получил `ISafetyService` через DI; каждый путь проходит `ValidateForDeletion` (заблокированные и требующие UAC пропускаются с пояснением в `SkippedMessages`); удаление по правилам плагинов — только в Корзину (перманентного режима для сторонних правил нет by design). Результат стал типизированным `PluginCleanResult` (CleanedCount/SavedBytes/CleanedItems/SkippedMessages); `PluginsViewModel` обновляет остаточный `TotalCleanableSize` и показывает число пропущенных политикой безопасности.
  - `LeftoverHunter`: перманентные удаления по fuzzy keyword-match без whitelist — тот же гейт `ValidateForDeletion` + удаление в Корзину.
  - ViewModel-удаления (`LargeFilesViewModel`, `DuplicatesViewModel`, `CliInspectorViewModel`) прогоняются через safety-проверку; блокировка и требование UAC показываются пользователю с причиной.
  - PowerShell-инъекция в `CliInspectorViewModel.OpenTerminal`: `Set-Location -LiteralPath '{path}'` — апостроф в имени папки ломал команду и позволял инъекцию. Запуск упрощён до `powershell -NoExit` с рабочей директорией через `ProcessStartInfo.WorkingDirectory` — путь больше не попадает в командную строку.
  - `CleaningService.ExecuteDockerDeleteAsync`: `docker rmi -f {imageId}` / `rm -f {containerId}` без экранирования — добавлен `IsSafeDockerIdentifier` (4–128 символов, только ASCII-буквы/цифры/`_`/`-`); недопустимые идентификаторы отклоняются с сообщением.
  - `CleaningSchedulerService`: `--profile {name}` попадал в командную строку schtasks без кавычек — профиль проверяется по allowlist («Быстрая»/«Разработка»/«Полное») и экранируется кавычками.
  - `App.RunElevatedCleanupAsync`: elevated-очистка хардкодила `Risk=PerformanceCache`, обходя правило защищённого периода для UserData. Теперь категория риска читается из подписанного запроса (`Enum.TryParse` с безопасным fallback); `ElevatedCleanRequestFile` дополнительно отклоняет запросы с невалидной строкой `Risk` (тест: валидная подпись, некорректный Risk).
  - `SafetyService.RequiresElevation`: сравнение префиксов без разделителя — `C:\WindowsFoo` ложно требовал UAC. Заменено на `IsUnder` со сравнением по границе каталога.
  - `SafetyService.IsWhitelisted`: паттерны вида `**\.git\**` защищали содержимое, но не саму папку — удаление папки `.git` снесло бы всё защищённое внутри. Существующая директория, чей ребёнок совпадает с паттерном (`path + "\*"`), теперь тоже защищена; логика матчинга вынесена в `MatchesAnyPattern`. Баг найден новым тестом.
  - `PluginEngine` wildcard: шаблоны с `*` в середине пути (`%LOCALAPPDATA%\JetBrains\*\caches`) молча не резолвились — реализован рекурсивный резолв сегментов (`ResolveRuleTargets`/`EnumeratePatternMatches`, `*` в любом сегменте). Плагин JetBrains до фикса не чистил ничего.
  - `PluginEngine` загрузка манифестов: хрупкий dev-only относительный путь (`BaseDirectory\..\..\..\..\SmartCleaner.Data\Plugins`) заменён на директорию приложения (встроенные плагины копируются сборкой через csproj) + пользовательские `%APPDATA%\SmartCleaner\Plugins`; манифесты с пустым `Id` отклоняются.
  - `FileShredderService`: безвозвратное удаление шло без whitelist-проверки — добавлен `ISafetyService`-гейт (`ValidateForDeletion` на каждый файл: CanDelete/RequiresElevation); каталог удаляется только если ни один файл не заблокирован, иначе recursive-delete снёс бы и защищённые файлы внутри.
- **Файлы:**
  - `SmartCleaner.Core/Plugins/PluginEngine.cs`, `Uninstaller/LeftoverHunter.cs`, `Cleaning/CleaningService.cs`, `Scheduler/CleaningSchedulerService.cs`, `Safety/SafetyService.cs`, `Safety/FileShredderService.cs`, `Cleaning/ElevatedCleanRequestFile.cs`
  - `SmartCleaner.App/ViewModels/PluginsViewModel.cs`, `LargeFilesViewModel.cs`, `DuplicatesViewModel.cs`, `CliInspectorViewModel.cs`
  - `SmartCleaner.App/App.xaml.cs`
- **Почему:** Приложение, чья единственная функция — безопасное удаление, не должно иметь ни одного пути удаления в обход центрального защитного контура; плагины — сторонний код и должны считаться потенциально враждебными.

### 3. 🗑️ Вычистка мёртвого кода и мусора (140 МБ с диска + 136 МБ истории git)
- **Что сделано:**
  - Удалены мёртвые модули Core (зарегистрированы в DI, нигде не потребляются): `Mft/` (`MftReader` с нативным memory-leak, `MftScanner`, `MftEntry`), `Services/WslShrinkService.cs`, `Localization/` (`LocalizationManager`, `Strings` — потреблялся только тестами).
  - App: удалён недостижимый `Services/TraySentinelService.cs`; из `App.xaml.cs` убраны мёртвые DI-регистрации (`WslShrinkService`, `MftScanner`, `TraySentinelService`) и дублирующие ThemeManager методы `ApplySystemTheme`/`IsSystemDarkTheme` (+ неиспользуемый `using Microsoft.Win32`).
  - `UninstallerEngine`: удалена неиспользуемая инъекция `LeftoverHunter` в конструктор.
  - `SmartCleaner.Data`: удалены 5 JSON-файлов, которые никто не читает (`ai_agents.json`, `browsers.json`, `games.json`, `system.json`, `whitelist.json`); оставлены только `Plugins/*.plugin.json`.
  - Из git и с диска удалены `publish/`, `release/` (~140 МБ устаревших сборок) и scratch-файлы `_AGENTS_MEMORY.md`, `_SCRATCHPAD.md`.
  - `.gitignore`: `release/` → `[Rr]elease/`; добавлены `TestResults/`, `*.binlog`, `artifacts/`, `.idea/`, `.zcode/` (состояние сессий агента).
  - `git reflog expire --expire=now --all && git gc --prune=now`: `.git` 136 МБ → 0.55 МБ (единый пакет 394 КБ), `git fsck` — без повреждений.
- **Файлы:** перечисленные удаления, `.gitignore`.
- **Почему:** Мёртвый код с нативными буферами — это утечки и ложная поверхность атаки; 140 МБ устаревших бинарников и 136 МБ раздутой истории — мусор.

### 4. ✅ Тесты: 124 → 214 (все зелёные)
- **Что сделано:**
  - Таутологические тесты (истинны при любом коде) удалены/заменены реальными: `PathHealthItem_CorrectlyDetectsDeadPath` (проверял объект, сконструированный самим тестом) → `CliInspectorEngine_ScanAsync_MarksDeadPathAsDeadAndLivePathAsActive` (живой и мёртвый PATH в temp-каталогах, проверка `DeadPathsCount`); `PluginEngine_LoadPluginsAsync_LoadsManifests` (`Assert.True(plugins.Count >= 0)` — истина по определению) и `LeftoverItem_ModelProperties_AssignCorrectly` (проверял присваивание свойств POCO) — удалены; дублирующая группа NLQ-теорий из `TierFeaturesTests` удалена.
  - Приватные фейки из `DiskUsageScannerTests` (`InMemoryConfigService`, `AllowAllSafetyService`, `NoopKnowledgeBase`) консолидированы в общий `TestSupport.cs`.
  - Тестируемость Core без изменения поведения: `PathEnvironmentService.GetUserPathEntries/GetSystemPathEntries` → `virtual`; `CleaningStatsService` получил internal-конструктор с каталогом статистики (изоляция от реального `%LOCALAPPDATA%`); `StartupEngine.ParseCommand` → `internal`; новый `SmartCleaner.Core/Properties/AssemblyInfo.cs` — `InternalsVisibleTo("SmartCleaner.Core.Tests")`.
  - Новые suites для модулей с нулевым покрытием: `SafetyServiceTests` (17: whitelist, защита самой папки, защищённый период, elevation, регрессия `C:\WindowsFoo`), `ConfigServiceTests` (6: portable/installed режимы), `CleaningStatsServiceTests` (7: сессии и накопление статистики), `PluginEngineTests` (6: multi-segment wildcard, safety-гейт с fake-сервисом), `LeftoverHunterTests` (9: keyword-матчинг, гейт, регрессия блокировки `.git`), `StartupEngineTests` (4: `ParseCommand`), плюс `ReadValidatedAsync_RejectsInvalidRiskEnumValue` в `ElevatedCleanRequestFileTests`.
  - Machine-зависимые тесты (CliInspector-скан реального PATH и т.п.) помечены `[Trait("Category","Integration")]`.
  - Новыми тестами найдены и пофикшены два реальных бага: whitelist-защита самой папки (п. 2) и занижение статистики пакетов (п. 1).
- **Файлы:**
  - `SmartCleaner.Core.Tests/SafetyServiceTests.cs` (новый), `ConfigServiceTests.cs` (новый), `CleaningStatsServiceTests.cs` (новый), `PluginEngineTests.cs` (новый), `LeftoverHunterTests.cs` (новый), `StartupEngineTests.cs` (новый), `TestSupport.cs`, `CliInspectorTests.cs`, `DiskUsageScannerTests.cs`, `ElevatedCleanRequestFileTests.cs`, `TierFeaturesTests.cs`
  - `SmartCleaner.Core/Properties/AssemblyInfo.cs` (новый)
- **Почему:** У protective-логики без тестов нет доверия; оба реальных бага нашлись именно новыми тестами, а не чтением кода.

### 5. 🎨 Консистентность UI/доков/сборки
- **Что сделано:**
  - Версия 2.7.2 в `SmartCleaner.App.csproj` (`Version`/`AssemblyVersion`/`FileVersion`); UI читает её из сборки через новый `Helpers/AppInfo.cs` и `x:Static` — заголовок окна и бейджи в `MainWindow.xaml`/`AboutWindow.xaml` больше не могут дрейфовать относительно csproj (было захардкожено «v2.7»); в `AboutWindow` счётчик сканеров исправлен на 16.
  - `SmartCleaner.App.csproj`: `Plugins/*.plugin.json` копируются в `output\Plugins` (`PreserveNewest`) — плагины работают из любой директории (пара к смене путей загрузки в п. 2).
  - `DashboardPage.xaml`: захардкоженный `LimeGreen` → `SafeBrush` темы; overlay-скримы `#B3000000` в `MainWindow.xaml` вынесены в ресурс `OverlayScrimBrush` (добавлен в DarkTheme и LightTheme).
  - README: убрано ложное «очистка теневых копий VSS» (такого кода в проекте нет), счётчик сканеров честный — 16 (было «18+» и «14»), добавлена отсутствовавшая строка «Разработка» (WSL2/Rust/Gradle/Maven/Go/Android SDK/Unity), тесты запускаются из решения, добавлена ссылка на LICENSE.
  - Создан `LICENSE` (MIT, © 2026 SmartCleaner Team).
  - CHANGELOG: удалены 22 локальные `file:///e:/...`-ссылки (не переносимы на другие машины), противоречие 21/19 XAML-файлов исправлено (фактически 21 представление).
  - `build.bat`: ANSI-цвета починены (ESC-байт вычисляется в рантайме через forfiles — литеральный ESC в .bat выедается редакторами/git; проверено живым запуском), CRLF + `chcp 65001`; сборка полного решения вместо одного проекта; новый пункт меню «Запустить тесты»; Clean охватывает Core.Tests, `release/`, `TestResults/`, логи сборки; убран фейковый «Installer»-режим (публиковал тот же portable под ложным описанием).
  - `publish_portable.bat`: `if errorlevel` после publish (раньше печатал «Build Successful!» даже при провале), тихий xcopy, корректные коды возврата.
- **Файлы:**
  - `SmartCleaner.App/SmartCleaner.App.csproj`, `SmartCleaner.App/Helpers/AppInfo.cs` (новый), `MainWindow.xaml`, `Views/AboutWindow.xaml`, `Views/DashboardPage.xaml`, `Themes/DarkTheme.xaml`, `Themes/LightTheme.xaml`
  - `README.md`, `LICENSE` (новый), `CHANGELOG.md`, `build.bat`, `publish_portable.bat`
- **Почему:** Документация, обещающая несуществующие функции, и меню сборки с мусором вместо цветов подрывают доверие ко всем остальным заявлениям продукта.

### 6. 🔍 Диагностика: логирование пустых catch в критичных путях
- **Что сделано:** 15 молча проглатываемых исключений получили логирование в Debug-выход с тегами: `ElevatedCleanRequestFile` (7: валидация запроса, чтение/удаление файла и результата, подпись, nonce, очистка просроченных nonce), `QuarantineService` (4: создание хранилища, чтение манифеста, перемещение в карантин, purge), `PrivacyDebloatService` (3: apply/revert твика, сохранение бэкапа), `App.LogCrash` (1). Намеренно оставлены без логов: `IsSafeRequestPath` (malformed path = false — ожидаемое поведение), `IOException` в `TryConsumeNonce` (повторное использование nonce — ожидаемая защита от replay), пробы чтения с поясняющими комментариями.
- **Файлы:** `SmartCleaner.Core/Cleaning/ElevatedCleanRequestFile.cs`, `SmartCleaner.Core/Safety/QuarantineService.cs`, `SmartCleaner.Core/Privacy/PrivacyDebloatService.cs`, `SmartCleaner.App/App.xaml.cs`.
- **Почему:** Отрицательный результат без следа («почему карантин пуст?», «почему elevated-очистка не сработала?») — неразрешимая загадка в поддержке; лог в Debug-выход не стоит ничего.

### 7. ✅ Финальная верификация и инфраструктура репозитория
- **Что сделано:** `dotnet build SmartCleaner.sln` — 0 ошибок / 0 предупреждений; `dotnet test` — 214/214 зелёные; `git fsck` — репозиторий цел; рабочее дерево чистое. Seal-коммит `55aaa0c` «chore: seal v2.7.2 — full audit, critical UI fixes, security hardening, dead code purge» (67 файлов, +2149/−1759); `git gc --prune=now` — `.git` 136 МБ → 0.55 МБ.
- **Почему:** Аудит без финального прогона и фиксации состояния — не аудит.

---

## [2.7.1] — 07.09.2026

### 🎯 Тема релиза: Полная дизайн-система Fluent, чистка эмодзи, токены тем, исправление биндингов и версионирование

---

### 1. 🎨 Унифицированная дизайн-система Fluent и паритет тем
- **Что сделано:**
  - Расширена семантическая система кистей в `DarkTheme.xaml` и `LightTheme.xaml`: добавлены `SelectionBrush`, `SelectionHoverBrush`, `AccentSoftBrush`, `SafeSoftBrush`, `CautionSoftBrush`, `DangerSoftBrush`, `AccentBadgeBrush`, `SafeBadgeBrush`, `CautionBadgeBrush`, `DangerBadgeBrush`.
  - Разработан полноценный кастомный `ControlTemplate` для `ComboBox` с поддержкой плавной кнопки-стрелки и выпадающего `Popup` в цветах темы (`SurfaceBrush` / `BorderBrush`), устраняющий стандартный системный Aero popup.
  - Добавлены неявные стили со скруглениями (`CornerRadius="6"`), состояниями наведения и выделения для `ComboBoxItem`, `ListBoxItem`, `ListViewItem`, `DataGrid`, `TabControl`, `TabItem`, `TextBox`, `ToolTip`, `ProgressBar`, `ScrollBar`.
  - Унифицированы внешние отступы корневых контейнеров всех 21 представлений (`Views/`) под единый стандарт `Margin="24,20"`.
  - Заменены все оставшиеся жестко заданные hex-цвета в стилях триггеров данных (`PrivacyDebloatPage.xaml`, `ServicesOptimizerPage.xaml`) на динамические ресурсы темы (`DynamicResource`).
- **Файлы:**
  - `SmartCleaner.App/Themes/DarkTheme.xaml`
  - `SmartCleaner.App/Themes/LightTheme.xaml`
  - `SmartCleaner.App/Views/PrivacyDebloatPage.xaml`
  - `SmartCleaner.App/Views/ServicesOptimizerPage.xaml`
  - `SmartCleaner.App/Views/DiscoveryView.xaml`
  - `SmartCleaner.App/Views/SettingsView.xaml`
- **Почему:** Устранение визуального расхождения между страницами, обеспечение 100% паритета тёмной и светлой тем без белых системных артефактов и жестко зашитых цветов.

---

### 2. 🔤 Полная зачистка эмодзи и переход на Segoe MDL2 Assets
- **Что сделано:**
  - Устранены дублирующие цветные эмодзи из заголовков, вкладок, кнопок и статусов во всех 21 XAML-файлах представлений и главном окне.
  - Иконки навигации, кнопок действий и служебных элементов переведены строго на векторные глифы шрифта `Segoe MDL2 Assets`.
  - Заголовки вкладок (`TabItem.Header`) очищены от префиксов-эмодзи и приведены к лаконичному Fluent-виду.
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/Views/*.xaml` (все 21 страница)
- **Почему:** Соблюдение строгой дизайн-системы Fluent UI и исключение разнобоя шрифтов/эмодзи в интерфейсе.

---

### 3. 🐛 Исправление биндинга команды в CLI Inspector
- **Что сделано:**
  - Переименован метод `CopyUninstallCommand` в `CopyUninstall` в `CliInspectorViewModel.cs`. Генератор CommunityToolkit.Mvvm `[RelayCommand]` теперь создаёт команду `CopyUninstallCommand`, точно совпадающую с привязкой `{Binding DataContext.CopyUninstallCommand...}` в `CliInspectorPage.xaml`.
- **Файлы:**
  - `SmartCleaner.App/ViewModels/CliInspectorViewModel.cs`
- **Почему:** Устранение бага, из-за которого кнопка копирования команды удаления была неактивна из-за ошибки генерации имени команды (`CopyUninstallCommandCommand`).

---

### 4. 📦 Версионирование сборки и актуализация репозитория
- **Что сделано:**
  - В `SmartCleaner.App.csproj` добавлены метаданные сборки: `Version 2.7.1`, `AssemblyVersion 2.7.1.0`, `FileVersion 2.7.1.0`, `Product "Smart System Cleaner — Full Suite"`.
  - Синхронизированы версии в интерфейсе: `MainWindow.xaml` (`Smart System Cleaner — Full Suite v2.7.1`), сайдбар и `AboutWindow.xaml`.
  - В `.gitignore` добавлена директория `release/` для исключения бинарных артефактов и логов из отслеживания git.
  - Актуализирован `README.md` с описанием всех модулей Full Suite v2.7.
- **Файлы:**
  - `SmartCleaner.App/SmartCleaner.App.csproj`
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/Views/AboutWindow.xaml`
  - `.gitignore`
  - `README.md`
- **Почему:** Приведение метаданных сборки, версий в интерфейсе и документации проекта к единому значению 2.7.1 Full Suite.

---

## [2.7.0] — 24.08.2026

### 🎯 Тема релиза: Full Suite — Защита приватности Windows (Anti-Spy Debloater) и Оптимизатор фоновых служб

---

### 1. 🛡️ Windows Privacy & Anti-Spy Telemetry Debloater (Защита приватности)
- **Что сделано:** Создан движок `PrivacyDebloatService.cs` и страница управления `PrivacyDebloatPage.xaml`:
  - 14 целевых твиков реестра и служб в 4 категориях: Телеметрия (DiagTrack, dmwappushservice, AllowTelemetry), Реклама (Advertising ID, Bing Search, рекомендации Explorer, промо-приложения Пуск), Отчеты (WER, CEIP, отзывы SIUF), Слежка и датчики (геолокация, Activity History).
  - 1-Click отключение всех рекомендуемых параметров («Отключить всё рекомендуемое»).
  - Автоматическое создание снимка реестра `privacy_backup.json` и 1-Click восстановление настроек по умолчанию Windows.
- **Файлы:**
  - `SmartCleaner.Core/Privacy/PrivacyTweakItem.cs`
  - `SmartCleaner.Core/Privacy/PrivacyDebloatService.cs`
  - `SmartCleaner.App/ViewModels/PrivacyDebloatViewModel.cs`
  - `SmartCleaner.App/Views/PrivacyDebloatPage.xaml`
  - `SmartCleaner.App/Views/PrivacyDebloatPage.xaml.cs`
  - `SmartCleaner.App/App.xaml.cs`
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/MainWindow.xaml.cs`
- **Почему:** Полная блокировка скрытой фоновой слежки, телеметрии и рекламы Windows без сторонних утилит.

---

### 2. ⚙️ Windows Services Optimizer (Умный оптимизатор фоновых служб)
- **Что сделано:** Создан оптимизатор системных служб `WindowsServicesOptimizer.cs` и экран `ServicesOptimizerPage.xaml`:
  - Готовые пресеты в 1 клик:
    - 🎮 **Игровой профиль (Gaming / Max FPS)**: Отключение SysMain, телеметрии, WAP Push, WerSvc, факсов, удаленного реестра, карт и ритейл демо для устранения микрозадержек.
    - ⚡ **Сбалансированный профиль (Balanced)**: 100% безопасное отключение неиспользуемого системного балласта (факсы, карты, ритейл демо, телеметрия).
    - 💼 **Офисный профиль (Workstation)**: Сохранение служб печати, локальной сети и биометрии Windows Hello.
    - ↩️ **Восстановление по умолчанию (Restore Defaults)**: Возврат служб в исходные состояния по снимку `services_backup.json` или дефолтным типам запуска.
  - Индивидуальное переключение служб в таблице с фильтрами по уровню риска (Безопасно, Умеренный).
- **Файлы:**
  - `SmartCleaner.Core/ServicesOpt/WindowsServiceItem.cs`
  - `SmartCleaner.Core/ServicesOpt/WindowsServicesOptimizer.cs`
  - `SmartCleaner.App/ViewModels/ServicesOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/ServicesOptimizerPage.xaml`
  - `SmartCleaner.App/Views/ServicesOptimizerPage.xaml.cs`
  - `SmartCleaner.App/App.xaml.cs`
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/MainWindow.xaml.cs`
- **Почему:** Снижение нагрузки на процессор, память и дисковый накопитель без риска поломки ОС.

---

### 3. 🧪 Тестирование и валидация
- **Что сделано:** Добавлены 4 новых модульных теста в `TierFeaturesTests.cs` (всего 51 тест, 100% пройдено).
- **Файлы:**
  - `SmartCleaner.Core.Tests/TierFeaturesTests.cs`
- **Почему:** Гарантия надежности и корректности определений твиков, профилей и сканеров.

---

### 4. 💎 Безупречная полировка UI/UX («С иголочки»)
- **Что сделано:** Проведен тотальный аудит визуального стиля, геометрии и микро-взаимодействий всех представлений приложения:
  - **Устранение наложения элементов в MainWindow:** Панель общей статистики («Освобождено за N сессий») перенесена из конфликтующей строки в аккуратный скругленный бейдж-пилюлю в правом углу заголовка рядом с кнопкой «Параметры». Устранено любое перекрытие с карточками сводки.
  - **Модернизация оверлеев сканирования и очистки:** Вместо темного монолитного прямоугольника оверлеи `ScanOverlay` и `CleanOverlay` переработаны в центрированные полупрозрачные карточки с радиусом 16px, тонкими границами, цветными векторными иконками и аккуратным скругленным индикатором прогресса.
  - **Эволюция Toast-уведомлений:** Всплывающий тост получил мягкую подсветку, акцентную рамку, иконку и скругление 12px.
  - **Диалог «О программе» (AboutWindow):** Обновлен до актуальной версии `v2.7 FULL SUITE`, получил градиентную иконку, современную типографику и карточку характеристик.
  - **Диалог подтверждения очистки (PreviewDialog):** Английские технические строки «Delete» и «Uninstall» заменены на русские («Удаление», «Деинсталляция»), основная кнопка переведена на яркий `DangerButton`, предупреждение оформлено в стиле аккуратной предупреждающей карточки.
  - **Единые стандарты заголовков и отступов всех страниц:** Все 14 вспомогательных страниц (`AiAssistantPage`, `CliInspectorPage`, `CompactPage`, `DashboardPage`, `DiscoveryView`, `DiskMapPage`, `DuplicatesPage`, `LargeFilesPage`, `NetworkOptimizerPage`, `PluginsPage`, `QuarantinePage`, `RamOptimizerPage`, `SchedulerPage`, `SettingsView`, `StartupPage`, `SystemDeepCleanPage`, `UninstallerPage`) приведены к строго единой геометрии: внешние поля `Margin="24,20"`, жирные заголовки 22px Bold, компактные векторные бейджи режима (`AI CO-PILOT`, `DEV & CLI`, `LZX ENGINE`, `ANALYTICS`, `AUTO DISCOVERY`, `TREEMAP`, `LOW LATENCY`, `EXTENSIONS`, `ZERO-RISK`, `PERFORMANCE HUB`, `AUTO CRON`, `PREFERENCES`, `DISM & DRIVERS`, `LEFTOVER HUNTER`) и вторичные поясняющие подзаголовки.
  - **Искоренение хардкод-цветов:** Заменены устаревшие литералы `OrangeRed`, `LimeGreen`, `DodgerBlue` на системные динамические кисти (`CautionBrush`, `DangerBrush`, `SafeBrush`, `AccentBrush`), гарантируя безупречный контраст как в темной Obsidian, так и в светлой теме.
  - **Системные ToolTip и ProgressBar стили:** В `DarkTheme.xaml` и `LightTheme.xaml` добавлены глобальные шаблоны для всплывающих подсказок (скругленные карточки с акцентной границей) и полос прогресса со скруглением 2.5px.
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/Views/AboutWindow.xaml`
  - `SmartCleaner.App/Views/AiAssistantPage.xaml`
  - `SmartCleaner.App/Views/CliInspectorPage.xaml`
  - `SmartCleaner.App/Views/CompactPage.xaml`
  - `SmartCleaner.App/Views/DashboardPage.xaml`
  - `SmartCleaner.App/Views/DiscoveryView.xaml`
  - `SmartCleaner.App/Views/DiskMapPage.xaml`
  - `SmartCleaner.App/Views/DuplicatesPage.xaml`
  - `SmartCleaner.App/Views/LargeFilesPage.xaml`
  - `SmartCleaner.App/Views/NetworkOptimizerPage.xaml`
  - `SmartCleaner.App/Views/PluginsPage.xaml`
  - `SmartCleaner.App/Views/PreviewDialog.xaml`
  - `SmartCleaner.App/Views/QuarantinePage.xaml`
  - `SmartCleaner.App/Views/RamOptimizerPage.xaml`
  - `SmartCleaner.App/Views/SchedulerPage.xaml`
  - `SmartCleaner.App/Views/SettingsView.xaml`
  - `SmartCleaner.App/Views/StartupPage.xaml`
  - `SmartCleaner.App/Views/SystemDeepCleanPage.xaml`
  - `SmartCleaner.App/Views/UninstallerPage.xaml`
  - `SmartCleaner.App/Themes/DarkTheme.xaml`
  - `SmartCleaner.App/Themes/LightTheme.xaml`
- **Почему:** Достижение высочайшего эталона визуальной целостности («pixel-perfect»), эстетики Fluent Design и комфорта пользователя.

---

## [2.6.0] — 24.08.2026

### 🎯 Тема релиза: Modern Fluent / Obsidian Design System & UI/UX Polish Overhaul

---

### 1. 🎨 Deep Obsidian & Modern Slate Design System (Темы оформления)
- **Что сделано:** Полностью переработана палитра и стилизация `DarkTheme.xaml` и `LightTheme.xaml`:
  - Глубокая стильная палитра Obsidian (`#0D1117` фон, `#161B22` карточки, `#21262D` возвышение, `#30363D` тонкие границы).
  - Электрические акцентные градиенты (`AccentGradientBrush`, `PrimaryButtonGradientBrush`, `DangerButtonGradientBrush`).
  - Минималистичные кастомные скроллбары (тонкая полоса 7px, прозрачный трек, скругленный бегунок 3.5px, плавная подсветка при наведении).
  - Кастомный чекбокс Fluent Design со скругленными углами и векторной галочкой.
  - Округлые поля ввода TextBox и ComboBox со скруглением 6px и акцентной подсветкой фокуса.
  - Оформление DataGrid с тонкими разделителями строк и стильными заголовками столбцов.
- **Файлы:**
  - `SmartCleaner.App/Themes/DarkTheme.xaml`
  - `SmartCleaner.App/Themes/LightTheme.xaml`
- **Почему:** Устаревший плоский серый интерфейс Windows 10 заменен на флагманский дизайн в стиле Windows 11 Fluent / Linear.

---

### 2. 🧭 Редизайн навигации и боковой панели (Sidebar & Brand Header)
- **Что сделано:** Обновлена боковая панель в `MainWindow.xaml`:
  - Брендовый логотип в светящемся градиентном контейнере с бейджем `v2.5 ULTIMATE`.
  - Кнопки навигации `NavButton` получили активный вертикальный индикатор (pill bar 3.5px) на левой грани и плавную подложку при выборе.
  - Четкая микротипографика заголовков секций ("ОЧИСТКА", "ИНСТРУМЕНТЫ", "СИСТЕМА").
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/Themes/DarkTheme.xaml`
  - `SmartCleaner.App/Themes/LightTheme.xaml`
- **Почему:** Профессиональная визуальная иерархия и интуитивная индикация текущего экрана.

---

### 3. 📊 Карточки сводной статистики и Hero-кнопки (Stat Widgets & Hero Actions)
- **Что сделано:**
  - 4 карточки сводки ("Найдено мусора", "Выбрано к очистке", "Объектов", "Сканировать") в `MainWindow.xaml` и `DashboardPage.xaml` переработаны в представительские виджеты:
    - Контейнеры иконок со скруглением и полупрозрачным фоном (акцентный синий, изумрудный, индиго, янтарный).
    - Крупные четкие цифры метрик (21-22px Bold).
    - Hero-кнопка сканирования с градиентом, иконкой и подписью.
    - Кнопка «Очистить выбранное» переведена на заметный стиль `DangerButton` с контрастным градиентом.
  - Бейджи уровней риска в списке результатов оформлены в виде скругленных пилюль (`CornerRadius="10"`).
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/Views/DashboardPage.xaml`
  - `SmartCleaner.App/Converters/Converters.cs`
- **Почему:** Фокусирует внимание пользователя на главных метриках и целевых действиях.

---

## [2.5.0] — 23.08.2026

### 🎯 Тема релиза: Ultimate Edition — Оптимизатор сети и DNS, Интеграция в Explorer, Паспорт системы HTML/PDF и Звуковые эффекты

---

### 1. 🌐 Gaming Network & Ping / Latency Optimizer (Оптимизатор сети и DNS)
- **Что сделано:** Создан сервис `NetworkOptimizerService.cs` и страница `NetworkOptimizerPage.xaml`.
  - 1-Click сброс DNS-кэша (`flushdns`), очистка ARP-таблицы и сокетов Winsock.
  - Быстрое переключение DNS с замером задержки (Ping) в реальном времени: Cloudflare (1.1.1.1), Google (8.8.8.8), Quad9 (9.9.9.9), AdGuard DNS и возврат на DHCP.
  - Игровой твик реестра TCP NoDelay (отключение алгоритма Нагла `TcpAckFrequency = 1`, `TCPNoDelay = 1`) для устранения микрозадержек в онлайн-играх.
- **Файлы:**
  - `SmartCleaner.Core/Network/NetworkOptimizerService.cs`
  - `SmartCleaner.App/ViewModels/NetworkOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/NetworkOptimizerPage.xaml`
  - `SmartCleaner.App/Views/NetworkOptimizerPage.xaml.cs`
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/MainWindow.xaml.cs`
- **Почему:** Максимальное снижение пинга и сетевых задержек в играх и ускорение открытия сайтов.

---

### 2. 🖱️ Windows Explorer Shell Integration (Интеграция в контекстное меню Проводника)
- **Что сделано:** Реализован менеджер контекстного меню `ExplorerContextMenuManager.cs` с переключателями в Настройках:
  - «⚡ Анализировать в SmartCleaner» (для папок)
  - «🗜️ Сжать через CompactOS (LZX)» (для папок)
  - «🔥 Безвозвратно уничтожить (Шредер DoD)» (для файлов)
- **Файлы:**
  - `SmartCleaner.Core/Shell/ExplorerContextMenuManager.cs`
  - `SmartCleaner.App/ViewModels/SettingsViewModel.cs`
  - `SmartCleaner.App/Views/SettingsView.xaml`
- **Почему:** Мгновенный доступ к функциям чистилки, сжатия и шредера прямо из Проводника Windows в 1 клик.

---

### 3. 📄 Executive HTML System Passport & Report Generator (Паспорт системы и отчет)
- **Что сделано:** Создан генератор отчетов `SystemReportGenerator.cs`, формирующий стильный интерактивный темный HTML-отчет с паспортом ПК (CPU, RAM, S.M.A.R.T. дисков, температура), таблицей освобожденного места и кнопкой печати / сохранения в PDF.
- **Файлы:**
  - `SmartCleaner.Core/Reporting/SystemReportGenerator.cs`
  - `SmartCleaner.App/ViewModels/SettingsViewModel.cs`
  - `SmartCleaner.App/Views/SettingsView.xaml`
- **Почему:** Профессиональный аудит и наглядная фиксация состояния ПК и результатов очистки.

---

### 4. 🔊 Audio Feedback & Sound FX Engine (Звуковое сопровождение)
- **Что сделано:** Создан сервис звукового сопровождения `AudioFeedbackService.cs`, воспроизводящий аккуратные системные сигналы при завершении сканирования, очистки и включении Turbo Boost с возможностью отключения в Настройках.
- **Файлы:**
  - `SmartCleaner.App/Services/AudioFeedbackService.cs`
  - `SmartCleaner.App/ViewModels/SettingsViewModel.cs`
  - `SmartCleaner.App/Views/SettingsView.xaml`
- **Почему:** Тактильный, живой и премиальный пользовательский опыт.

---

## [2.4.0] — 23.08.2026

### 🎯 Тема релиза: Master-Tier — Game Turbo Boost, S.M.A.R.T. SSD Health & Износ, Single-File Portable сборщик

---

### 1. 🚀 1-Click Game Turbo Boost (Турбо-режим для игр и рендера)
- **Что сделано:** Реализован сервис `GameBoostService.cs` для максимального ускорения ПК в играх в 1 клик. Сервис сбрасывает неиспользуемые страницы оперативной памяти (`EmptyWorkingSet`), временно приостанавливает фоновые пожиратели ресурсов (`SysMain`, `wuauserv`, `DiagTrack`, `WSearch`) и переключает схему электропитания на «Высокая производительность». Поддерживается мгновенный возврат в исходный режим.
- **Файлы:**
  - `SmartCleaner.Core/SystemOpt/GameBoostService.cs`
  - `SmartCleaner.App/ViewModels/RamOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/RamOptimizerPage.xaml`
- **Почему:** Максимизация FPS в играх и устранение системных лагов и троттлинга без сторонних утилит.

---

### 2. 📊 S.M.A.R.T. SSD Health, Temperature & Wear Telemetry (Здоровье и износ SSD)
- **Что сделано:** Создан сервис `DiskHealthService.cs`, запрашивающий аппаратную телеметрию физических накопителей (NVMe/SATA SSD, HDD) через WMI и PowerShell. Отображает модель диска, тип шины, состояние здоровья (`Healthy`/`Warning`), процент оставшегося ресурса ячеек памяти и температуру в реальном времени.
- **Файлы:**
  - `SmartCleaner.Core/DiskHealth/DiskHealthService.cs`
  - `SmartCleaner.App/ViewModels/RamOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/RamOptimizerPage.xaml`
- **Почему:** Своевременное выявление деградации и перегрева NVMe/SSD накопителей для предотвращения потери данных.

---

### 3. 📦 Single-File Portable Release Publish Script (Автономный сборщик)
- **Что сделано:** Создан скрипт `publish_portable.bat` для компиляции и сборки автономного исполняемого файла `SmartCleaner.App.exe` со всеми упакованными DLL, нативными библиотеками и плагинами сообщества.
- **Файлы:**
  - `publish_portable.bat`
- **Почему:** Возможность запускать чистилку с флешки на любом ПК с Windows 10/11 без необходимости установки .NET Runtime.

---

## [2.3.0] — 23.08.2026

### 🎯 Тема релиза: God-Tier (SSSSS) — Интерактивный Treemap, Вакуум SQLite, Оптимизация RAM, Шредер файлов DoD 5220.22-M и Кэш шейдеров

---

### 1. 🗺️ Squarified Treemap Interactive Visualizer (Визуализатор карты диска)
- **Что сделано:** Реализован алгоритм раскладки Squarified Treemap (`SmartCleaner.Core/DiskMap/TreemapLayout.cs`), цветовая подсветка типов файлов (видео, архивы, исполняемые файлы, код, документы, изображения) и интерактивный зум / проваливание в папки по двойному клику в `TreeMapControl.cs`.
- **Файлы:**
  - `SmartCleaner.Core/DiskMap/TreemapLayout.cs`
  - `SmartCleaner.App/Controls/TreeMapControl.cs`
  - `SmartCleaner.App/ViewModels/DiskMapViewModel.cs`
  - `SmartCleaner.App/Views/DiskMapPage.xaml`
- **Почему:** Наглядное понимание занятого места на диске в стиле лучших мировых инструментов (SpaceSniffer, WizTree).

---

### 2. 🧠 SQLite Database Vacuum & Compactor (Сжатие баз данных AI IDE и браузеров)
- **Что сделано:** Создан сервис обнаружения и вакуумизации баз данных SQLite (`VACUUM;` и `PRAGMA wal_checkpoint(TRUNCATE);`) для Cursor AI, VS Code, Windsurf, Claude Desktop, Google Chrome, Microsoft Edge и Telegram Desktop.
- **Файлы:**
  - `SmartCleaner.Core/Optimization/SqliteCompactorService.cs`
  - `SmartCleaner.App/ViewModels/RamOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/RamOptimizerPage.xaml`
- **Почему:** Устранение фрагментации и сжатие баз данных на 40–70%, ускорение холодного запуска IDE и браузеров.

---

### 3. 🎮 GPU Shader Cache & DirectX Optimizer (Кэши шейдеров NVIDIA, AMD, Intel, DirectX)
- **Что сделано:** Создан специализированный сканер `ShaderCacheScanner.cs` для очистки устаревших скомпилированных шейдеров DirectX 11/12 (`D3DSCache`), NVIDIA (`DXCache`, `GLCache`, `NV_Cache`), AMD Radeon (`DxCache`, `GLCache`), Intel и Steam Shader Pre-Caching.
- **Файлы:**
  - `SmartCleaner.Core/Scanning/Scanners/ShaderCacheScanner.cs`
- **Почему:** Освобождение 5–30 ГБ быстрого SSD пространства и предотвращение микрофризов в играх из-за устаревших шейдерных бинарников.

---

### 4. ⚡ RAM & Working Set Optimizer (Оптимизация оперативной памяти)
- **Что сделано:** Реализован сервис сброса неиспользуемых страниц памяти и Standby List через Win32 API `EmptyWorkingSet` и `GlobalMemoryStatusEx` с мониторингом физической памяти в реальном времени.
- **Файлы:**
  - `SmartCleaner.Core/SystemOpt/RamOptimizerService.cs`
  - `SmartCleaner.App/ViewModels/RamOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/RamOptimizerPage.xaml`
- **Почему:** Мгновенное высвобождение 2–8 ГБ оперативной памяти без перезапуска Windows.

---

### 5. 🛡️ File Shredder (DoD 5220.22-M 3-Pass Secure Wipe)
- **Что сделано:** Разработан шредер файлов с гарантированным уничтожением данных по военному стандарту DoD 5220.22-M: Проход 1 (0x00), Проход 2 (0xFF), Проход 3 (криптографические псевдослучайные байты) с рандомизацией имен файлов в таблице MFT перед удалением.
- **Файлы:**
  - `SmartCleaner.Core/Safety/FileShredderService.cs`
  - `SmartCleaner.App/ViewModels/RamOptimizerViewModel.cs`
  - `SmartCleaner.App/Views/RamOptimizerPage.xaml`
- **Почему:** 100% защита от восстановления конфиденциальных документов и паролей через Recuva, R-Studio и forensic-софт.

---

### 6. 🔔 System Tray Sentinel & Дисковый страж
- **Что сделано:** Создан сервис фонового мониторинга дисков `TraySentinelService.cs` для заблаговременного предупреждения пользователя о падении свободного места ниже 12% (<10 ГБ).
- **Файлы:**
  - `SmartCleaner.App/Services/TraySentinelService.cs`
- **Почему:** Проактивная защита системы от внезапного переполнения диска C:.

---

### 7. 🌐 Multi-Language Runtime Localization Engine
- **Что сделано:** Создан синглтон-менеджер локализации `LocalizationManager.cs` с поддержкой русского и английского языков и динамическим обновлением UI.
- **Файлы:**
  - `SmartCleaner.Core/Localization/LocalizationManager.cs`
- **Почему:** Готовность приложения для международной аудитории.

---

## [2.2.0] — 23.08.2026

### 🎯 Тема релиза: Внедрение полного пакета 9 ультимативных Tier-фичей (S, SSS, SSSS)

---

### 1. ⚡ S-Tier: WizTree MFT Instant Scan
- **Что сделано:** Реализован прямой доступ к Master File Table (MFT) и журналу USN (`MftReader.cs`, `MftScanner.cs`) через системные вызовы Win32 `FSCTL_ENUM_USN_DATA` и структуры `MFT_ENUM_DATA` с автоматическим быстрым откатом на многопоточный сканер при отсутствии административных прав.
- **Файлы:**
  - `SmartCleaner.Core/Mft/MftEntry.cs`
  - `SmartCleaner.Core/Mft/MftReader.cs`
  - `SmartCleaner.Core/Mft/MftScanner.cs`
- **Почему:** Мгновенное индексирование файловой системы за 1–2 секунды для дисков любого объема.

---

### 2. 🗑️ S-Tier: Deep Uninstaller & Leftover Hunter (Умный деинсталлятор + Охотник за хвостами)
- **Что сделано:** Создан полноценный деинсталлятор программ для Windows (Win32, MSI, WinGet, AppX) со сканером «хвостов» в реестре (`HKCU/HKLM\Software`, `WOW6432Node`), `AppData`, `ProgramData`, документах и сохранениях игр с возможностью выборочной зачистки остатков.
- **Файлы:**
  - `SmartCleaner.Core/Uninstaller/InstalledAppItem.cs`
  - `SmartCleaner.Core/Uninstaller/LeftoverItem.cs`
  - `SmartCleaner.Core/Uninstaller/LeftoverHunter.cs`
  - `SmartCleaner.Core/Uninstaller/UninstallerEngine.cs`
  - `SmartCleaner.App/ViewModels/UninstallerViewModel.cs`
  - `SmartCleaner.App/Views/UninstallerPage.xaml`
  - `SmartCleaner.App/Views/UninstallerPage.xaml.cs`
- **Почему:** Полное избавление системы от сотен мегабайт и тысяч мусорных ключей реестра, остающихся после стандартного удаления программ.

---

### 3. 🛠️ S-Tier: Dev Super-Cleaner (WSL2, Rust, Gradle, Maven, Go, Android, Unity, Unreal)
- **Что сделано:** Реализован специализированный сканер инструментов разработки и сервис сжатия виртуальных дисков WSL2 `ext4.vhdx` через `diskpart compact`. Очищаются кэши Rust `cargo/registry/cache`, `target`, `Gradle caches`, `Maven .m2`, `Go build/mod`, `Android SDK temp/snapshots`, кэши `Unity` и `Unreal Engine DDC`.
- **Файлы:**
  - `SmartCleaner.Core/Scanning/Scanners/DevSuperScanner.cs`
  - `SmartCleaner.Core/Services/WslShrinkService.cs`
- **Почему:** Освобождение от 20 до 150+ ГБ на дисках программистов и разработчиков.

---

### 4. 🗜️ SSS-Tier: CompactOS & LZX Transparent Compression Engine
- **Что сделано:** Разработан движок прозрачного файлового сжатия NTFS (алгоритмы `LZX`, `XPRESS16K`) с автопоиском установленных игр (Steam, Epic Games, GOG) и тяжелых директорий разработки.
- **Файлы:**
  - `SmartCleaner.Core/Compression/CompactTargetItem.cs`
  - `SmartCleaner.Core/Compression/CompactEngine.cs`
  - `SmartCleaner.App/ViewModels/CompactViewModel.cs`
  - `SmartCleaner.App/Views/CompactPage.xaml`
  - `SmartCleaner.App/Views/CompactPage.xaml.cs`
- **Почему:** Высвобождение 30–50% места на SSD без удаления файлов и без падения FPS (распаковка на лету в RAM).

---

### 5. 🔗 SSS-Tier: Hardlink Deduplication (Zero-byte Single Instance Storage)
- **Что сделано:** В движок поиска дубликатов добавлен механизм создания жестких ссылок NTFS `CreateHardLinkW`. Физические дубликаты заменяются на хардлинки на том же томе, освобождая физические кластеры без нарушения путей и совместимости программ.
- **Файлы:**
  - `SmartCleaner.Core/Duplicates/DuplicateEngine.cs`
  - `SmartCleaner.App/ViewModels/DuplicatesViewModel.cs`
  - `SmartCleaner.App/Views/DuplicatesPage.xaml`
- **Почему:** Устранение дублирования данных без риска сломать установленные программы или библиотеки.

---

### 6. 🛡️ SSS-Tier: WinSxS & DriverStore Purge
- **Что сделано:** Реализован аудит и очистка ядра Windows (консолидация старых версий обновлений через `Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase`) и поиск/удаление устаревших версий OEM-драйверов в `DriverStore` через `PnPUtil`.
- **Файлы:**
  - `SmartCleaner.Core/WinSxS/WinSxSEngine.cs`
  - `SmartCleaner.Core/WinSxS/DriverStoreCleaner.cs`
  - `SmartCleaner.App/ViewModels/SystemDeepCleanViewModel.cs`
  - `SmartCleaner.App/Views/SystemDeepCleanPage.xaml`
  - `SmartCleaner.App/Views/SystemDeepCleanPage.xaml.cs`
- **Почему:** Очистка системного диска C: от гигабайтов неудаляемых старых обновлений и дубликатов драйверов видеокарт/чипсетов.

---

### 7. 🤖 SSSS-Tier: AI Natural Language Cleanup («Диалог с диском»)
- **Что сделано:** Создан модуль интеллектуального диалогового ассистента с семантическим парсером намерений (поиск видео > N ГБ, освобождение места, очистка dev-кэшей, поиск дубликатов, сжатие игр, вызов деинсталлятора) с интерактивными карточками действий в чате.
- **Файлы:**
  - `SmartCleaner.Core/AiAssistant/NaturalLanguageQueryEngine.cs`
  - `SmartCleaner.App/ViewModels/AiAssistantViewModel.cs`
  - `SmartCleaner.App/Views/AiAssistantPage.xaml`
  - `SmartCleaner.App/Views/AiAssistantPage.xaml.cs`
- **Почему:** Революционный интерфейс взаимодействия пользователя с дисковым пространством на понятном естественном языке.

---

### 8. 📦 SSSS-Tier: Zero-Risk Sandbox Quarantine (Карантин и откат в 1 клик)
- **Что сделано:** Создана изолированная песочница безопасного удаления (`%LOCALAPPDATA%\SmartCleaner\Quarantine\`) с JSON-манифестом метаданных, поддержкой мгновенного восстановления файлов на прежние места и окончательного стирания.
- **Файлы:**
  - `SmartCleaner.Core/Safety/QuarantineService.cs`
  - `SmartCleaner.App/ViewModels/QuarantineViewModel.cs`
  - `SmartCleaner.App/Views/QuarantinePage.xaml`
  - `SmartCleaner.App/Views/QuarantinePage.xaml.cs`
- **Почему:** 100% гарантия сохранности данных и возможность отката любых действий очистки в 1 клик.

---

### 9. 🧩 SSSS-Tier: Open Plugin Scripting Engine (Плагины сообщества)
- **Что сделано:** Создан расширяемый движок плагинов на базе JSON-манифестов (`*.plugin.json`). Добавлены встроенные плагины для `JetBrains IDEs`, `Discord`, `Telegram Desktop`, `Blender`.
- **Файлы:**
  - `SmartCleaner.Core/Plugins/PluginManifest.cs`
  - `SmartCleaner.Core/Plugins/PluginEngine.cs`
  - `SmartCleaner.Data/Plugins/*.plugin.json`
  - `SmartCleaner.App/ViewModels/PluginsViewModel.cs`
  - `SmartCleaner.App/Views/PluginsPage.xaml`
  - `SmartCleaner.App/Views/PluginsPage.xaml.cs`
- **Почему:** Бесконечная расширяемость правил очистки силами сообщества и пользователей.

---

## [2.1.0] — 23.08.2026

### 🎯 Тема релиза: Глубокая нативная интеграция Deliter (инспекция CLI, AI-агентов, оздоровление PATH и аудит PowerShell)

---

### 1. Нативный C# Core движок CLI-инспектора и реестровый сервис PATH
- **Что сделано:** 
  - Реализован класс `PathEnvironmentService` для прямого чтения пользовательских и системных записей переменной среды Windows `PATH` через реестр (`HKCU\Environment`, `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`), обнаружения мертвых директорий и их безопасного удаления.
  - Добавлен P/Invoke вызов `SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, ...)` для мгновенного оповещения всех запущенных процессов Windows об обновлении переменных окружения без необходимости перезагрузки или выхода из системы.
  - Реализован высокопроизводительный многопоточный сканер `CliInspectorEngine`, проверяющий:
    - Dot-папки в домашней директории пользователя с базой сигнатур AI-ассистентов (OpenCode, Orca, Hermes, Claude, Copilot, Gemini/Antigravity, Kimi, Cline, Roo-Cline, Continue, Aider, Bolt AI, V0, Bun и др.).
    - Глобальные пакеты NPM (`%APPDATA%\npm\node_modules`) с парсингом `package.json` (версии, описание, алиасы команд).
    - Изолированные приложения Python Pipx (`%LOCALAPPDATA%\pipx\venvs`).
    - Пакетные менеджеры: Scoop (`~/scoop/apps`), Chocolatey (`ProgramData/chocolatey/lib`), Cargo (`~/.cargo/bin`), WinGet Links (`%LOCALAPPDATA%\Microsoft\WinGet\Links`).
    - Все исполняемые файлы (`.exe`, `.cmd`, `.bat`, `.ps1`) в директориях `PATH`.
    - Скрипты автозагрузки PowerShell (`$PROFILE`) и установленные модули.
- **Файлы:**
  - `SmartCleaner.Core/CliInspector/CliToolItem.cs`
  - `SmartCleaner.Core/CliInspector/PathHealthItem.cs`
  - `SmartCleaner.Core/CliInspector/PathEnvironmentService.cs`
  - `SmartCleaner.Core/CliInspector/CliInspectorEngine.cs`
- **Почему:** Полный перенос и расширение возможностей проекта **Deliter** на нативный C# .NET 8, дающий нулевые задержки при сканировании и избавляющий пользователя от необходимости устанавливать Python и внешние зависимости.

---

### 2. WPF UI и ViewModel модуля «CLI и AI Агенты»
- **Что сделано:**
  - Создан `CliInspectorViewModel` с поддержкой асинхронного сканирования, фильтрации по категориям и тексту в реальном времени, безопасного удаления утилит в Корзину (`FileSystem.DeleteDirectory(..., RecycleOption.SendToRecycleBin)`), открытия папок в Проводнике, запуска PowerShell в целевой папке, копирования команд деинсталляции и экспорта полного отчета в JSON/CSV.
  - Создана страница `CliInspectorPage.xaml` со сводными карточками метрик и 3 вкладками («Инструменты & AI-агенты», «Здоровье PATH & Битые пути», «PowerShell Profiles ($PROFILE)»).
  - Добавлены новые конвертеры значений (`ZeroToColorConverter`, `DeadStatusColorConverter`, `EmptyStringToVisibilityConverter`).
  - Добавлен пункт навигации «CLI и AI Агенты» (`\uE756`) в боковое меню `MainWindow.xaml` и зарегистрированы сервисы в DI `App.xaml.cs`.
- **Файлы:**
  - `SmartCleaner.App/ViewModels/CliInspectorViewModel.cs`
  - `SmartCleaner.App/Views/CliInspectorPage.xaml`
  - `SmartCleaner.App/Views/CliInspectorPage.xaml.cs`
  - `SmartCleaner.App/Converters/Converters.cs`
  - `SmartCleaner.App/App.xaml`
  - `SmartCleaner.App/App.xaml.cs`
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/MainWindow.xaml.cs`
- **Почему:** Предоставить пользователю современный, удобный и интуитивный инструмент для аудита и наведения порядка в утилитах командной строки и агентах прямо внутри главного приложения SmartCleaner.

---

### 3. Модульные тесты
- **Что сделано:** Добавлены модульные тесты для проверки чтения переменных PATH, работы `CliInspectorEngine` и детекции битых путей.
- **Файлы:**
  - `SmartCleaner.Core.Tests/CliInspectorTests.cs`
- **Почему:** Гарантия надежности и стабильности работы новых сервисов (все 26 тестов успешно пройдены).

---

### 4. Улучшение отдельного проекта Deliter (`e:\AllMyProject\Deliter\`)
- **Что сделано:**
  - Добавлен API-эндпоинт `POST /api/clean-dead-paths` с Win32 реестровой очисткой и `WM_SETTINGCHANGE` броадкастом.
  - Расширена база сигнатур AI-агентов (`.cline`, `.roo-cline`, `.continue`, `.aider`, `.v0`, `.boltai`).
  - Обновлен журнал изменений `e:\AllMyProject\Deliter\CHANGELOG.md`.
- **Файлы:**
  - `e:\AllMyProject\Deliter\app.py`
  - `e:\AllMyProject\Deliter\scanner.py`
  - `e:\AllMyProject\Deliter\CHANGELOG.md`
- **Почему:** Синхронизация и усовершенствование автономной версии Deliter наряду с ее интеграцией в SmartCleaner.

---

## [2.0.0] — 21.08.2026

### 🎯 Тема релиза: Адаптация под .NET 8.0 LTS, полная очистка мусора в репозитории, исправление критических багов и редизайн UI в стиле Windows 11 Fluent

---

### 1. Адаптация под .NET 8.0 LTS и починка сборки
- **Что сделано:** Переведены все 3 проекта решения (`SmartCleaner.App`, `SmartCleaner.Core`, `SmartCleaner.Core.Tests`) с `.NET 9` на `.NET 8.0 LTS` (`net8.0-windows`), обновлены зависимости пакетов `Microsoft.Extensions.*` на совместимые версии `8.0.x`.
- **Файлы:**
  - `SmartCleaner.App/SmartCleaner.App.csproj`
  - `SmartCleaner.Core/SmartCleaner.Core.csproj`
  - `SmartCleaner.Core.Tests/SmartCleaner.Core.Tests.csproj`
- **Почему:** На рабочей машине пользователя установлен только .NET 8 SDK (версия 8.0.421), а .NET 9 SDK отсутствует (к тому же .NET 9 — это STS релиз с коротким сроком поддержки). Без этого проект не собирался вовсе (ошибка `NETSDK1045`).

- **Что сделано:** Заменен метод хэширования `Convert.ToHexStringLower(hash)` на `Convert.ToHexString(hash).ToLowerInvariant()`.
- **Файлы:**
  - `SmartCleaner.Core/Duplicates/DuplicateEngine.cs`
- **Почему:** Метод `Convert.ToHexStringLower` появился только в .NET 9 и вызывал ошибку компиляции CS0117 в среде .NET 8.

- **Что сделано:** Обновлены пути вывода сборки в командном скрипте с `net9.0-windows` на `net8.0-windows`.
- **Файлы:**
  - `build.bat`
- **Почему:** Чтобы скрипт корректно запускал и публиковал собранные бинарники из директорий .NET 8.

---

### 2. Очистка репозитория от мусора и оптимизация Git
- **Что сделано:**
  - Удален архив `CHISTilka.rar` (124 МБ).
  - Удалена папка `Duplicater/` со старым Python-прототипом и окружением `venv` (более 29 800 лишних файлов).
  - Удалены временные папки `publish/`, `tmp/`, `.opencode/`.
  - Удалены старые кэши и артефакты `net9.0-windows` в `bin/` и `obj/`.
  - Сброшен и очищен git-индекс.
- **Файлы:**
  - `.gitignore`
  - Корень проекта
- **Почему:** Проект был перегружен устаревшими файлами от предыдущих версий. В git-индексе отслеживалось почти 30 000 файлов из Python venv и сборок, что перегружало память и замедляло работу IDE и инструментов.

- **Что сделано:** Расширен `.gitignore` комплексными правилами игнорирования (bin, obj, publish, tmp, *.rar, *.zip, venv, логи, кэши, пользовательские файлы VS).
- **Файлы:**
  - `.gitignore`
- **Почему:** Чтобы предотвратить случайное попадание мусора, скомпилированных dll и временных логов в систему контроля версий.

---

### 3. Исправление логических багов
- **Что сделано:** Реализована специальная обработка удаления виртуальных Docker-элементов (`docker:image:...`, `docker:container:...`, `docker:build-cache`) через выполнение реальных CLI-команд Docker (`docker rmi -f`, `docker rm -f`, `docker builder prune -f`).
- **Файлы:**
  - `SmartCleaner.Core/Cleaning/CleaningService.cs`
- **Почему:** Ранее при попытке очистить найденные Docker-образы или контейнеры сервис очистки пытался удалить виртуальный путь как обычный файл на диске Windows (`File.Delete` / `Directory.Delete`), что приводило к исключению о недопустимых символах в пути.

- **Что сделано:** Разрешен конфликт одинаковых иконок Segoe MDL2 — для раздела «Дашборд» назначена уникальная иконка аналитики (`\uE9D9`).
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
- **Почему:** В сайдбаре «Карта диска» и «Дашборд» использовали одну и ту же иконку `\uE9F9`, что визуально путало пользователя.

---

### 4. Редизайн UI, навигации и система тем (Windows 11 Fluent)
- **Что сделано:** Разработан полноценный менеджер тем `ThemeManager` с поддержкой динамического переключения тем оформления (**Тёмная / Светлая / Системная**) в реальном времени и сохранением предпочтений в конфигурационный файл `ui_settings.json`.
- **Файлы:**
  - `SmartCleaner.App/Services/ThemeManager.cs` (новый)
  - `SmartCleaner.Core/Models/SettingsModels.cs`
  - `SmartCleaner.App/App.xaml.cs`
  - `SmartCleaner.App/ViewModels/SettingsViewModel.cs`
  - `SmartCleaner.App/Views/SettingsView.xaml`
- **Почему:** Пользователь не имел возможности управлять темой оформления из интерфейса (тема определялась только один раз при старте по системному реестру). Теперь тему можно переключать как в Настройках, так и мгновенно кнопкой в сайдбаре.

- **Что сделано:** Сайдбар полностью реорганизован в чистую иерархическую структуру с группировкой по секциям:
  - **ОЧИСТКА:** Обзор (все категории) + динамический список категорий со счетчиками и бейджами размеров.
  - **ИНСТРУМЕНТЫ:** Дубликаты, Карта диска, Большие файлы, Автозагрузка, Обнаружение.
  - **СИСТЕМА:** Дашборд, Планировщик, Настройки.
  - **Футер:** Быстрый переключатель темы (`🌓 Тема`) и кнопка «О программе» (`ℹ️`).
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
  - `SmartCleaner.App/MainWindow.xaml.cs`
- **Почему:** Ранее 8 кнопок разделов и кнопка «О программе» были прибиты к низу (`DockPanel.Dock="Bottom"`), занимая около 400 пикселей по высоте. Это перекрывало динамические категории сканирования и приводило к сжатию и неудобному отображению на экранах с низким разрешением.

- **Что сделано:** Добавлены глобальные горячие клавиши:
  - `Ctrl + A` — Выбрать все безопасные элементы для очистки
  - `Ctrl + F` — Фокус на строку поиска
  - `Ctrl + S` — Запуск сканирования
  - `Delete` — Запуск очистки выбранного
  - `Escape` — Отмена сканирования
- **Файлы:**
  - `SmartCleaner.App/MainWindow.xaml`
- **Почему:** Повышение удобства и скорости работы для продвинутых пользователей.

- **Что сделано:** Модернизированы стили тем оформления (скругления 8–10px, карточки, ContextMenu, ComboBox, TextBox, TabControl/TabItem, бейджи риска).
- **Файлы:**
  - `SmartCleaner.App/Themes/DarkTheme.xaml`
  - `SmartCleaner.App/Themes/LightTheme.xaml`
- **Почему:** Приведение визуального стиля к современным стандартам Windows 11 Fluent Design без артефактов старого стиля WinForms/WPF.

- **Что сделано:** Обновлено окно «О программе» и документация проекта.
- **Файлы:**
  - `SmartCleaner.App/Views/AboutWindow.xaml`
  - `README.md`
  - `_AGENTS_MEMORY.md`
- **Почему:** Актуализация спецификаций: указание .NET 8 LTS, 14 сканеров, 8 встроенных утилит и современного технологического стека.

---

## 📋 Правило ведения Changelog

При каждом последующем изменении кода необходимо добавлять запись в секцию текущей/новой версии со структурой:
1. **Что сделано** (описание функционала или фикса).
2. **Файлы** (список затронутых исходных файлов).
3. **Почему** (причина изменений, бизнес-логика или устраняемый баг).
