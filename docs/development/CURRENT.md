# Current Engineering Context

Оновлено: 2026-09-06

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Стабільний реліз: **v1.2.1**. Публічний stable release опубліковано з tag `v1.2.1`; canonical application bundle містить один ZIP-asset. `code-quality-ux-improvements` завершено, прийнято та заархівовано. Новий ACTIVE PRIMARY — `release-experience-polish`.

Поточна наступна дія: `Architect review R1, then R2 deterministic structured release notes`.

## Architecture Summary

- `Program.cs` є manual composition root без DI-контейнера.
- `MainForm` координує UI та application services; довгі HTTP/file operations виконуються async.
- `Api/BdoUaApiClient` володіє API-запитами до `/releases`.
- `Services/LocalizationInstaller` відповідає за download, retry, checksum та timeout локалізації.
- `Storage` відповідає за config, installation state, original snapshot і restore points.
- `Update` містить GitHub Release discovery, schema-2 bundle validation, staging, replacement helper, rollback та startup maintenance.

## Tray / Background Contract

- Звичайне X ховає MainForm у Windows tray і не завершує процес; `Відкрити` та подвійний клік відновлюють те саме вікно; `Вихід` є фактичним завершенням.
- Нормальний і background запуск використовують single-instance activation; повторний запуск активує існуючий клієнт.
- Видимий API polling працює приблизно кожні 15 секунд, прихований — приблизно кожні 5 хвилин; локальний файл локалізації у background перевіряється дешевим metadata fingerprint.
- Localization update notification і application version update notification — окремі інформаційні канали з RAM-only dedup; application notification показується один раз для кожного tag у hidden tray. Click-to-open не є контрактом.
- Application-update discovery виконується одразу під час startup і приблизно кожні 5 хвилин, а restore та `Перевірити зараз` запитують свіжу перевірку. Download/install application update не є автоматичними.
- Приховування вікна не перериває активну операцію; explicit Exit зберігає безпечну семантику завершення.

## Current Phase

- Stage A — accepted.
- Stage C — MainForm physical decomposition, accepted.
- Tray/background T1–T6 — accepted, released in v1.2.0, plan archived.
- Stage B — **COMPLETED / REVIEWED / ACCEPTED**. B.1/B.2/B.3 прийняті; install rollback, selected restore-point state apply та restore-backup rollback мігровані на `InstallationStateStore.RestoreRawStateAsync`; typed `SaveAsync`, `BackupStore` snapshots і transaction orchestration залишаються окремими.
- Stage D — **COMPLETED / REVIEWED / ACCEPTED**; D.1 підтвердив negligible UI-thread local IO у realistic scenarios, D.2 — **NOT REQUIRED**.
- Code-quality roadmap — **COMPLETED / REVIEWED / ACCEPTED**. Stage E.1 — **COMPLETED / REVIEWED / ACCEPTED**; E.2 — **NO ACTION REQUIRED / ALREADY SATISFIED**, бо `LocalizationModeCard` уже має hover surface/border feedback.
- `code-quality-ux-improvements` — **ARCHIVED**, roadmap COMPLETED / REVIEWED / ACCEPTED, released through stable v1.2.1; no remaining implementation action.
- `release-experience-polish` — **ACTIVE PRIMARY**. R1 — **IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW**; R2 — **NOT IMPLEMENTED**; R3 — **NOT STARTED**.
- Exact v1.2.1 facts: release ID `383553345`, RC #28 / run `34028489675`, one public asset `BDO-UA-Client-v1.2.1-win-x64.zip`. Production `v1.2.0 → v1.2.1` built-in self-update succeeded.

## Validation / Release Facts

- RC #28 succeeded; stable v1.2.1 published.
- Production self-update `v1.2.0 → v1.2.1` was successfully exercised by the owner.
- R1 validation: Release build 0 warnings / 0 errors; 907 tests passed / 0 failed; `git diff --check` passed.

## Important Invariants

- API contract — `GET https://bdo-ua.com.ua/api/public/v1/releases`; актуальний release визначає сервер.
- Release compatibility перевіряється до install/update; incompatible release не завантажується.
- Game file operations: download/temp → validation → snapshot/restore point → replace → verify → state commit.
- Original snapshot незмінний; restore points — окремі pre-operation recovery points.
- Self-update current EXE не змінюється до manifest, SHA-256 і version validation.
- Secrets, tokens і credentials не зберігаються в repository.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — правила, контракти, security, build і commit requirements
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [`docs/plans/active/release-experience-polish.md`](../plans/active/release-experience-polish.md) — ACTIVE PRIMARY plan
- [`docs/plans/archive/code-quality-ux-improvements.md`](../plans/archive/code-quality-ux-improvements.md) — completed archived roadmap
- [`docs/releases/v1.2.1.md`](../releases/v1.2.1.md) — canonical release archive
- [`history/2026-09.md`](history/2026-09.md) — recent engineering journal
