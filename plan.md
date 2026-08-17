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
- **Latest SHA:** (see git log)
- **Automated tests:** 336
- **Normal CI:** PENDING
- **Windows E2E:** PENDING

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

### v12.5 — Release readiness
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

---

## Історичні плани

Детальний план Stage 1–12.2: [docs/plans/archive/initial-implementation-plan.md](docs/plans/archive/initial-implementation-plan.md)

Історія змін: Git commits.
