# Технічна документація BDO UA Client

## Навігація

| Документ | Опис |
|---|---|
| [architecture.md](architecture.md) | Архітектура проєкту, структура каталогів |
| [api.md](api.md) | API клієнт та моделі даних |
| [services.md](services.md) | Сервісний шар: детекція, встановлення, відновлення |
| [storage.md](storage.md) | Конфігурація, стан, бекапи |
| [ui.md](ui.md) | Користувацький інтерфейс |
| [states.md](states.md) | Моделі станів: LocalizationState, OperationState |
| [testing.md](testing.md) | Тестування |
| [build.md](build.md) | Збірка та пакування |
| [Плани](plans/README.md) | Плани реалізації |

## Поточний стан

- **Платформа:** Windows x64, .NET 8, WinForms
- **Пакування:** self-contained single-file (BDO-UA-Client.exe)
- **Тести:** 313 automated tests
- **Статус:** Stage 12 — Real Windows E2E stabilization

## Пов'язані документи

| Документ | Призначення |
|---|---|
| [AGENTS.md](../AGENTS.md) | Постійні правила та контракти проєкту |
| [Plans Registry](plans/README.md) | Реєстр активних, backlog та архівних планів |
| [Плани](plans/README.md) | Історичні та активні плани |
