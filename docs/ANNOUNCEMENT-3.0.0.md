# Анонс Puryx 3.0.0 — production readiness (ЧЕРНОВИК)

> Статус: черновик для публикации (форум / Reddit / Telegram / Habr).
> Перед публикацией: проверить ссылки, решить, куда публикуем, при необходимости сократить под площадку.

---

Вышел **Puryx 3.0.0** — финальный релиз дорожной карты «production readiness». Puryx — бесплатный (MIT) чистильщик Windows для разработчиков и геймеров: 16 сканеров (системные кэши, браузеры, node_modules, pip, WSL2, Docker, шейдерные кэши GPU), 19 инструментов, карантин и многоуровневая защита удаления. Portable, один exe, без установки.

- **Puryx 3.0.0** — https://github.com/Harbuzilia/Puryx/releases/tag/v3.0.0
- **Puryx 2.8.0** (предыдущий, security and correctness) — https://github.com/Harbuzilia/Puryx/releases/tag/v2.8.0

## Что нового — по фазам дорожной карты

### v2.8.0 — Security and correctness (фазы P1–P2)

Закрыты все High- и Medium-дефекты аудита безопасности: деинсталлятор не даёт молчаливой элевации недоверенным командам; успех внешних утилит честно проверяется по коду возврата; системные утилиты запускаются только по абсолютным путям из системного каталога (анти-binary-planting); junction/symlink больше не обходит whitelist удаления; карантин подписан HMAC-SHA256. Ушли и выдуманные метрики — WinSxS/SMART/Compact считают по фактическим выводам утилит или честно показывают «н/д». Тесты: 214 → 349.

### v3.0.0 — Систематика и продуктизация (фазы P3–P4)

- **Единый исполнитель внешних команд**: все запуски утилит в Core идут через один контракт `ICommandExecutor` — честный код возврата, таймаут с kill-tree, стриминг прогресса, правильные кодировки вывода. 44 `Debug.WriteLine` заменены на `ILogger`; отмена операции — честный статус, а не ошибка.
- **Осторожность необратимых операций**: `/ResetBase` и удаление устаревших драйверов — только с явным opt-in; VACUUM баз — только при незанятой БД; junction не уводит сканер из корня пользователя.
- **Инфраструктура доверия**: CI на GitHub Actions (build + тесты на каждый push), [CONTRIBUTING.md](https://github.com/Harbuzilia/Puryx/blob/master/CONTRIBUTING.md), [SECURITY.md](https://github.com/Harbuzilia/Puryx/blob/master/SECURITY.md) (приватный канал уязвимостей), [docs/CODE-SIGNING.md](https://github.com/Harbuzilia/Puryx/blob/master/docs/CODE-SIGNING.md) — честный разбор, почему пока без подписи кода.

Итог: сборка 0 ошибок / 0 предупреждений, **475/475 тестов зелёные** (в 2.8.0 — 349, в 2.7.2 — 214).

## Установка

1. Скачайте `Puryx-3.0.0-portable-win-x64.exe` со страницы [релиза](https://github.com/Harbuzilia/Puryx/releases/tag/v3.0.0) (~70 МБ).
2. SHA256: `8693AFCBB70944EC9EB498743E5BB89DAB79139BA71A0994E565E7485B2A3D52`
   (для v2.8.0: `078425B087F8693B9F8976EA4DC27FB5B87FB2188878CB3A050DF2D8184A8EDB`)
3. Запустите. Приложение пока не подписано код-подписью: **Windows SmartScreen покажет предупреждение — нажмите «Подробнее» → «Выполнить в любом случае»**. Почему и когда это изменится — [docs/CODE-SIGNING.md](https://github.com/Harbuzilia/Puryx/blob/master/docs/CODE-SIGNING.md).
4. Удалённое по умолчанию отправляется в Корзину (восстановимо), повышенная очистка — по явному подтверждению.

## winget — в планах

Путь в [winget](https://github.com/Harbuzilia/Puryx/blob/master/docs/RESEARCH-WINGET.md) исследован и разобран: подпись кода политикой winget-pkgs не обязательна, манифест-основа готова (`packaging/winget/`), сабмит манифеста 3.0.0 — следующий шаг после этого релиза. До подписи остаётся риск AV/PUA-детекта и SmartScreen-предупреждений — поэтому в ближайшее время вернёмся к вопросу подписи. Альтернатива на сейчас — Scoop-бакет (публикация без ревью).

## Ссылки

- Репозиторий: https://github.com/Harbuzilia/Puryx
- Полный changelog: [CHANGELOG.md](https://github.com/Harbuzilia/Puryx/blob/master/CHANGELOG.md)
- Сообщить об уязвимости: [SECURITY.md](https://github.com/Harbuzilia/Puryx/blob/master/SECURITY.md)
- Дорожная карта: [docs/ROADMAP.md](https://github.com/Harbuzilia/Puryx/blob/master/docs/ROADMAP.md)
