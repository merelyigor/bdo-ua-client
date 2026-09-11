# Current Engineering Context

Оновлено: 2026-09-11

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Стабільний реліз: **v1.2.4**. Публічний stable release опубліковано з tag v1.2.4; canonical application bundle містить один ZIP-asset. Offline/degraded release-feed implementation завершено та прийнято зовнішнім Architect; ACTIVE implementation plan немає.

Поточна наступна дія: `External Architect final release review, then WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`.

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
- v15.46 — **REVIEWED / ACCEPTED**: додано normalized last-known release-feed cache, cached read-only fallback та Live-only mutation gate; API, installer safety, GameDetector threading і self-update architecture не змінювалися.
- v15.47 — **REVIEWED / ACCEPTED**: repository AI workflow синхронізовано навколо default Combined mode, risk-based pre-commit review, evidence policy та terminal state `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`; production і release behavior не змінювалися.
- v15.49 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: portable Architect bootstrap скорочено до 3505 UTF-16 code units, додано hard limit gate `7500` і його normal CI перевірку; production та release behavior не змінювалися.
- v15.51 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: repository rules зафіксували дозволене використання локально автентифікованого GitHub CLI для task-authorized repository/CI operations із перевіркою target SHA та без persistence credentials.
- v15.52 — **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**: game-found status розділяє локальний та останній відомий patch; застаріла гра показується як warning, а localization write-actions блокуються до оновлення гри. API, storage schema та install/restore transaction behavior не змінювалися.
- v1.2.4 — **PUBLISHED / VERIFIED**: public stable release ID `386705757` опубліковано на tag `v1.2.4`, який вказує на `e2694eb6d4288d7341d11b8bc187ce2a98b1e2de`; canonical ZIP і внутрішні hashes повторно перевірено. `NEXT.json` скинуто до порожнього schema-v1 джерела.
- `offline-degraded-reliability` — **ARCHIVED**: roadmap завершено, reviewed / accepted; активного PRIMARY немає.
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
- `docs/ai-workflow/PROJECT_CHAT_RULES.md` є Owner-maintained portable bootstrap prompt для Architect-chat sessions з hard limit `7500` UTF-16 code units; repository-specific workflow explanation залишається в інших ai-workflow docs.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — правила, контракти, security, build і commit requirements
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [docs/plans/archive/offline-degraded-reliability.md](../plans/archive/offline-degraded-reliability.md) — completed offline/degraded release-feed roadmap
- [`docs/plans/archive/release-experience-polish.md`](../plans/archive/release-experience-polish.md) — completed archived plan
- [`docs/ai-workflow/README.md`](../ai-workflow/README.md) — canonical orchestration, prompt, review та handoff contract
- [`docs/plans/archive/code-quality-ux-improvements.md`](../plans/archive/code-quality-ux-improvements.md) — completed archived roadmap
- [`docs/releases/v1.2.4.md`](../releases/v1.2.4.md) — current stable release archive
- [`history/2026-09.md`](history/2026-09.md) — recent engineering journal

## Current Task Handoff

- v1.2.3 release cycle is completed and archived.
- Offline/degraded release-feed mode is completed, reviewed and accepted; `offline-degraded-reliability` is archived.
- No ACTIVE/PRIMARY roadmap remains.
- v1.2.4 release cycle completed; public Release verified and `NEXT.json` reset for the next cycle.
- Next action: `External Architect final release review, then WORK CYCLE COMPLETE / OWNER DECISION REQUIRED`.
