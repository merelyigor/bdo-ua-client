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
| [update.md](update.md) | Self-update клієнта (GitHub Releases, Stage 13) |
| [build.md](build.md) | Збірка, CI та реліз |
| [Релізи](releases/README.md) | Процедура релізу та архів реліз-нотаток |
| [Дизайн](design/BDO_THEME_PLAN.md) | BDO-тема UI (план та кольори) |
| [Плани](plans/README.md) | Плани реалізації |
| [Development context](development/README.md) | Поточний engineering handoff та monthly journal |
| [AI workflow](ai-workflow/README.md) | Canonical Owner / Architect-Reviewer / Implementation-Agent workflow, prompts, reviews та handoffs |

## Поточний стан

- **Платформа:** Windows x64, .NET 8, WinForms
- **Пакування:** self-contained single-file (BDO-UA-Client.exe)
- **Оновлення клієнта:** GitHub Releases `merelyigor/bdo-ua-client`, canonical ZIP transport (schema-2 manifest)
- **Тести:** 907 автоматизованих тестів (за останнім Release validation)
- **Стабільний реліз:** v1.2.2, опублікований з canonical ZIP self-update/release transport
- **Плани:** усі approved implementation plans заархівовані; ACTIVE plan немає, наступна product roadmap ще не затверджена

## Пов'язані документи

| Документ | Призначення |
|---|---|
| [AGENTS.md](../AGENTS.md) | Постійні правила та контракти проєкту |
| [Plans Registry](plans/README.md) | Реєстр активних, backlog та архівних планів |
| [Development context](development/README.md) | Persistent engineering context та журнал завершених задач |
| [Плани](plans/README.md) | Історичні та активні плани |
