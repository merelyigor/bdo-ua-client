# Current Engineering Context

Оновлено: 2026-09-08

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Стабільний реліз: **v1.2.3**. Публічний stable release опубліковано з tag v1.2.3; canonical application bundle містить один ZIP-asset. Offline/degraded release-feed implementation виконується в ACTIVE PRIMARY plan offline-degraded-reliability і очікує external Architect review.

Поточна наступна дія: External Architect review of offline/degraded release-feed mode.

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
- `release-experience-polish` — **ARCHIVED**. R1/R2/R3 — **COMPLETED / REVIEWED / ACCEPTED**; application discovery має startup + resident ~5-minute monitoring, а schema-v1 `NEXT.json` generator є normal release contract.
- Exact v1.2.2 facts: release ID `383636389`, RC #29 / run `34038984319`, one public asset `BDO-UA-Client-v1.2.2-win-x64.zip`; outer SHA-256 `329c31987955dbb2139a061ea09bbad0e89fa403cf731343d124b917e68f120c`. Власник успішно виконав built-in update до `v1.2.2`; застосунок після оновлення працює. Hidden periodic notification не спостерігалася окремо в production smoke.
- v15.41 — **REVIEWED / ACCEPTED**: bounded test-only MainForm lifecycle integration coverage додано без production changes; testability gate пройдено через dedicated STA/message-loop fixture.
- v15.42 — **REVIEWED / ACCEPTED**: додано README та MainForm informational guidance для видалення portable-клієнта без self-uninstall механізму; localization, autostart і storage behavior не змінювалися.
- v15.43 — **REVIEWED / ACCEPTED**: Release Candidate workflow більше не створює фінальний tag; Owner native UI smoke gate успішно пройдено перед публікацією `v1.2.3`.
- v15.46 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: додано normalized last-known release-feed cache, cached read-only fallback та Live-only mutation gate; API, installer safety, GameDetector threading і self-update architecture не змінювалися.
- v1.2.3 — **PUBLISHED / VERIFIED**: tag `v1.2.3` і public Release ID `384894464` опубліковано на exact approved RC SHA `ec891ea7077dd70e2aafd0c2e00674f47c45a94a`; Owner native UI smoke exact RC passed.

## Validation / Release Facts

- RC #29 succeeded; stable v1.2.2 published.
- Production self-update to `v1.2.2` was successfully exercised by the owner; the updated application works.
- Structured release-note pipeline is the normal release contract; `NEXT.json` has been reset to an empty schema-v1 source for the next cycle.
- R3 validation: Release build 0 warnings / 0 errors, 907 tests passed / 0 failed, resolver/generator tests and release preflight passed.
- v15.41 validation: Release build — 0 warnings / 0 errors; full Release suite — 911 passed / 0 failed / 0 skipped; focused MainForm lifecycle suite — 4 passed; 20/20 independent targeted invocations — 4/4 passed; related lifecycle suites passed.
- v15.46 validation: Release build — 0 warnings / 0 errors; full Release suite — 924 passed / 0 failed / 0 skipped; focused cache/policy/poller/MainForm suite — 87 passed; five independent MainForm lifecycle invocations passed; git diff --check passed.
- v1.2.3 validation: RC #30 / run `34236529921`, public Release ID `384894464`, canonical asset `BDO-UA-Client-v1.2.3-win-x64.zip` (asset ID `550865653`, 67,904,470 bytes, SHA-256 `0c740be029bfe30a2d020109a817a64af2eb927c24ab143fa868c3fe279718fe`), internal EXE SHA-256 `2abafb502de7f6b8effc8e3ee620afd813ce4ad544fe49d908739e3d4487932b`; public asset downloaded back and verified, live self-update eligibility from v1.2.2 confirmed.

## Important Invariants

- API contract — `GET https://bdo-ua.com.ua/api/public/v1/releases`; актуальний release визначає сервер.
- Release compatibility перевіряється до install/update; incompatible release не завантажується.
- Game file operations: download/temp → validation → snapshot/restore point → replace → verify → state commit.
- Original snapshot незмінний; restore points — окремі pre-operation recovery points.
- Self-update current EXE не змінюється до manifest, SHA-256 і version validation.
- Secrets, tokens і credentials не зберігаються в repository.
- `docs/ai-workflow/` є canonical orchestration/process documentation: repository формально розділяє Owner, Architect-Reviewer та Implementation Agent responsibilities; external conversations — coordination, а repository-owned docs/code — persistent truth.
- `docs/ai-workflow/PROJECT_CHAT_RULES.md` є єдиною Owner-maintained canonical copyable project instruction для Architect-chat sessions; repository-specific workflow explanation залишається в інших ai-workflow docs.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — правила, контракти, security, build і commit requirements
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [docs/plans/active/offline-degraded-reliability.md](../plans/active/offline-degraded-reliability.md) — current offline/degraded release-feed plan
- [`docs/plans/archive/release-experience-polish.md`](../plans/archive/release-experience-polish.md) — completed archived plan
- [`docs/ai-workflow/README.md`](../ai-workflow/README.md) — canonical orchestration, prompt, review та handoff contract
- [`docs/plans/archive/code-quality-ux-improvements.md`](../plans/archive/code-quality-ux-improvements.md) — completed archived roadmap
- [`docs/releases/v1.2.2.md`](../releases/v1.2.2.md) — current stable release archive
- [`history/2026-09.md`](history/2026-09.md) — recent engineering journal

## Current Task Handoff

- v1.2.3 release cycle is completed and archived.
- Offline/degraded release-feed mode is implemented and validated; external Architect review is pending.
- Next action: `External Architect review of offline/degraded release-feed mode`.
