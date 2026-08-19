# BDO UA Client — Active Plan

## Accepted baseline (before E2E iteration)

- **Accepted through:** v12.2
- **SHA:** `5d79f567f8b1937a53ae68173021d89f0c813c5d`
- **Automated tests:** 300

## Current implementation

- **v12.3 / v12.3.1** — mode selection, game detection UX, installed marker fixes
- **v12.3.3** — parallel startup (API + local detection), API error mapping
- **v12.3.3.1** — StartupCoordinator extraction, ApiErrorPresentation, 329 tests
- **v12.3.3.2** — finalize startup game state safely, deterministic tests
- **v12.3.4 / v12.3.4.1 / v12.3.4.2** — status readability, neutral color reset, README screenshots
- **v12.3.5 / v12.3.5.1** — Marquee progress, timing logs, application icon
- **v12.4** — public root README, release candidate workflow, release docs
- **v12.4.1** — finalize release candidate workflow (version properties, remote tag check, step summary)
- **v12.4.2** — immutable release contract (strict version regex, schema_version, tag-last, release docs)
- **v12.4.3** — fix release candidate + automate patch version (ZIP fix, optional version, auto-increment)
- **v12.4.3.3** — fix resolver test isolation (RESOLVER_TEST_TAGS_JSON, real-origin smoke, 13 scenarios)
- **v12.4.4** — network diagnostics and log retention (API timing, download timing, 15-day log retention, startup version, release-assets artifact)
- **v12.4.4.1** — fix diagnostic correctness (retention boundary, real elapsed timing in catches, stable error categories, UTF-8 byte count)
- **v12.4.5** — live refresh release feed (15s polling, semantic change detection, last-known-good, correlation headers, CheckedChanged suppression)
- **v12.4.5.1** — harden live feed lifecycle (accepted-baseline semantics, pending feed, pause/resume, FormClosing fix, mode ordering, download correlation)
- **v12.4.5.2** — serialize live feed application (AcceptFeed after RefreshState, single-flight, operation finalization order)
- **v12.4.5.3** — close live feed finalization races (_operationFinalizing gate, no fire-and-forget, exception-safe finalization, network diagnostics, FeedApplicationCoordinator)
- **v12.4.5.4** — unify feed application coordinator (MainForm uses coordinator, semantic equality, stale-pending fix, Task-based API, whole-pipeline callback)
- **v12.4.5.5** — preserve newer pending feed on failure (failed candidate does not overwrite newer pending, exactly-once acceptance test)
- **v12.4.6** — fix compiler warnings + release notes archive (`docs/releases/`, `AGENTS.md` §40, removed obsolete `_operationFinalizing`)
- **v12.4.7** — auto-generate release notes from git log (`scripts/Generate-ReleaseNotes.ps1`)
- **v12.4.8** — rename Release Build to Test Build (`.github/workflows/test-build.yml`, artifact: `BDO-UA-Client-test-build`)
- **v12.4.9** — Test Build auto-triggers on push/PR (not just `workflow_dispatch`)
- **v12.4.10** — Ukrainian release notes auto-generation (Ukrainian description mapping in `Generate-ReleaseNotes.ps1`)
- **v12.4.11** — add connection warmup at startup
- **Published GitHub Releases:** v0.1.2, v0.1.3 (both stable, both with assets)
- **.NET tests:** 404
- **Resolver scenarios:** 13
- **Normal CI:** active (push/PR trigger)
- **Test Build:** active (push/PR/workflow_dispatch trigger)

## Поточна фаза

**Stage 13 — Application self-update + utility footer (v13.1 IMPLEMENTED)**

### v12.3 / v12.3.1 — fix mode selection and game detection UX

**Status:** IMPLEMENTED — E2E PENDING

**E2E findings (confirmed in real Windows test):**
- Multiple localization RadioButtons could be selected simultaneously (each had separate parent FlowLayoutPanel)
- Ambiguous selection could install the wrong mode
- Cross-mode Install could remain disabled
- Game detection lacked persistent success confirmation
- Manual folder chooser accepted only exact game root
- Exact installed release had disabled Install without clear explanation
- Installed/selected concepts needed clearer UI labels

**Fixes:**
- RadioButtons now share same immediate parent (modesFlowPanel) — native WinForms mutual exclusion
- GetSelectedModeSlug hardened: returns null if ambiguous (multiple checked)
- Installed marker: "✓ Встановлено" on matching RadioButton
- Exact-installed message: "Цей реліз уже встановлено."
- Game status UI: persistent "✓ Гру знайдено" with green color
- Manual path resolution: accepts parent folder, resolves unique child
- Ambiguous multiple roots → rejected with message
- Invalid manual pick preserves existing valid game root
- Labels: "Встановлено:" / "Обрано:" for clarity
- Progress reset on mode change

**Acceptance:**
- [ ] Exactly one localization mode selected at all times
- [ ] Ambiguous selection cannot trigger install
- [ ] Installed A + selected B → Install enabled
- [ ] Exact installed target → Install disabled + explicit explanation
- [ ] Installed release marker visible
- [ ] Persistent game-found status visible
- [ ] Manual exact root works
- [ ] Manual unique immediate child root works
- [ ] Ambiguous multiple roots rejected
- [ ] Invalid manual pick preserves current valid game root
- [ ] Restore Original independent from selected mode
- [ ] Automated tests green
- [ ] Normal CI green
- [ ] Next Windows E2E pending

### v12.3.3 — decouple startup detection from API loading

**Status:** IMPLEMENTED — E2E PENDING

**Problem:** Serial startup blocked game detection on API completion. When API hung/stalled, entire app appeared frozen with "Завантаження даних..." for 30+ seconds.

**Fix:**
- API request and local game detection run in parallel
- Local detection result (SavedConfig/Registry/Steam) shown immediately
- API patterns used as fallback if local detection fails
- Mode loading placeholder: "Завантаження доступних режимів..."
- API failure placeholder: "Не вдалося завантажити режими."
- Zero modes: "Наразі немає доступних режимів."
- API error messages mapped to Ukrainian via `ApiErrorPresentation`
- `StartupCoordinator` returns factual final game outcome (`StartupCoordinatorResult`)
- Final game status applied after coordinator completes (no stale transitional text)
- API-pattern fallback UX: "Пошук гри за даними сервера..."
- Deterministic TCS-based tests (no `Task.Delay` ordering)
- 335 total tests

### v12.3.4 — improve status readability

**Status:** IMPLEMENTED — VISUAL E2E PENDING

**Real E2E finding:** Status installed/selected information was functionally correct but insufficiently readable. GrayText color and small font made status labels hard to read. "Актуальна" was contextless when installed/selected modes differed.

**Fix:**
- `localizationStateLabel`: Segoe UI 10pt Bold, `ControlText` (green for UpToDate, DarkRed for Corrupted)
- `installedInfoLabel` / `detailsLabel`: Segoe UI 9.5pt, `ControlText` (was GrayText)
- UpToDate wording: "✓ Встановлена локалізація актуальна" (was "Актуальна")
- All state wording reviewed for contextual clarity
- Separators: " • " (was " | ")
- `ApplyLocalizationStatePresentation` helper sets text + color atomically

### v12.3.5 — startup loading responsiveness

**Status:** IMPLEMENTED — E2E PENDING

**Real E2E finding:** First HttpClient request to bdo-ua.com.ua was observed to take ~21 seconds on the test Windows machine. The exact low-level cause was not conclusively established. Subsequent requests: ~70ms.

**Fix:**
- Marquee progress bar for indeterminate states (LoadingApi, DetectingGame, Verifying, etc.)
- Stopwatch-based timing logs for API HTTP GET and total startup coordinator
- Shared HttpClient, single GET request, bounded timeout, no security bypasses

**v12.3.5.1 — harden startup networking and add application icon**

**Status:** IMPLEMENTED — E2E PENDING

**Fix:**
- Removed sequential HEAD warmup → single GET to /api/public/v1/releases
- Corrected root-cause wording (not conclusively established)
- Application icon: `assets/bdo-ua-icon.ico` (6 sizes: 16-256) embedded via `<ApplicationIcon>`
- HTTP test: `GetReleasesAsync_SingleGetRequest_CorrectUrl` verifies exactly one GET request

## Дорожня карта

### v12.3.x — v12.4.x (IMPLEMENTED)

Див. історичні коміти та `docs/plans/archive/`.

### v12.5 — Release readiness (DEFERRED)
- Remaining runtime edge cases
- Clean Windows/self-contained validation
- Final user-facing messages/UX
- No known critical/major E2E defects

---

## Stage 13 — Application self-update + utility footer

### Product contract

Stage 13 додає автоматичну перевірку оновлень самого BDO UA Client та utility footer.

**Final UX:**

1. Програма запускається нормально.
2. Перевірка GitHub update відбувається автоматично у background.
3. Update check не блокує startup, не затримує game detection, не затримує bdo-ua.com.ua API, не робить UI unresponsive.
4. Якщо update немає — користувачу нічого intrusive не показувати.
5. Якщо GitHub недоступний — нормальна робота продовжується; warning у log; без modal error.
6. Якщо доступна нова версія — внизу справа з'являється кнопка "Оновити до vX.Y.Z".
7. Натискання кнопки = explicit user consent. Без додаткового confirmation dialog.
8. Після кліку: download → progress → verification → app exits → replace EXE → restart.
9. НІКОЛИ не встановлювати update silent/forced без натискання кнопки.

### Footer UX

Поточний bottom actions row перетворюється на footer із двома зонами:

**Ліворуч** (зберігаються): `[Встановити]` `[Відновити оригінал]` `[Скасувати]`

**Праворуч:** `[Оновити до vX.Y.Z]` `vX.Y.Z` `[icon-only logs button]`

- existing localization actions залишаються зліва
- version label завжди видно
- update button hidden, поки update не знайдений
- log button завжди absolute far-right element
- footer нормально працює при resize/DPI

### Version display

`AppVersionInfo` — canonical version helper. Використовується для: startup logging, footer, update comparison. Не дублювати reflection/version parsing. UI показує `vX.Y.Z`. Якщо current version не можна надійно розпарсити — safe diagnostic representation; updater fail closed.

### Logs button

Icon-only кнопка в absolute bottom-right. Tooltip: "Відкрити папку журналів". AccessibleName задати. Відкриває Windows Explorer у `%LocalAppData%\BDO-UA-Client\logs` (з `AppPaths.LogsDir`). UseShellExecute=true. При failure: log + non-destructive message.

### GitHub update source

- Canonical source: GitHub repository `merelyigor/bdo-ua-client`, public GitHub Releases
- Не використовувати bdo-ua.com.ua як updater backend
- Не використовувати GitHub token
- Використовувати GitHub REST `GET /repos/{owner}/{repo}/releases` (List releases)
- НЕ `/releases/latest` (для підтримки prerelease channel)
- Public unauthenticated requests; explicit User-Agent; `Accept: application/vnd.github+json`
- Current recommended API version: `X-GitHub-Api-Version: 2022-11-28` (перевірити official docs перед v13.1)
- `per_page=100`
- Максимум один запит при startup у першій реалізації
- BCL HttpClient + System.Text.Json достатньо; не додавати Octokit

### GitHub Release models

Моделювати тільки необхідні поля:

```
GitHubRelease { tag_name, draft, prerelease, published_at, assets[] }
GitHubReleaseAsset { name, browser_download_url, size, state, digest? }
```

Парсер повинен толерувати extra GitHub fields.

### Version / channel policy

Strict public tag: `^vMAJOR.MINOR.PATCH$`. Порівняння ТІЛЬКИ numeric (0.1.9 < 0.1.10, ніколи lexicographic).

**Current release determination:**
1. Отримати numeric X.Y.Z з current executable
2. Знайти release з `tag_name == vX.Y.Z` у published releases
3. Якщо current version НЕ відповідає published release → updater disabled; log diagnostic

**Channel policy:**
- Current release `prerelease=true` → дозволити newer prerelease + newer stable
- Current release `prerelease=false` → дозволити тільки newer stable; newer prerelease ігнорувати
- Ніякого Settings UI для update channels у Stage 13

### Release selection

Розглядати тільки releases: `draft == false`, published, strict valid `vX.Y.Z` tag. Не довіряти API ordering. Розпарсити versions numerically. Обрати highest eligible version > current version. Якщо highest eligible release malformed/incomplete → fail closed; не перескакувати на older release.

### Required release assets

Updater-enabled release повинен містити:
- `release-manifest.json`
- production ZIP (який manifest визначає через `asset_name`)

Canonical checksum source: `release-manifest.json.sha256` (asset у release).

Download URL брати з exact `browser_download_url` asset object від GitHub API. НЕ довіряти URL із manifest.

### Manifest contract

Schema 1 canonical. Validate: `schema_version == 1`, `version == X.Y.Z`, `tag == vX.Y.Z`, `platform == "win-x64"`, `asset_name` non-empty, `sha256` valid hex. Не змінювати schema для EXE hash (EXE hash локально обчислюється після extraction).

### Optional GitHub asset digest

Якщо GitHub API asset object повертає `digest=sha256:...` — використати як ADDITIONAL cross-check. Canonical checksum залишається `release-manifest.json.sha256`. Відсутність digest не ламає update. Mismatch → fail closed.

### Download safety

- HTTPS only; no TLS bypass; no custom cert
- CancellationToken; bounded timeout; bounded safe retries for idempotent GET
- Streaming download; no unbounded memory buffering
- Manifest: small bounded size
- ZIP: max size ~200 MB (запас для self-contained binary)
- Content-Length cross-check when available
- Temp cleanup after failure/cancel

### ZIP validation

Приймається тільки якщо: valid archive, рівно 1 file entry, root-level, exact name `BDO-UA-Client.exe`, no directories, no path traversal, no absolute path, no `../`, non-zero, sane bounded uncompressed size. Будь-яка аномалія → fail closed.

### Extracted EXE validation

Після extraction: файл існує, non-zero, FileVersion/ProductVersion відповідає target X.Y.Z, локально обчислити candidate EXE SHA-256. Не запускати candidate до завершення validation.

### Staging / session

Додати `%LocalAppData%\BDO-UA-Client\updates` через `AppPaths.UpdatesDir`.

```
updates/<GUID>/BDO-UA-Client.exe
updates/<GUID>/update-session.json
```

Session ID: GUID. Internal updater CLI: `--apply-update <session-id>`. Helper по session-id читає trusted session file з UpdatesDir. Session JSON писати atomic.

### Self-update architecture

НЕ додавати: permanent Updater.exe, PowerShell/BAT updater, Windows Service, Scheduled Task, MSI.

`BDO-UA-Client.exe --apply-update <GUID>` → updater apply path. MainForm НЕ створюється. Program startup чітко розділяє normal mode vs updater mode.

### Target EXE

Target path = фактичний current process executable path. Не hardcode. Перевірити: absolute normalized path, target exists, target directory write capability ДО handoff.

### Safe replace

Після verification у LocalAppData → підготувати verified candidate у target directory поруч із current EXE як unique temporary sibling. Мета: перевірити write access ДО handoff; candidate і target на одному volume.

1. Launch staged NEW EXE: `--apply-update <session-id>`
2. Old app exits
3. Updater waits old parent PID (bounded timeout)
4. Updater verify: session, target, candidate hash
5. Perform safe replace (дослідити `File.Replace` на Windows)
6. Restart new EXE

### Backup / rollback

Updater НЕ має відношення до game localization backups. Використовує власний temporary backup/session state.

- Перед replace: current target має мати recoverable backup
- Replacement failed → current version restored
- Post-replace verification failed → rollback
- Restart failed → rollback + attempt restart old
- НІКОЛИ: "old deleted, new not installed"

### Parent process / handoff

Session зберігає parent PID. Updater: bounded wait на parent. Якщо timeout → abort; не replace; cleanup; log.

### Success cleanup

Після successful replace/restart: cleanup old temporary backup/staging best-effort; logs success; version label = target version. Не видаляти recovery data до підтвердження replacement.

Abandoned sessions: cleanup за bounded retention policy (наприклад, 7 днів).

### Directory not writable / UAC

Stage 13: NO automatic elevation, NO UAC helper, NO runas. Якщо target directory not writable → current EXE untouched; показати українське повідомлення; пояснити manual update або переміщення до writable folder.

### Operation lifecycle

Application update НЕ працює одночасно з: localization Install, Restore Original, rollback, іншим application update. Якщо localization operation active → update button disabled. Якщо application update active → Install disabled, Restore Original disabled. Existing `ReleaseFeedPoller` / `FeedApplicationCoordinator` не ламати. Перед handoff: safely pause/stop background feed activity.

### Cancel behavior

До destructive handoff: Cancel дозволений (manifest fetch, download, staging). Після handoff: non-cancellable. Не cancel посеред File.Replace/rollback.

### Progress UX

Використовувати existing progress UI: "Завантаження оновлення vX.Y.Z... 42%", "Підготовка оновлення... Програма буде перезапущена." Startup background check НЕ показує intrusive spinner.

### Error UX

- Startup check failure → log only; normal app continues
- Download failure → visible message; retry later; app remains usable
- Manifest/SHA/ZIP validation failure → integrity error; current EXE untouched
- Apply failure → rollback; clear error log; preserve old version
- Не показувати raw stack traces

### Logging

Log: current version, update check result, selected channel, candidate version, no-update reason, manifest validation, download timing/bytes, SHA verification, staging, handoff session, replacement result, rollback, restart, cleanup. Не логувати: response bodies unnecessarily, secrets. Reuse existing ILogger.

### Security non-goals / hard bans

Stage 13 НЕ робить: silent forced update, GitHub token, custom backend, arbitrary manifest/executable URL, HTTP, disabled TLS, PowerShell/BAT updater, permanent Updater EXE, Windows Service, Scheduled Task, MSI, automatic UAC, delta updates, WebSocket/SSE, GitHub polling every 15 sec, update channel settings UI, third-party updater frameworks, unnecessary NuGet dependencies.

### Architecture — keep it small

```
AppVersionInfo              — current numeric/display/full version
GitHubUpdateClient          — GitHub REST releases + manifest/asset fetch
UpdateSelectionPolicy       — current release/channel; numeric selection
UpdatePackageService        — download; SHA; ZIP; EXE validation; staging
UpdateSession / Store       — atomic session state
SelfUpdateApplier           — parent wait; verify; replace; rollback; restart
MainForm                    — presentation/orchestration only
Program                     — detect internal updater mode vs normal UI
```

Не створювати interface для кожного класу. Не створювати DI container. KISS.

---

### v13.0 — document client self-update architecture

**Status:** IMPLEMENTED (commit 8319973)

**Goal:** Documentation/contracts only. Reconcile plan with repo state. Full Stage 13 plan. AGENTS §41 critical invariants.

**Scope:** plan.md, AGENTS.md. No production code.

---

### v13.1 — add update discovery and utility footer

**Status:** IMPLEMENTED

**Goal:** User can launch app, see version in footer, see update button when newer GitHub Release exists.

**Scope:**
- `AppVersionInfo` — helper for current numeric/display/full version (extract from `AssemblyInformationalVersionAttribute`)
- Footer layout: left (existing actions), right (update button + version label + logs button)
- Version label: `vX.Y.Z` always visible, Segoe UI 9pt, right-aligned
- Update button: hidden by default, visible when update found, text "Оновити до vX.Y.Z"
- Logs button: icon-only, absolute bottom-right, opens Explorer at `AppPaths.LogsDir`, tooltip + AccessibleName
- `GitHubUpdateClient` — `HttpClient` + `System.Text.Json`, `GET /repos/merelyigor/bdo-ua-client/releases`, per_page=100, explicit User-Agent, Accept header
- `GitHubRelease` / `GitHubReleaseAsset` models (tolerant of extra fields)
- `VersionParser` — strict `^v(\d+)\.(\d+)\.(\d+)$` tag parsing, numeric comparison
- `UpdateSelectionPolicy` — determine current published release; find highest eligible newer version; channel rules (stable/prerelease)
- One background startup check (non-blocking, fire-and-forget with try/catch)
- Button appears with "Оновити до vX.Y.Z" if eligible update found
- NO package download/apply yet

**Behavior contract:**
- Startup check runs after MainForm shown; does NOT block game detection or API loading
- GitHub failure → log warning; app continues normally
- Current version not found in published releases → updater disabled; log diagnostic
- Update button disabled during localization operations
- Version label shows safe diagnostic if version unparseable

**Security rules:**
- HTTPS only; no token; no auth; explicit User-Agent
- Tolerant JSON parsing (extra fields ignored)
- No arbitrary URL construction

**Automated tests:**
- VersionParser: 0.1.9 < 0.1.10, equal, malformed tag ignored, prerelease tag format
- UpdateSelectionPolicy: current not published → disabled; current prerelease → allow newer prerelease + stable; current stable → only newer stable; multiple releases → highest eligible; draft ignored; malformed ignored
- GitHubRelease model: valid deserialization, extra fields tolerated, null published_at
- GitHubUpdateClient: valid list, HTTP errors, malformed JSON, timeout, cancellation

**Acceptance:**
- [ ] `vX.Y.Z` visible in footer
- [ ] Logs button opens correct folder
- [ ] Update button hidden when no update
- [ ] Update button visible with correct text when update found
- [ ] GitHub failure doesn't block app
- [ ] Current not-published → no update offered
- [ ] All automated tests green

---

### v13.2 — add verified update package staging

**Goal:** New version can be downloaded, verified, staged — but current EXE is NOT replaced.

**Scope:**
- `UpdatePackageService` — download manifest → validate → download ZIP → SHA-256 → validate ZIP → extract → validate EXE → stage
- Manifest download from release asset (`release-manifest.json`); bounded size; parse JSON
- Manifest validation: `schema_version == 1`, `version` matches selected release, `tag` matches, `platform == "win-x64"`, `asset_name` non-empty, `sha256` valid hex
- ZIP asset selection: find asset by `asset_name` from manifest in GitHub release assets; use `browser_download_url`
- Streamed ZIP download with progress; CancellationToken; timeout; bounded retries
- Size validation: Content-Length cross-check when available; max ~200 MB
- SHA-256 verification of downloaded ZIP against manifest `sha256`
- Optional: cross-check against GitHub asset `digest` field (if present; `sha256:...` format)
- ZIP validation: exactly 1 entry, root-level `BDO-UA-Client.exe`, no directories/traversal, non-zero
- Extraction to temp staging dir
- EXE version validation: FileVersion/ProductVersion matches target X.Y.Z
- Local EXE SHA-256 computation; stored in session
- `UpdateSession` + `UpdateSessionStore`: GUID session, atomic JSON write in `AppPaths.UpdatesDir`
- Progress UX: "Завантаження оновлення vX.Y.Z... XX%", "Перевірка...", "Підготовка..."
- NO actual replacement/restart yet

**Behavior contract:**
- User clicks "Оновити до vX.Y.Z" → download begins
- Cancel allowed until staging complete
- Any validation failure → current EXE untouched; staging cleaned up; user-visible error
- Download failure → retry option; current app usable
- Manifest/asset mismatch → fail closed; no partial install

**Security rules:**
- HTTPS only; no TLS bypass
- Streaming download; bounded memory
- Path traversal check on ZIP entries
- Don't trust manifest URLs for download (use GitHub API asset URL)
- Don't run extracted EXE before full validation

**Automated tests:**
- Manifest: valid, wrong schema, version mismatch, tag mismatch, platform mismatch, invalid SHA, missing manifest, missing ZIP, wrong asset_name
- Download: correct SHA, wrong SHA, size mismatch, timeout, cancellation, cleanup
- ZIP: exactly one correct root EXE, extra entry, directory, traversal, absolute/invalid name, empty EXE
- EXE: target version match/mismatch
- Session: valid GUID, malformed JSON, wrong session id, missing staged file

**Acceptance:**
- [ ] Click update button → download starts with progress
- [ ] Manifest validated before ZIP download
- [ ] SHA-256 verified after download
- [ ] ZIP structure validated
- [ ] EXE version validated
- [ ] Session created in UpdatesDir
- [ ] Cancel cleans up staging
- [ ] Validation failure doesn't touch current EXE
- [ ] All automated tests green

---

### v13.3 — add self-update replacement and restart

**Goal:** Technical self-update works end-to-end: download → verify → stage → exit → replace → restart.

**Scope:**
- `Program.cs`: detect `--apply-update <session-id>` → updater mode (skip MainForm entirely)
- `SelfUpdateApplier`: read session, verify staged candidate, wait parent PID, perform replace, restart
- Writable target validation before handoff
- Sibling verified candidate: copy staged EXE to target dir as unique temp sibling name
- Parent PID wait: bounded timeout (e.g., 30s); poll Process.GetProcessById
- Safe replace: research `File.Replace` semantics on Windows; if not suitable, use rename-old → move-new → verify pattern
- Backup: keep old EXE as temp backup in target dir (unique name)
- Post-replace verification: target exists, non-zero, version metadata correct
- Rollback: if replace fails → restore backup; if verification fails → restore backup; if restart fails → attempt restore + restart old
- Restart new EXE: `Process.Start(targetPath)`; old-exit → new-start sequencing
- Success: log; cleanup best-effort
- Update session state: mark completed after successful restart verification

**Behavior contract:**
- Handoff: old app saves session, launches staged EXE with `--apply-update <GUID>`, old app exits
- Updater: waits for parent exit, verifies session, performs replace, launches new, exits
- If parent doesn't exit in time → abort; cleanup; don't replace
- If replace fails → rollback; log; user can still use old version
- If new EXE can't start → rollback + attempt restart old

**Security rules:**
- Session read only from trusted `AppPaths.UpdatesDir/<GUID>/`
- Don't accept arbitrary paths via CLI args
- Verify staged candidate hash matches session before replace
- Don't launch arbitrary executables

**Automated tests:**
- Session: valid, malformed, wrong id, missing file, hash mismatch
- Replace: success, backup exists, failure before replace leaves target, replace failure rollback, verification failure rollback
- Parent: already exited (immediate), wait timeout (abort)
- Restart: success, failure recovery
- ALL tests use temp directories/files ONLY

**Acceptance:**
- [ ] `--apply-update <GUID>` starts updater mode
- [ ] Old app exits cleanly
- [ ] New EXE replaces old
- [ ] New EXE starts with correct version
- [ ] Rollback works on replace failure
- [ ] Rollback works on verification failure
- [ ] Parent timeout aborts safely
- [ ] All automated tests green

---

### v13.4 — harden updater lifecycle and UX

**Goal:** Connect everything; close all edge cases; production-ready.

**Scope:**
- Full lifecycle: startup check → button → click → download → verify → stage → handoff → replace → restart
- Install/Restore exclusion: update button disabled during localization operations; localization buttons disabled during update
- Update cancellation before handoff restores usable UI
- FeedPoller interaction: pause before handoff; resume on cancel
- FormClosing during update: warn or block
- Duplicate click protection: max one update operation
- Error handling: all failure paths restore UI to usable state
- Unwritable path UX: clear Ukrainian message; suggest manual update
- Abandoned session cleanup: bounded retention (7 days); cleanup on startup
- Success cleanup: remove old backup/staging after confirmed restart
- Comprehensive logging through all paths
- User messages: Ukrainian, non-technical, actionable

**Automated tests:**
- Lifecycle: button hidden → visible → click → progress → complete
- Exclusion: update disabled during localization op; localization disabled during update
- Cancel: before handoff restores UI; after handoff non-cancellable
- Duplicate click: ignored
- FeedPoller: paused during update; resumed on cancel
- Unwritable target: error message; no destructive action
- Abandoned sessions: cleanup by age
- ALL tests use temp directories/files ONLY

**Acceptance:**
- [ ] Full E2E happy path works with real GitHub Release (manual test)
- [ ] All edge cases handled
- [ ] UI always returns to usable state
- [ ] No orphaned temp files after success
- [ ] Logs comprehensive
- [ ] All automated tests green

---

### v13.5 — real published-release E2E

**Goal:** Verify self-update with actual published GitHub Releases.

**Scope:** E2E verification, not artificial code stage. No commit needed if no defects found.

**Flow:**
1. Launch old updater-enabled published release
2. Automatic GitHub check
3. Update button appears
4. Click once
5. Real GitHub asset download
6. Verify
7. App exits
8. Self replace
9. Restart
10. Visible version is new version

**Verify persistence:**
- config.json, installation.json, game path, selected mode, installed localization state, backups, logs

**Verify cleanup:**
- No stale update session
- No unwanted .tmp/.new/.bak leftovers after confirmed success
- Old application remains recoverable on simulated/apply failures via automated tests

**If defect found:** targeted v13.5.1 fix.

---

## Production release process

**Production GitHub Releases are always manually published by the repository owner.**

### A. Normal CI
- Automatic push/PR verification
- Development safety check

### B. Test Build
- Automatic on push/PR to main + manual `workflow_dispatch`
- Generic unversioned testing artifact (`BDO-UA-Client-test-build`)
- NOT a public GitHub Release

### C. Release Candidate
- NEW manual `workflow_dispatch`
- User enters version (e.g. `0.1.0`)
- Builds exact current main
- Tests
- Produces versioned release package (`BDO-UA-Client-vX.Y.Z-win-x64.zip`)
- Creates version tag on exact verified SHA
- Uploads release-candidate artifact
- Does NOT create/publish GitHub Release

### D. Final publication
- Manual GitHub UI action by repository owner
- Select existing tag
- Paste/edit release notes
- Upload the exact ZIP produced by Release Candidate workflow
- Click Publish release

---

### v12.4.4 — network diagnostics and log retention

**Status:** IMPLEMENTED

**E2E v0.1.0 findings:**
- v0.1.0 Release Candidate functional success (ZIP, EXE, SHA, manifest all OK)
- Intermittent ~21s bdo-ua.com.ua network latency observed (API + download)
- Examples: 61ms, 70ms, 471ms, 21164ms, 21262ms, 21282ms
- Root cause NOT established (could be DNS/TCP/TLS/proxy/IPv6/Cloudflare/server)
- No speculative network fixes — instrument first

**Changes:**
- API timing: headers_ms, body_ms, parse_ms, total_ms, bytes, HTTP version
- Release download timing: headers_ms, body_ms, total_ms, bytes, throughput, error timing
- Official download timing: same fields
- Failure timing: elapsed_ms on timeout/network/error
- Startup: logs assembly version from metadata
- Log retention: 15 calendar days (today + 14 previous), cleanup on init, best-effort
- Actions artifact renamed: `BDO-UA-Client-vX.Y.Z-release-assets` (CI transport container)
- Production user package: one ZIP with one EXE (unchanged)

**v0.1.0 tag:** immutable, not published as GitHub Release
**Next RC expected:** v0.1.1 (automatic patch)

---

### v12.4.5 — live refresh release feed

**Status:** IMPLEMENTED

**Requirement:** While app is open, new releases/modes should appear without restart.

**Design:**
- `ReleaseFeedPoller`: async loop, 15s after previous completion, CancellationToken
- `FeedChangeDetector`: static comparison of UI-relevant fields (slugs, public_id, version, patch, compatibility, public_name, official_patch)
- `MainForm`: `_suppressModeChanged` flag for programmatic rebuild, `_operationInProgress` guard
- Last-known-good: background failure keeps existing feed, no UI disruption
- Startup failure recovery: poller retries, UI recovers on first success
- Correlation headers: X-Request-ID, Server-Timing, CF-Ray logged when present

**Behavior:**
- Unchanged feed → no RadioButton rebuild, no config churn
- New mode appears → mode list updated, previous selection preserved
- Mode removed → deterministic fallback via DynamicModePolicy
- Newer current release → feed updated, state refresh triggered (UpToDate → UpdateAvailable)
- Operation in progress → poller paused, candidate stored as pending, applied after operation
- Shutdown → poller cancelled, no errors

---

### v12.4.5.1 — harden live feed refresh lifecycle

**Status:** IMPLEMENTED

**Fixes:**
- Accepted-baseline semantics: poller does NOT advance snapshot until `AcceptFeed()` called by consumer
- Pending feed: candidates during operation stored in `_pendingFeed`, applied after operation finishes
- Pause/Resume: `_poller.Pause()` during operations stops new HTTP requests; `_poller.Resume()` after
- FormClosing: only stop poller when close actually proceeds; cancelled close preserves polling
- Startup-close race: `_closing` flag prevents poller start after shutdown begun
- Mode ordering: FeedChangeDetector compares ordered slug sequences (A,B,C → C,A,B is change)
- Malformed slugs: null-coalescing on slug comparisons, no dictionary exceptions
- Async RefreshState: `ApplyFeedUpdate` is async void with try/catch, no unobserved tasks
- Download correlation headers: X-Request-ID, Server-Timing, CF-Ray logged on release + official downloads
- Poller lifecycle: Start/Stop/Pause/Resume/Dispose/IsRunning semantically consistent

---

## Історичні плани

Детальний план Stage 1–12.2: [docs/plans/archive/initial-implementation-plan.md](docs/plans/archive/initial-implementation-plan.md)

Історія змін: Git commits.
