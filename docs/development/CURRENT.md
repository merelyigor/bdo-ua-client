# Current Engineering Context

Оновлено: 2026-09-04

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Стабільний реліз: v1.1.3. `code-quality-ux-improvements` — ACTIVE PRIMARY; Stage A/C accepted, Stage B deferred until tray release completion. `background-tray-notifications` — ACTIVE secondary: T1–T6 — **COMPLETED / REVIEWED / ACCEPTED**. Next: Release preparation.

## Architecture Summary

- `Program.cs` є manual composition root без DI-контейнера.
- `MainForm` координує UI та application services; довгі HTTP/file operations виконуються async.
- `Api/BdoUaApiClient` володіє API-запитами до `/releases`.
- `Services/LocalizationInstaller` відповідає за download, retry, checksum та timeout локалізації.
- `Storage` відповідає за config, installation state, original snapshot і restore points.
- `Update` містить GitHub Release discovery, schema-2 bundle validation, staging, replacement helper, rollback та startup maintenance.
- GitHub updater запускається у background і використовує internal `--apply-update <session-id>` helper mode.

## Important Invariants

- API contract — `GET https://bdo-ua.com.ua/api/public/v1/releases`; актуальний release визначає сервер.
- Release compatibility перевіряється до install/update; incompatible release не завантажується.
- Game file operations: download/temp → validation → snapshot/restore point → replace → verify → state commit.
- SHA-256 перевіряється для release files, localization files, updater candidates і owned recovery files.
- Original snapshot незмінний; restore points — окремі pre-operation recovery points.
- Self-update current EXE не змінюється до manifest, SHA-256 і version validation.
- `File.Replace` retry policy для Windows replacement зберігається з bounded 60-second retry window.
- Session cleanup fail-closed: metadata видаляється останньою, unknown files не видаляються рекурсивно, lock failure залишає retryable metadata.
- Restore-point retention: latest 3 valid points; original snapshot не бере участі в pruning.
- Shared BDO-UA/localization production HTTP traffic uses `SocketsHttpHandler.UseProxy = false` with `ResilientConnectionConnector`: DNS candidates are interleaved where practical, connection attempts use a bounded stagger, and the first successful TCP stream wins. No server IP is hardcoded; standard .NET HTTPS/TLS/SNI and certificate validation remain in control of the handler. The GitHub updater keeps its separate HTTP client and networking policy.
- Secrets, tokens і credentials не зберігаються в repository.

## Relevant Subsystems

- **API:** `Api/`, `Models/`, `docs/api.md`
- **Localization:** `Services/LocalizationInstaller.cs`, `Services/LocalizationInstallService.cs`, `docs/services.md`
- **State and backup:** `Storage/`, `docs/storage.md`, `docs/states.md`
- **Self-update:** `Update/`, `Program.cs`, `AGENTS.md` §41
- **UI operation states:** `MainForm.cs`, `Services/OperationState.cs`, `docs/ui.md`
- **Build and tests:** `BdoUaClient.sln`, `BdoClient.Tests/`, `docs/build.md`, `docs/testing.md`

## Recently Completed

- Native WinForms launcher redesign completed through Stage 4 (Stages 0–3 + final DPI/runtime validation).
- v14.2.25 polished launcher operation feedback: removed neutral badges, responsive progress, Cancel visibility, cleanup logging.
- v1.1.2 published as stable release; owner verified Windows scaling 100/125/150/200%.
- `client-ui-redesign` archived.
- Code-quality/UX backlog reconciled with v14.2.25 baseline; architecture decisions recorded (no generic transaction engine, no generic runner, physical MainForm decomposition only).

Exact changes and validation are recorded in Git history and the monthly journal.

## Active Work

T5 uses `UpdateAvailable`-only actionability, RAM-only episode dedup, hidden first-episode native notification, visible latch without balloon, informational-only notification behavior, and isolated presentation failure. Real Windows E2E confirmed that `BalloonTipClicked` is not reliably delivered by the current notification surface; activation is intentionally outside the first-version contract. Current validation: 891 tests passed; Release build 0 warnings/0 errors.

ACTIVE PRIMARY: `code-quality-ux-improvements`

Current phase: Stage C — MainForm physical decomposition — COMPLETED / REVIEWED / ACCEPTED (C.1–C.5 physical implementation complete; final architect review / lifecycle handoff completed). Current execution: `background-tray-notifications` ACTIVE (secondary, not PRIMARY). **T1 — Tray lifetime shell — COMPLETED / REVIEWED / OWNER ACCEPTED**: native WinForms `NotifyIcon` + `ContextMenuStrip` у `MainForm.Tray.cs` (partial `MainForm`); X ховає у трей, `Відкрити`/double-click відновлюють; idle Exit — реальне завершення; звичайне X під час активної Install/Update/Restore не скасовує операцію; `_updateHandoffInProgress` лишається першою гілкою безпеки; self-update `Application.Exit()` — реальний вихід; Windows shutdown не перетворюється на hide-to-tray; іконка трея видобута з exe (`Icon.ExtractAssociatedIcon`) із fallback `SystemIcons.Application`, власна іконка disposed лише при реальному завершенні; виправлено дефект геометрії після відновлення (відкладений post-show relayout `RefreshModeCardLayout()` + `ScheduleContentFit()`). Owner E2E: `T1 PASS, layout defect fixed`. **T1.1 — Autostart / background startup — COMPLETED / REVIEWED / ACCEPTED**: `MainForm.Tray.cs` тепер також володіє autostart/tray інтеграцією; додано `Services/WindowsAutostartService` (HKCU `Run` enable/disable/is-enabled + canonical command) та `Services/SingleInstanceCoordinator` (Mutex + AutoReset event); точний флаг `--background` (Normal приховано в трей, той самий стартовий pipeline); конфіг має `autostart_prompt_dismissed` (лише UX-стан підказки); HKCU `Run` — джерело істини автозапуску; нормальний/background застосунок — single-instance на Windows-сесію (manual вторинний запуск відновлює існуючий primary, duplicate background завершується тихо); helper `--apply-update` залишається поза гейтом single-instance; automated валідація 835/835, build 0/0. **T3 — Background polling cadence — COMPLETED / REVIEWED / ACCEPTED** (2026-09-03); **T4 — Local file-change trigger — COMPLETED / REVIEWED / ACCEPTED** (2026-09-04).

**T2 — Operation / shutdown semantics — COMPLETED / REVIEWED / ACCEPTED (2026-08-29):** `_exitAfterOperation` тепер означає відкладене реальне завершення, що стало pending під час активної Install/Update/Restore. Звичайне X під час активної операції лишає hide-to-tray без скасування (T1 поведінка незмінна). Явний Exit трея під час активної операції: `e.Cancel = true`, `_closing = true`, `_exitAfterOperation = true`, запит скасування існуючого CTS; після повного безпечного очищення операції (`CompletePendingExitAfterOperation()` → відкладений `BeginInvoke(Close)`) процес завершується автоматично — друге натискання `Вихід` не потрібне. Windows/system реальне закриття під час активної операції слідує тому ж патерну defer/cancel/cleanup/exit; автоматичне відновлення оригінального Windows shutdown не гарантується і не ініціюється (без Windows shutdown API). Self-update `_updateHandoffInProgress` лишається першою гілкою FormClosing; успішний handoff порядок незмінний. `HandleInstallAsync` захищено від pre-CTS гонки: після `await _stateService.ResolveAsync(...)` встановлення переривається до створення CTS/початку транзакції. Тести: 845/845, build 0/0.

**T3 — Background polling cadence — COMPLETED / REVIEWED / ACCEPTED (2026-09-03):** один `ReleaseFeedPoller.RunLoopAsync`; усі recurring/immediate API feed-запити сходяться на єдиний шлях `PerformPollAsync` → `_apiClient.GetReleasesAsync`; максимум одночасних API feed-запитів = 1. Виробничі інтервали: видимий клієнт ≈ 15 секунд, tray/background клієнт ≈ 5 хвилин. `HideToTray()` обирає Background і скидає поточну visible-затримку (наступний poll — свіжа Background каденція); `--background` обирає Background через той самий нормальний startup pipeline до `_poller.Start`. `RestoreFromTray()` обирає Visible і викликає негайний API poll (`RequestImmediatePoll`); наступний poll — свіжа Visible ~15s каденція. Меню трея: `Відкрити` → `Перевірити зараз` → `Запускати разом із Windows` → separator → `Вихід`; `Перевірити зараз` викликає лише `_poller.RequestImmediatePoll()`. Кілька immediate-запитів коалесують; immediate під час активного запиту використовує Option A (поточний запит задовольняє, додатковий не ставиться). `Pause` авторитетний (без poll під час паузи, immediate відкидаються); `Resume` запускає свіжу каденцію з поточним режимом без immediate-poll. Feed-шлях не змінено: poller → `OnFeedCandidate` → `OnReleaseFeedCandidate` → `FeedApplicationCoordinator` → `ApplyFeedPipelineAsync`. Під час фінального concurrency-рев'ю виявлено та виправлено витік власності scheduler-wait CTS: `RunLoopAsync` створює linked wait CTS, публікує `_schedulerWaitCts`, `WakeSchedulerWait()` лише скасовує його, `RunLoopAsync` у `finally` очищає посилання через `ReleaseSchedulerWait` і диспозить власний CTS рівно один раз; інваріант `_schedulerWaitCts == null` доки виконується API-запит; `Dispose()` не володіє `_schedulerWaitCts`, `_disposed` — `volatile`. Це усуває необмежений per-iteration витік linked-CTS у довгоживучому tray-клієнті. Тести: 862/862, build 0/0. Живого tray/runtime E2E не виконувалось — automated validation = unit/concurrency-тести + статичний/MainForm-інтеграційний рев'ю. T4 згодом реалізовано (див. нижче).

**T4 — Local file-change trigger — COMPLETED / REVIEWED / ACCEPTED (2026-09-04):** окремий `System.Windows.Forms.Timer` ~5 хв у `MainForm.LocalFileMonitor.cs` (background/прихований режим; `ReleaseFeedPoller` не змінено). Дешева відбиток-метаданих `LocalizationFileFingerprint` (Exists/Length/LastWriteTimeUtc), без постійного SHA; відсутній файл — валідний відбиток. `LocalFileChangeTracker` — RAM-only, `OrdinalIgnoreCase` порівняння шляху. Baseline належить останньому успішному `RefreshStateAsync`; змінений/відсутній baseline → існуючий шлях `RefreshStateAsync`; restore робить одне дешеве порівняння. `FeedApplicationCoordinator` серіалізує локальну й API-реконсиляцію. Тестів: 880/880; build 0/0.

**Hotfix completed and owner-accepted (2026-08-27):** managed localization hash-mismatch state resolution. When game/launcher replaces the localization file after BDO-UA Client installed it, the state was incorrectly classified as `Corrupted` (same patch, different SHA). Fix: new `ManagedFileChanged` transition resolves to `UpdateAvailable`/`WaitingForRelease` instead. Automated validation: 805/805 tests. Owner reproduced real launcher-restored-file scenario: preview showed "Доступне оновлення" / "Оновити", update completed, state returned UpToDate, restart remained UpToDate.

**C.1 Presentation extraction completed (2026-08-28):** 21 presentation/layout методів фізично перенесено з `MainForm.cs` у новий `MainForm.Presentation.cs` (partial `MainForm`): theme/shell layout, game-status presentation, operation/control presentation, mode-card presentation, public presentation helpers, `BuildLogsIcon`. Усі fields, constructor, `WireEventHandlers`, `MainForm_FormClosing`, `LogsButton_Click` залишилися у `MainForm.cs`. `RefreshStateAsync` та `RefreshModeCardLayout` залишилися поза Presentation (належать майбутньому C.3 Localization). Runtime-поведінка не змінена; build/tests без помилок (807/807). Наступна задача: C.2 Startup extraction. Tray залишається BACKLOG; Stage B відкладено.

**C.2 Startup extraction completed (2026-08-28):** `MainForm_Shown` та `RunStartupLifecycleMaintenanceAsync` фізично перенесено з `MainForm.cs` у новий `MainForm.Startup.cs` (partial `MainForm`). Усі fields, constructor, `WireEventHandlers`, `MainForm_FormClosing`, `LogsButton_Click` залишилися у `MainForm.cs`; підписка `this.Shown += MainForm_Shown` лишилася єдиною у `MainForm.cs`. Startup sequencing, poller start, update lifecycle та startup timer semantics не змінено. Runtime-поведінка не змінена; build/tests без помилок (807/807). Наступна задача: C.3 Localization extraction. Tray залишається BACKLOG; Stage B відкладено.

**C.3 Localization extraction completed (2026-08-28):** точно 19 методів (modes, game detect/browse, selection/config routing, release-feed application, `RefreshStateAsync`) фізично перенесено з `MainForm.cs` у новий `MainForm.Localization.cs` (partial `MainForm`) без зміни тіл або семантики власності. У `MainForm.cs` залишилися: усі fields, constructor, `WireEventHandlers`, `MainForm_FormClosing`, `LogsButton_Click`; wiring конструктора `FeedApplicationCoordinator`; підписка `_poller.OnFeedCandidate`; підписка `detectGameButton.Click`; підписка `browseGameButton.Click`. `MainForm.Localization.cs` володіє двома динамічними підписками карток, оскільки вони залишилися всередині перенесеного тіла `BuildDynamicModes`: `card.SelectionRequested += ModeCard_SelectionRequested;` та `card.ActionRequested += ModeCard_ActionRequested;`. Release-feed UI-thread handoff, feed pipeline ordering та `RefreshStateAsync` semantics незмінні. Шість C.4 операційних методів (`HandleInstallAsync` тощо) залишилися у `MainForm.cs` для C.4. Runtime-поведінка не змінена. Наступна задача: C.4 Operations extraction. Tray залишається BACKLOG; Stage B відкладено.

**A.1 GamePaths completed (2026-08-28):** introduced canonical `BdoClient.Services.GamePaths` primitive owning `AdsDirName` (`"ads"`), `LocalizationFileName` (`"languagedata_en.loc"`) and `GetLocalizationFilePath(gameRoot)`. Replaced duplicated literals/constants in `GameDetector`, `AdsFilesPatchReader`, `LocalizationInstallService`, `RestoreOriginalService`, `RestoreBackupService`, `BackupStore`, `MainForm`. Behavior unchanged; backup/restore-point layout unchanged (no extra `ads\` introduced). Automated validation: 807/807 tests.

**A.2 InstallationSource completed (2026-08-28):** introduced canonical `BdoClient.Storage.InstallationSource` static constants (`Api = "api"`, `Official = "official"`). Replaced duplicated `"api"`/`"official"` source-marker literals in `InstallationMetadata`, `InstallationStateStore`, `LocalizationInstallService`, `LocalizationStateService`, `RestoreOriginalService`, `MainForm`. `Source` property stays `string`; persisted JSON values, casing, case-sensitivity, and unknown-source rejection are unchanged. Automated validation: 807/807 tests.

**C.4 Operations extraction completed (2026-08-28):** точно шість операційних методів (`HandleInstallAsync`, `RestoreOriginalButton_Click`, `HandleRestoreOriginalAsync`, `CancelButton_Click`, `MapInstallError`, `MapRestoreError`) фізично перенесено з `MainForm.cs` у новий `MainForm.Operations.cs` (partial `MainForm`) без зміни тіл, порядку operation lifecycle, cancellation semantics, error mapping або feed/poller взаємодії. `SetControlsDuringOperation` лишився у `MainForm.Presentation.cs`, `RefreshStateAsync` — у `MainForm.Localization.cs`, `MainForm_FormClosing` та `LogsButton_Click` — у `MainForm.cs`. Усі fields, constructor, `WireEventHandlers` і підписки `restoreOriginalButton.Click`/`cancelButton.Click` залишилися у `MainForm.cs`. `MainForm.Operations.cs` — лише фізичний partial, не контролер/сервіс/generic runner. Runtime-поведінка не змінена; build 0 errors/0 warnings, tests 807/807. Наступна задача: C.5 ApplicationUpdate extraction. Tray залишається BACKLOG; Stage B відкладено.

**C.5 ApplicationUpdate extraction completed (2026-08-28):** точно 10 методів self-update (`StartBackgroundUpdateCheck`, `RunUpdateCheckAsync`, `RefreshUpdateButtonPresentation`, `UpdateButton_Click`, `HandleApplicationUpdateDownloadAsync`, `MapUpdatePackageError`, `MapPreparationError`, `RestorePostHandoffFailureState`, `TryCleanupPreparedAttempt`, `CleanupAbandonedStagingSession`) фізично перенесено з `MainForm.cs` у новий `MainForm.ApplicationUpdate.cs` (partial `MainForm`) без зміни тіл. Усі fields, constructor, `WireEventHandlers`, `MainForm_FormClosing`, `LogsButton_Click` залишилися у `MainForm.cs`; підписка `updateButton.Click += UpdateButton_Click` лишилася єдиною у `MainForm.cs`; Startup викликає `StartBackgroundUpdateCheck` cross-partial без обгорток. Handoff semantics незмінні: `_updateHandoffInProgress = true` встановлюється перед `Application.Exit`; outer-finally гейт `if (!_updateHandoffInProgress)` запобігає звичайному resume після успішної handoff; `RestorePostHandoffFailureState`, `TryCleanupPreparedAttempt`, `CleanupAbandonedStagingSession`, error mappers ідентичні. UTF-8/Mojibake guard: перенесення виконано на рівні байтів з чистого HEAD, без пошкоджень. Runtime-поведінка не змінена; build 0 errors/0 warnings, tests 807/807. Наступна задача: **T1 read-only tray lifetime inspection / mapping** (owner активував `background-tray-notifications` — ACTIVE; runtime-код ще не реалізовано). Stage B відкладено.

**A.3 AppPaths.InstallationFile reuse completed (2026-08-28):** `InstallationStateStore` now exposes its canonical `InstallationFile` from `AppPaths`; install/restore services use `_stateStore.InstallationFile` instead of manually reconstructing `Path.Combine(_stateStore.StateDir, "installation.json")`. No path/schema/behavior change; restore-point `installation-state.json` and rollback temp naming untouched; `StateDir` retained for the rollback temp path. Stage A is now implementation-complete, reviewed and accepted. Stage C also COMPLETED / REVIEWED / ACCEPTED; owner explicitly activated `background-tray-notifications` (now ACTIVE); next task is T1 read-only tray lifetime inspection / mapping. Automated validation: 807/807 tests.

## Known Issues / Unresolved Investigations

- Historical intermittent cold-start connection stalls were mitigated by the DNS-based resilient connector after repeated owner runtime validation. The exact historical Cloudflare address responsible for each slow run is not asserted.
- `docs/index.md` contains older historical test/stage text and is not the source of truth for current implementation status.

## Important Decisions

- Public GitHub Releases are the updater source; no GitHub token or custom updater backend is used.
- Production bundles are canonical GitHub-generated schema-2 ZIP artifacts with four flat files; the updater validates internal EXE metadata and SHA-256.
- There is no permanent `Updater.exe`, PowerShell/BAT updater, Windows service, automatic UAC elevation, or silent forced update.
- Direct networking intentionally prioritizes deterministic startup latency over environments that require a system HTTP proxy. Proxy selection is not a current UI/configuration feature.
- No generic GameFileTransaction is planned; BackupStore already centralizes the destructive replace/recovery boundary.
- No generic RunExclusiveOperationAsync is planned; self-update handoff is materially different.
- MainForm decomposition is physical (partial files) only, not architectural.
- `CURRENT.md` is a living snapshot and normally stays below approximately 400 lines. It is not append-only. Remove obsolete context only after it is present in the monthly journal or Git history.

## Next Likely Work

1. Stage C COMPLETED / REVIEWED / ACCEPTED (C.1–C.5 фізична декомпозиція + фінальний architect review/lifecycle handoff). `background-tray-notifications` (ACTIVE secondary): **T1–T6 COMPLETED / REVIEWED / ACCEPTED**. Next: Release preparation.
2. Do not start Stage B before tray/background release completion.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — rules, contracts, security, build and commit requirements
- [`docs/architecture.md`](../architecture.md) — architecture and dependency graph
- [`docs/api.md`](../api.md) — API client and models
- [`docs/services.md`](../services.md) — service responsibilities
- [`docs/storage.md`](../storage.md) — state and backup storage
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [`docs/releases/README.md`](../releases/README.md) — release-note and RC process
- [`history/2026-09.md`](history/2026-09.md) — recent engineering journal
