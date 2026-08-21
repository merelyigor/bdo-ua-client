# Development Context

Цей каталог містить persistent engineering context для нового розробника або AI-сесії.
Документи є публічним вмістом репозиторію.

## Документи

- [`CURRENT.md`](CURRENT.md) — короткий canonical handoff: поточний статус, архітектура, інваріанти, рішення, відомі питання та наступні кроки. Це living document, не append-only changelog.
- [`history/YYYY-MM.md`](history/2026-08.md) — append-only журнал завершених meaningful engineering tasks за календарний місяць.

## Межі відповідальності

- Development journal фіксує стислий engineering context і рішення.
- Git history є джерелом точних diff, commit-ів і повної історії змін.
- `docs/plans/` містить implementation plans та їх lifecycle; він не замінюється journal-ом.
- `docs/releases/` містить user-facing release notes; він не замінюється journal-ом.
- `AGENTS.md` містить обов'язкові правила роботи, контракти та Definition of Done.

## Відновлення контексту

Нову developer/AI-сесію слід починати в такому порядку:

1. `AGENTS.md`
2. `docs/development/CURRENT.md`
3. Поточний або найновіший `docs/development/history/YYYY-MM.md`
4. Relevant ACTIVE plan з `docs/plans/`
5. Recent Git commits/diffs, якщо потрібні точні деталі
6. Subsystem documentation, релевантна задачі

## Ротація

- Один journal-файл створюється на календарний місяць.
- Формат імені: `YYYY-MM.md`.
- Коли в новому місяці завершується перша meaningful task, створюється файл цього місяця.
- Попередні monthly files стають immutable historical records; дозволені лише factual corrections.
- Не використовувати довільну нумерацію на кшталт `DEVLOG-2.md`.
- Старі journal-файли автоматично не видаляються.
- Git залишається джерелом точних історичних diff.

## Безпека

Journal і CURRENT ніколи не повинні містити паролі, API keys, tokens, credentials, private URLs, приватні дані користувачів, персональні деталі ChatGPT-розмов, raw sensitive logs або local environment secrets. Дозволені лише repository-relevant engineering facts.
