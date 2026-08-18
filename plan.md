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
- **Latest SHA:** (see git log)
- **.NET tests:** 399
- **Resolver scenarios:** 13
- **Normal CI:** PENDING
- **Release Candidate:** v0.1.0 functional success (intermittent ~21s bdo-ua.com.ua latency observed, root cause not established)

## Поточна фаза

**Stage 12 — Real Windows E2E stabilization**

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

### v12.3.x — E2E bugfix iterations
Only if next real Windows test finds concrete defects.
Target scenarios:
- Startup detection
- Dynamic API modes
- Install first localization
- Restart persistence
- Switch mode A → B
- Same-mode newer release if naturally available
- Restore Original
- Reinstall after Restore Original
- Cancellation safety where practically testable
- Logs
- Single-file startup

### v12.4 — prepare first public release candidate

**Status:** IMPLEMENTED — PENDING

**Target:**
- Public root README.md (Ukrainian product guide with screenshots)
- Release notes template
- Manual Release Candidate workflow (workflow_dispatch, versioned build+test+package+tag)
- Real packaged E2E after workflow acceptance
- Manual GitHub Release publication by repository owner

### v12.5 — Release readiness
Target:
- Remaining runtime edge cases
- Clean Windows/self-contained validation
- Final user-facing messages/UX
- No known critical/major E2E defects

### v1.0.0
First public release after v12.4-v12.5 acceptance.

### Future: Client auto-update
Target (post v1.0.0 stabilization):
- Application reads own version from assembly metadata
- Startup checks latest published client release metadata (release-manifest.json)
- Semantic version comparison (numeric, not lexicographic)
- User-confirmed update: "Доступна нова версія BDO UA Client X.Y.Z" → "Оновити зараз" / "Пізніше"
- Download to temp → SHA-256 verify → validate version → close → replace → restart
- Preserve rollback/recovery path
- No silent forced auto-update as first implementation
- Future GitHub Releases should include ZIP + SHA256SUMS.txt + release-manifest.json

---

## Production release process

**Production GitHub Releases are always manually published by the repository owner.**

### A. Normal CI
- Automatic push/PR verification
- Development safety check

### B. Release Build
- Existing manual `workflow_dispatch`
- Generic unversioned E2E/testing artifact
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
