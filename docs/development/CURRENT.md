# Current Engineering Context

Оновлено: 2026-08-22

## Project Purpose / Status

BDO-UA Client — Windows .NET 8 WinForms застосунок для пошуку Black Desert Online, отримання українських локалізацій через `bdo-ua.com.ua`, безпечного встановлення, оновлення та відновлення файлів гри.

Останній runtime-affecting baseline: `v14.2.3` — direct networking policy для application-owned HTTP clients. Активний PRIMARY implementation plan: `client-ui-redesign`, Stage 2 completed and owner-approved / Stage 3 next. `MainForm` now uses a Material-inspired BDO header/card layout with dynamic content-driven window sizing and vertical scroll fallback. Startup selection prioritizes the currently installed API `ModeSlug` when it remains installable, then `LastMode`. Shared BDO-UA/localization HTTP traffic uses `SocketsHttpHandler` with `UseProxy = false` and a DNS-based staggered multi-address TCP fallback; no hardcoded IPs are used, TLS remains standard .NET validation, and GitHub updater networking remains separate. Stage 3 must replace visible RadioButtons with API-driven selectable mode cards and graphical flag presentation. Production remains native .NET 8 WinForms.

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

- v14.1.4 isolated replacement workspace and added foreground restart handoff.
- v14.2 hardened session cleanup and bounded restore-point storage.
- v14.2.1 preserved cleanup compatibility with legacy sessions that lack `package_file_name` and added owned-file SHA verification.
- v14.2.2 reset stale progress-bar value when returning to `OperationState.Idle`.
- v14.2.3 bypassed Windows system proxy/WPAD discovery for production HTTP clients.
- Persistent development context and monthly engineering journal were established under `docs/development/`.

Exact changes and validation are recorded in Git history and the monthly journal.

## Active Work

`client-ui-redesign` is the sole ACTIVE PRIMARY plan. Stage 2 — Static Material-inspired layout and information hierarchy — is complete and owner-approved. Stage 3 — UI completion — is next and combines selectable localization cards, graphical flags, operation/progress presentation, and application-update visual consistency. Stage 4 will be final validation and regression closure.

## Known Issues / Unresolved Investigations

- Historical intermittent cold-start connection stalls were mitigated by the DNS-based resilient connector after repeated owner runtime validation. The exact historical Cloudflare address responsible for each slow run is not asserted.
- `docs/index.md` contains older historical test/stage text and is not the source of truth for current implementation status.

## Important Decisions

- Public GitHub Releases are the updater source; no GitHub token or custom updater backend is used.
- Production bundles are canonical GitHub-generated schema-2 ZIP artifacts with four flat files; the updater validates internal EXE metadata and SHA-256.
- There is no permanent `Updater.exe`, PowerShell/BAT updater, Windows service, automatic UAC elevation, or silent forced update.
- Direct networking intentionally prioritizes deterministic startup latency over environments that require a system HTTP proxy. Proxy selection is not a current UI/configuration feature.
- `CURRENT.md` is a living snapshot and normally stays below approximately 400 lines. It is not append-only. Remove obsolete context only after it is present in the monthly journal or Git history.

## Next Likely Work

- Implement `client-ui-redesign` Stage 3 only after reviewing the canonical active plan and its local preview gate. Stage 4 follows only after Stage 3 owner acceptance.
- For any new meaningful implementation task, update this handoff if current context changes and append one concise entry to the current monthly journal in the same commit.

## Canonical References

- [`AGENTS.md`](../../AGENTS.md) — rules, contracts, security, build and commit requirements
- [`docs/architecture.md`](../architecture.md) — architecture and dependency graph
- [`docs/api.md`](../api.md) — API client and models
- [`docs/services.md`](../services.md) — service responsibilities
- [`docs/storage.md`](../storage.md) — state and backup storage
- [`docs/plans/README.md`](../plans/README.md) — plan lifecycle registry
- [`docs/releases/README.md`](../releases/README.md) — release-note and RC process
- [`history/2026-08.md`](history/2026-08.md) — recent engineering journal
