# Contributing — как внести вклад в Puryx

Спасибо за интерес к проекту! Puryx — утилита очистки и оптимизации Windows на .NET 8 / WPF (кодовая база исторически называется `SmartCleaner.*`). Проект поддерживается одним мейнтейнером; любой вклад — правки, сканеры, тесты, документация — приветствуется.

## Сборка

Требования: Windows 10/11 и [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — проект WPF, сборка возможна только на Windows.

```bash
dotnet build SmartCleaner.sln            # Debug-сборка
dotnet build SmartCleaner.sln -c Release # релизный гейт: 0 ошибок / 0 предупреждений
```

Вспомогательные скрипты:

- `build.bat` — интерактивное меню (сборка/тесты/публикация/запуск/очистка);
- `publish_portable.bat` — portable-сборка одним exe (`release\portable\SmartCleaner.App.exe`).

## Тесты

```bash
dotnet test SmartCleaner.sln
```

- Тесты — xUnit, проект `SmartCleaner.Core.Tests`.
- Часть тестов помечена `[Trait("Category", "Integration")]`: они создают реальные файлы, каталоги и junction (`cmd mklink /J`) во временной папке, потому что поведение файловой системы и reparse-точек нельзя надёжно замокать. Запускайте их на локальной Windows-машине.
- Быстрый прогон без машинозависимых тестов: `dotnet test SmartCleaner.sln --filter Category!=Integration`.
- CI (`.github/workflows/ci.yml`) на каждый push/PR в master: `dotnet build -c Release` и `dotnet test -c Release` на `windows-latest`.

Правило вклада: новые функции и баг-фиксы приходят вместе с тестами. Перед коммитом или PR — зелёный Release-билд (0 ошибок / 0 предупреждений) и полный зелёный прогон тестов.

## Стиль коммитов

Conventional Commits, строго:

```
<type>(<scope>): <описание>
```

- Типы: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `chore`.
- **Без эмодзи.** Описания — на русском языке, со строчной буквы.
- Атомарность: один коммит = один смысловой шаг. Не смешивайте несвязанные изменения и не дробите осмысленный шаг на искусственные микро-коммиты.

## Модель ветвления и каденция

- Мейнтейнер работает в **линейном master без PR**: каждый коммит в master оставляет дерево зелёным (сборка + тесты); история публичная и не переписывается (force-push запрещён).
- Внешние контрибьюторы: fork → ветка (`feat/...`, `fix/...`) → **PR в master**.
- Релизы выходят в milestone-точках плана ([docs/ROADMAP.md](docs/ROADMAP.md)); между релизами изменения накапливаются в секции `[Unreleased]` журнала [CHANGELOG.md](CHANGELOG.md).
- Подробности каденции и правил «пушабельности» дня — [docs/RELEASE-CADENCE.md](docs/RELEASE-CADENCE.md).

## Структура решения

| Проект | Назначение |
|--------|------------|
| `SmartCleaner.App` | WPF-приложение: Views, ViewModels, DI-регистрация в `App.xaml.cs` |
| `SmartCleaner.Core` | Доменная логика: сканеры, очистка, безопасность, плагины, оптимизация |
| `SmartCleaner.Core.Tests` | xUnit-тесты движка очистки, безопасности и плагинов |
| `SmartCleaner.Data` | Встроенные плагины `Plugins/*.plugin.json` (копируются в сборку) |

Решение — `SmartCleaner.sln`, точка входа DI — `SmartCleaner.App/App.xaml.cs`.

## Как добавить сканер

1. Реализуйте `IScannerStrategy` (`SmartCleaner.Core/Scanning/IScannerStrategy.cs`) в `SmartCleaner.Core/Scanning/Scanners/`: свойства `CategoryName`, `CategoryIcon`, `DisplayOrder`, `IsEnabledByDefault` и метод `ScanAsync(IProgress<string>?, CancellationToken)`.
2. Зарегистрируйте сканер в DI — `SmartCleaner.App/App.xaml.cs`:

   ```csharp
   services.AddSingleton<IScannerStrategy, MyScanner>();
   ```

3. Покройте логику сканера тестами в `SmartCleaner.Core.Tests` (для файловых операций — временная папка; сценарии с junction — с трейтом `Integration`).

Обзор встроенных сканеров — в [README.md](README.md).

## Как добавить правило очистки без кода

Правила очистки подключаются плагином — JSON-манифестом `*.plugin.json` (`Id`, `Name`, `Version`, `Category`, `Rules` с `PathTemplate`, `Pattern`, `Recursive`, `Excludes`). Встроенные манифесты лежат в `SmartCleaner.Data/Plugins/`, пользовательские — в `Plugins/` рядом с приложением или в `%APPDATA%\SmartCleaner\Plugins`. Пути из плагинов проходят те же проверки безопасности, удаление — только в Корзину. Подробнее — в [README.md](README.md).

## Как предлагать изменения

- **Баг или идея** — сначала [issue](https://github.com/Harbuzilia/Puryx/issues): для крупных изменений обсудите подход до написания кода.
- **PR**: fork → ветка (`feat/...`, `fix/...`) → PR в master. Ожидания к PR: атомарные коммиты в стиле выше, зелёная сборка и тесты, описание «что / зачем / как проверено».
- Небольшие правки (опечатки, документация) можно отправлять сразу PR-ом без предварительного issue.
- Вклад лицензируется под [MIT](LICENSE) — как и весь проект.
