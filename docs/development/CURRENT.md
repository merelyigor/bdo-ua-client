# Current Engineering Context

Оновлено: 2026-09-06

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Стабільний реліз: **v1.2.0**. Публічний реліз опубліковано з tag `v1.2.0`; canonical application bundle містить один ZIP-asset. `background-tray-notifications` — T1–T6 COMPLETED / REVIEWED / ACCEPTED, released and archived. `code-quality-ux-improvements` залишається ACTIVE PRIMARY.

Поточна наступна дія: `Architect review E.1, then close code-quality-ux-improvements plan`.

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
- Єдиний actionable factual state для notification — `UpdateAvailable`; dedup RAM-only, без PublicId/mode keys. Windows notification є інформаційною; click-to-open не є контрактом.
- Приховування вікна не перериває активну операцію; explicit Exit зберігає безпечну семантику завершення.

## Current Phase

- Stage A — accepted.
- Stage C — MainForm physical decomposition, accepted.
- Tray/background T1–T6 — accepted, released in v1.2.0, plan archived.
- Stage B — **COMPLETED / REVIEWED / ACCEPTED**. B.1/B.2/B.3 прийняті; install rollback, selected restore-point state apply та restore-backup rollback мігровані на `InstallationStateStore.RestoreRawStateAsync`; typed `SaveAsync`, `BackupStore` snapshots і transaction orchestration залишаються окремими.
- Stage D — **COMPLETED / REVIEWED / ACCEPTED**; D.1 підтвердив negligible UI-thread local IO у realistic scenarios, D.2 — **NOT REQUIRED**.
- Stage E.1 — **IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW**; E.2 — **NO ACTION REQUIRED / ALREADY SATISFIED**, бо `LocalizationModeCard` уже має hover surface/border feedback.

## Validation / Release Facts

- RC #27 succeeded; stable v1.2.0 published.
- Release preparation and post-release finalization залишили runtime, tests, workflows та scripts без змін.
- Остання локальна валідація B.3: Release build 0 warnings / 0 errors; 901 tests passed / 0 failed.

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
- [`docs/plans/active/code-quality-ux-improvements.md`](../plans/active/code-quality-ux-improvements.md) — PRIMARY plan і Stage B
- [`docs/releases/v1.2.0.md`](../releases/v1.2.0.md) — canonical release archive
- [`history/2026-09.md`](history/2026-09.md) — recent engineering journal
