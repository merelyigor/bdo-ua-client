# Current Engineering Context

Оновлено: 2026-08-27

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Стабільний реліз: v1.1.3. Останній runtime-affecting baseline включає v14.2.25 launcher polish та accepted managed-localization-overwrite hotfix. `client-ui-redesign` завершено та заархівовано. `code-quality-ux-improvements` — ACTIVE PRIMARY план; поточна фаза Stage A, наступна задача A.2 source markers. Production залишається native .NET 8 WinForms.

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

ACTIVE PRIMARY: `code-quality-ux-improvements`

Current phase: Stage A — Shared path/source primitives

**Hotfix completed and owner-accepted (2026-08-27):** managed localization hash-mismatch state resolution. When game/launcher replaces the localization file after BDO-UA Client installed it, the state was incorrectly classified as `Corrupted` (same patch, different SHA). Fix: new `ManagedFileChanged` transition resolves to `UpdateAvailable`/`WaitingForRelease` instead. Automated validation: 805/805 tests. Owner reproduced real launcher-restored-file scenario: preview showed "Доступне оновлення" / "Оновити", update completed, state returned UpToDate, restart remained UpToDate.

**A.1 GamePaths completed (2026-08-28):** introduced canonical `BdoClient.Services.GamePaths` primitive owning `AdsDirName` (`"ads"`), `LocalizationFileName` (`"languagedata_en.loc"`) and `GetLocalizationFilePath(gameRoot)`. Replaced duplicated literals/constants in `GameDetector`, `AdsFilesPatchReader`, `LocalizationInstallService`, `RestoreOriginalService`, `RestoreBackupService`, `BackupStore`, `MainForm`. Behavior unchanged; backup/restore-point layout unchanged (no extra `ads\` introduced). Automated validation: 807/807 tests.

Resume next: A.2 source markers — introduce `InstallationSource` constants and replace `"api"`/`"official"` literals.

Future: A.3 `AppPaths.InstallationFile` reuse, then Stage B raw installation-state operations.

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

1. Implement Stage A.2 source markers.
2. Review and validate.
3. Continue A.3 as a separate small task.
4. Do not automatically start Stage B before Stage A review.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — rules, contracts, security, build and commit requirements
- [`docs/architecture.md`](../architecture.md) — architecture and dependency graph
- [`docs/api.md`](../api.md) — API client and models
- [`docs/services.md`](../services.md) — service responsibilities
- [`docs/storage.md`](../storage.md) — state and backup storage
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [`docs/releases/README.md`](../releases/README.md) — release-note and RC process
- [`history/2026-08.md`](history/2026-08.md) — recent engineering journal
