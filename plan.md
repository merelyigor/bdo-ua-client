# План реалізації bdo-ua-client

## Техстек

| Компонент | Вибір | Обґрунтування |
|---|---|---|
| Мова | C# 12 / .NET 8 | Нативний Windows .exe, WinForms |
| UI | WinForms | Простіше для .exe, менше залежностей |
| HTTP | `HttpClient` (built-in) | Стандартна бібліотека |
| Serialization | `System.Text.Json` | Вбудований, швидкий |
| Hash | `System.Security.Cryptography` | SHA-256 вбудований |
| Tests | xUnit (test-only NuGet dependency) | Стандартний test framework для .NET |
| Side dependencies | **Немає** | Все з .NET 8 SDK (runtime/production) |

**TargetFramework:** `net8.0-windows` з `UseWindowsForms=true` для обох проектів.

**Solution:** `BdoUaClient.sln` містить `BdoClient.csproj` + `BdoClient.Tests/BdoClient.Tests.csproj`.

**Команди перевірки:**
- `dotnet build BdoUaClient.sln`
- `dotnet test BdoUaClient.sln --no-build`

---

## Архітектура (source code)

```
bdo-ua-client/
├── BdoUaClient.sln
│
├── BdoClient.csproj          (net8.0-windows, UseWindowsForms=true)
├── Program.cs
├── MainForm.cs
├── MainForm.Designer.cs
│
├── Api/
│   ├── BdoUaApiClient.cs
│   └── ApiResult.cs
│
├── Models/
│   ├── ReleasesResponse.cs
│   ├── ReleaseData.cs
│   ├── LocalizationMode.cs
│   ├── CurrentRelease.cs
│   ├── ReleaseHistoryItem.cs
│   ├── InstallPathPattern.cs
│   └── GameTestInfo.cs
│
├── Services/
│   ├── GameDetector.cs
│   ├── LocalizationInstaller.cs
│   └── LocalizationStateService.cs
│
├── Storage/
│   ├── AppPaths.cs
│   ├── ConfigStore.cs
│   └── InstallationStateStore.cs
│
├── Logging/
│   └── ILogger.cs (мінімальний contract)
│
├── app.manifest
│
└── BdoClient.Tests/          (net8.0-windows, xUnit)
    ├── BdoClient.Tests.csproj
    ├── Api/
    │   └── BdoUaApiClientTests.cs
    ├── Models/
    │   └── LocalizationModeTests.cs
    ├── Services/
    │   ├── LocalizationInstallerTests.cs
    │   ├── LocalizationStateServiceTests.cs
    │   └── GameDetectorTests.cs
    └── Storage/
        └── ConfigStoreTests.cs
```

## Архітектура (runtime data)

```
%LocalAppData%\BDO-UA-Client\
├── config.json
├── state\
│   └── installation.json
├── logs\
├── cache\
└── backups\
    ├── original\
    └── restore-points\
```

---

## Модель станів

### LocalizationState (постійний стан)

| Стан | Умова |
|---|---|
| `NotInstalled` | metadata відсутня |
| `UpToDate` | `installed.public_id == current.public_id` |
| `UpdateAvailable` | `installed.public_id != current.public_id` |
| `WaitingForRelease` | встановлено, але `current` відсутній |
| `InstalledVersionUnknown` | metadata нечитабельна |
| `Corrupted` | hash не збігається |

### OperationState (тимчасовий стан)

`Idle` / `DetectingGame` / `LoadingApi` / `Downloading` / `Verifying` / `BackingUp` / `Installing` / `Restoring` / `Completed` / `Failed` / `Cancelled`

### Installation Metadata

```json
{
  "mode_slug": "full-ukrainian",
  "public_id": "01KZFM8YZBEBYF9JYSACTR8XW9",
  "version": 2,
  "game_patch": 396,
  "sha256": "3b2fce...",
  "installed_at": "2026-08-13T15:30:00+03:00",
  "source": "api"
}
```

Для official restore:
```json
{
  "mode_slug": null,
  "public_id": null,
  "version": null,
  "game_patch": 396,
  "sha256": null,
  "installed_at": "2026-08-13T16:00:00+03:00",
  "source": "official"
}
```

---

## Етапи реалізації

### Етап 1: Project skeleton + API models + API client

**Що реалізовано:**
- `.csproj` з .NET 8, WinForms
- Всі моделі в `Models/` відповідно до API contract
- `BdoUaApiClient.cs` з `GetReleasesAsync()` → `ReleasesResponse`
- Обробка помилок: timeout, DNS, 4xx/5xx, malformed JSON, порожня відповідь

**Acceptance criteria:**
- [ ] `dotnet build` проходить без помилок
- [ ] Моделі містять всі поля з API (включно з `official_source_url`, `install_path_patterns`, `game_tested_at`)
- [ ] `LocalizationMode.Current` є nullable (`CurrentRelease?`). `current == null` — валідний бізнес-стан, не deserialization error
- [ ] API client повертає `ApiResult<T>` (власна проста обгортка). `null` НЕ використовується як generic signal failure
- [ ] Network error, timeout, DNS, 4xx/5xx, malformed JSON, empty response — все через `ApiResult<T>.Failure`
- [ ] SHA-256 НЕ використовується для official source (лише для release downloads)
- [ ] Async service methods приймають `CancellationToken` де це доречно
- [ ] Помилки не ковтаються (немає порожніх catch/pass)
- [ ] Мінімальний logger contract: можливість передавати/викликати logging без прив'язки до UI. Без DI container. Без `Microsoft.Extensions.*`. Без складної abstraction. Persistent file logging — на Етапі 11

**Файли:** `BdoClient.csproj`, `Models/*.cs`, `Api/BdoUaApiClient.cs`, `Api/ApiResult.cs`, `Logging/ILogger.cs`, `BdoClient.Tests/`

**Тести (v1.x):**
- [ ] JSON deserialization успішного response
- [ ] JSON deserialization з `current: null` (nullable)
- [ ] Malformed JSON → `ApiResult.Failure`
- [ ] Empty response → `ApiResult.Failure`

---

### Етап 2: Local paths + config + installation state

**Що реалізовано:**
- `AppPaths.cs` — `%LocalAppData%\BDO-UA-Client\` + піддиректорії
- `ConfigStore.cs` — читання/запис `config.json`
- `InstallationStateStore.cs` — читання/запис `state/installation.json`

**Acceptance criteria:**
- [ ] Директорії створюються автоматично при першому запуску
- [ ] `config.json` зберігає `game_path` та `last_mode`
- [ ] `installation.json` зберігає повну metadata
- [ ] Для official restore metadata: `source: "official"`, `public_id: null`
- [ ] При відсутності файлів — дефолтні значення (не виключення)

**Файли:** `Storage/AppPaths.cs`, `Storage/ConfigStore.cs`, `Storage/InstallationStateStore.cs`

**Тести (v2.x):**
- [ ] Config serialization/deserialization
- [ ] Installation metadata serialization/deserialization
- [ ] При відсутності файлів — дефолтні значення

---

### Етап 3: Game detection + validation

**Що реалізовано:**
- `GameDetector.cs` — automatic detection + path validation
- Detection order: saved config → registry → Steam → API patterns → NotFound
- Path validation: `{game_path}\ads\languagedata_en.loc`
- Manual path validation (без UI)
- Steam VDF/appmanifest parsing
- API `{drive}` pattern expansion

**Acceptance criteria:**
- [ ] Порядок: saved path → registry → Steam libraryfolders → appmanifest → API patterns → NotFound
- [ ] Steam: читає `libraryfolders.vdf`, знаходить `appmanifest_582660.acf`, витягує `installdir`
- [ ] API `install_path_patterns` — ТІЛЬКИ hints (перебір дисків)
- [ ] Validation: файл `{game_path}\ads\languagedata_en.loc` існує
- [ ] `ValidateAndSaveManualPathAsync()` — caller передає directory, validation + save
- [ ] FolderBrowserDialog — UI fallback, підключається на UI integration stage
- [ ] Знайдений шлях зберігається в `config.json` без втрати `last_mode`

**Файли:** `Services/GameDetector.cs`, `Services/DetectionResult.cs`

**Тести (v3.x):**
- [ ] Path validation: валідна директорія, missing file, Unicode, spaces
- [ ] Saved config: valid path → SavedConfig, invalid → skip
- [ ] Steam: libraryfolders, appmanifest, malformed VDF/ACF
- [ ] API patterns: `{drive}` expansion, malformed pattern skip
- [ ] Manual: valid → save, invalid → no save, last_mode preserved

---

### Етап 4: Download + SHA-256 verification

**Що реалізовано:**
- Download у `{cache}/{unique-tmp}`
- Перевірка HTTP status, `Content-Length` vs `size_bytes`
- SHA-256 для release downloads; для official source — без hash
- Retry: до 3 повторних спроб після первинної, максимум 4 HTTP attempts
- Exponential backoff: 1s, 2s, 4s
- Per-attempt timeout через linked CancellationTokenSource (не HttpClient.Timeout)
- Відновлення файлу гри при помилці після replace (rollback)
- `DownloadResult` typed result з `DownloadError` enum
- `DownloadProgress` через `IProgress<T>`
- Streaming SHA-256 через `IncrementalHash` (без буферизації всього файлу)
- `DownloadResult.IsRetryable` flag для відрізнення retryable/non-retryable помилок
- HTTP 4xx (крім 408) → non-retryable Http; HTTP 408/5xx → retryable Http
- Timeout/Network → retryable; IO/Cancellation → non-retryable
- Temp cleanup: видалення після dispose stream (try/finally)
- Cleanup logging: Warning при помилці видалення
- InvalidMetadata: повертає `DownloadResult.Failure` замість throw
- URL validation: `Uri.TryCreate` з HTTPS scheme check
- Official source: та сама retry політика що й release

**Acceptance criteria:**
- [x] `HttpClient` з timeout
- [x] Temporary: `%LocalAppData%\BDO-UA-Client\cache\{random}.tmp`
- [x] Retry: до 3 повторних спроб після первинної, максимум 4 HTTP attempts
- [x] Exponential backoff (1s, 2s, 4s)
- [x] Per-attempt timeout через linked CancellationTokenSource
- [x] Перевірка `Content-Length` vs `size_bytes` (якщо доступно)
- [x] SHA-256 для release files
- [x] Для official: без SHA-256 перевірки
- [x] При помилці — temp видаляється
- [x] Cleanup logging
- [x] InvalidMetadata: return result (no throw)
- [x] URL validation (Uri.TryCreate + HTTPS)
- [x] Official source: same retry policy
- [x] `IsRetryable` flag on result
- [x] Final error kind preserved after retry exhaustion

**Файли:** `Services/LocalizationInstaller.cs`, `Services/DownloadResult.cs`

**Тести (v4.x):**
- [x] SHA-256 verification: hash збігається
- [x] SHA-256 verification: hash не збігається → помилка
- [x] Size validation при наявності `size_bytes`
- [x] 404 → no retry, final Http error
- [x] 500 → retry then fail with Http
- [x] 408 → retry then fail with Http
- [x] Network error → retry then fail with Network
- [x] Internal timeout → retry then fail with Timeout
- [x] Caller cancellation → no retry, propagate
- [x] InvalidMetadata → no HTTP call
- [x] Temp cleanup on failure (HashMismatch/SizeMismatch/Network/Cancellation)
- [x] Temp file remains on success
- [x] Progress reporting
- [x] SHA-256 case-insensitive
- [x] Official source: success without SHA
- [x] Official source: 500 → retry then fail
- [x] Official source: network error → retry then fail
- [x] Official source: internal timeout → retry then fail
- [x] Official source: cancellation → no retry, propagate
- [x] Official source: 404 → no retry
- [x] Official source: empty/http/malformed URL → InvalidMetadata
- [x] Official source: temp cleanup on failure
- [x] DownloadProgress percentage calculation
- [x] DownloadResult types (Success/SuccessWithoutHash/Failure)

---

### Етап 5: Backup/snapshot/restore

**Що реалізовано:**
- Original snapshot: ОДИН РАЗ перед першою модифікацією (не перезаписувати)
- Restore points: створюються ПЕРЕД replace game file
- Restore original: download з `official_source_url`; локальний snapshot — fallback
- Immutable snapshot: size/hash validation на read
- Replace safety: temp file → atomic move → verification → recovery на failure
- Typed results: `RestoreResult` з `RestoreError` enum

**Acceptance criteria:**
- [x] Original snapshot: `%LocalAppData%\BDO-UA-Client\backups\original\`
- [x] Original snapshot НЕ перезаписується (навіть при зміні game file)
- [x] Metadata original snapshot: `created_at`, `game_patch` (nullable), `sha256` (локально), `size_bytes`
- [x] Локальний SHA-256 — НЕ checksum від API
- [x] Snapshot validation: size_bytes + SHA-256 при read
- [x] Corrupted snapshot → explicit error, NOT overwritten
- [x] Restore points: `%LocalAppData%\BDO-UA-Client\backups\restore-points\{unique-id}\`
- [x] Restore point створюється ПЕРЕД replace (pre-operation snapshot)
- [x] Кожен restore point: `languagedata_en.loc` + `metadata.json`
- [x] Restore original: download з `official_source_url` → restore point → replace → verify → metadata `source: "official"`
- [x] Fallback: snapshot.game_patch != null && currentOfficialPatch != null && match → allowed
- [x] Fallback forbidden: patch null, mismatch, corrupted snapshot
- [x] Replace safety: temp → move → verify → recovery on failure
- [x] Post-replace recovery from restore point

**Файли:** `Storage/BackupStore.cs`, `Models/BackupMetadata.cs`, `Models/RestoreResult.cs`, `Services/HashHelper.cs`, `Services/RestoreOriginalService.cs`

**Тести (v5.x):**
- [x] Original snapshot: first creation creates file + metadata
- [x] Original snapshot: SHA/size metadata matches snapshot
- [x] Original snapshot: second call does NOT overwrite
- [x] Original snapshot: source changed → original remains unchanged
- [x] Original snapshot: existing valid → accepted
- [x] Original snapshot: corrupted → error, NOT overwritten
- [x] Original snapshot: incomplete (file without metadata / metadata without file) → error
- [x] Original snapshot: source missing → SourceMissing
- [x] Original snapshot: cancellation leaves no partial
- [x] Restore point: creates unique directory with file + metadata
- [x] Restore point: hash/size correct
- [x] Restore point: source missing → failure
- [x] Official restore: success → restore point created, file replaced, metadata saved
- [x] Official restore: download temp cleaned
- [x] Official restore: game_patch recorded
- [x] Fallback: official unavailable + patch match → fallback success
- [x] Fallback: patch mismatch → PatchMismatch
- [x] Fallback: snapshot patch null → FallbackNotAllowed
- [x] Fallback: currentOfficialPatch null → FallbackNotAllowed
- [x] Fallback: corrupted snapshot → FallbackNotAllowed
- [x] Fallback: does not modify immutable snapshot
- [x] Failure before replace → target unchanged

---

### Етап 6: Transactional install + rollback

**Що реалізовано:**
- Transactional API release install/update через `LocalizationInstallService`
- Verified Stage 4 download перед будь-якою модифікацією
- Immutable original snapshot safety: наявність + цілісність перед replace
- Pre-operation restore point з `trustedGamePatch` з попереднього стану (не з нового release)
- Exact raw installation-state snapshot в restore-point директорію
- Destructive boundary: `ReplaceGameFileAsync` з post-replace SHA-256 verification
- Post-replace size + SHA-256 verification проти release contract
- API `InstallationMetadata` commit тільки після verified replacement
- Full game + installation-state rollback при помилці після replace
- `RollbackAsync` повертає `RollbackResult` (IsSuccess, GameRestored, StateRestored, ErrorMessage)
- Обидва rollback components (game + state) завжди виконуються навіть при помилці одного
- State rollback атомарний: temp → `File.Replace`/`File.Move` → byte-for-byte verify
- `InstallError.RollbackFailed` — typed critical result при partial rollback failure
- Restore points retained після failed transaction
- Download temp cleanup через ownership flag + finally pattern (`preReplaceCompleted`)
- Pre-operation state validation: Invalid → `PreOperationStateFailed` до download/snapshot
- Cancellation-safe: OCE передається, mandatory rollback через `CancellationToken.None`

**Acceptance criteria:**
- [x] Download verified (size + SHA-256) before destructive operation
- [x] Original snapshot safety (exists + integrity) before first modification
- [x] Restore point created immediately before replace
- [x] Exact pre-operation installation state captured (raw bytes or null)
- [x] Never download directly over game file
- [x] Failure before replace leaves game/state unchanged
- [x] Replace followed by release size + SHA-256 verification
- [x] Installation metadata saved only after verified game replacement
- [x] Post-replace verification/state-save failure rolls back game + installation state
- [x] First-install rollback restores installation-state absence
- [x] Update rollback restores exact prior installation-state bytes
- [x] Mandatory rollback uses independent cancellation token (`CancellationToken.None`)
- [x] Partial rollback failure returns `InstallError.RollbackFailed`
- [x] No false successful metadata after failure
- [x] Download temp cleanup on success/failure/cancellation/early return
- [x] Completed restore points retained
- [x] Original snapshot remains immutable

**Boundary RollbackFailed vs Stage 7 Corrupted:**
Stage 6 повертає `InstallError.RollbackFailed` при невдалому rollback. LocalizationState resolution (включно з `Corrupted`) належить до Stage 7. Stage 6 НЕ встановлює `LocalizationState.Corrupted` напряму.

**Файли:** `Services/LocalizationInstallService.cs`, `Services/InstallResult.cs`, `Storage/BackupStore.cs`, `Storage/InstallationStateStore.cs`

**Тести (v6.x, 203 total):**
- [x] Input validation: missing game, empty mode, empty public id, invalid version/patch, HTTP URL, zero size, empty SHA-256
- [x] Incompatible release blocks before HTTP
- [x] First install success: snapshot created, game replaced, metadata saved, restore point exists
- [x] Update success: existing snapshot unchanged, game updated, metadata updated, restore point exists
- [x] Download failure: game/state unchanged, no snapshot, no restore point
- [x] Corrupted original snapshot after download → OriginalSnapshotFailed
- [x] Missing snapshot + valid source=api metadata → OriginalSnapshotFailed
- [x] Corrupted pre-operation state → PreOperationStateFailed before HTTP
- [x] Restore-point game_patch matches pre-operation patch (update: old=100, new=101 → rp=100)
- [x] Restore-point game_patch null for first install without prior state
- [x] State save failure → game rollback to original, state rollback to prior bytes
- [x] State save failure → rollback restores exact prior metadata bytes (byte-for-byte)
- [x] First-install state save failure → state absent after rollback
- [x] Cancellation before replace → game unchanged, no state, temp cleaned
- [x] Cancellation during original snapshot → download temp cleaned, no *.tmp
- [x] Cancellation during restore-point creation → download temp cleaned, no *.tmp
- [x] Restore-point retained after state-save failure rollback
- [x] Raw state snapshot persistence failure → BackupFailed
- [x] Corrupted pre-state with/without snapshot → PreOperationStateFailed
- [x] Download ordering: failure creates no snapshot, no restore point
- [x] Post-replace verification rollback: game restored

---

### Етап 7: Localization state/update detection + compatibility

**Що реалізовано (v7.0 + v7.1):**
- `LocalizationStateService` — deterministic state resolution
- `LocalizationStateResult` — typed result with `State` + diagnostic `Error`
- `LocalizationCompatibilityService` — action-level compatibility decision
- `CompatibilityResult` — typed result with `IsAllowed` + blocking `Reason`
- Resolution order: metadata load → source check → file existence → SHA-256 verification → public_id comparison
- Canonical contract: Missing metadata → NotInstalled; Invalid metadata → InstalledVersionUnknown; source=official → NotInstalled
- Primary release identity: `public_id` (ordinal exact), not version/patch
- `current == null` → WaitingForRelease (Error=null) + compatibility Blocked
- `current != null` але `PublicId` null/empty/whitespace → WaitingForRelease + diagnostic Error + compatibility Blocked
- Factual hash verification via `HashHelper.ComputeFileSha256Async`
- `Corrupted`: API metadata valid, but file missing/unreadable/hash mismatch
- Cancellation propagation during hash computation
- Compatibility: API `CompatibleWithOfficialPatch` boolean = source of truth; version/patch irrelevant
- Stage 6 transaction guard (`InstallError.Incompatible`, 0 HTTP) retained as defense-in-depth
- Compatibility layer returns reason; UI display → Stage 9

**Acceptance criteria (state resolution — v7.0):**
- [x] `NotInstalled`: metadata відсутня АБО source="official"
- [x] `UpToDate`: public_id збігаються (ordinal exact)
- [x] `UpdateAvailable`: public_id відрізняються
- [x] `WaitingForRelease`: встановлено, hash OK, current відсутній
- [x] `InstalledVersionUnknown`: installation.json існує, але Load() повертає Invalid
- [x] `Corrupted`: API metadata valid, але файл missing/unreadable/hash mismatch
- [x] Primary identity — `public_id`, не version/patch
- [x] Не використовувати лише `patch`/`version`

**Acceptance criteria (compatibility — v7.1):**
- [x] `compatible_with_official_patch == false` → Install та Update заборонені
- [x] Download не починається при несумісності (Stage 6 guard: `InstallError.Incompatible`, 0 HTTP)
- [x] Compatibility layer повертає blocking reason для UI
- [x] `current == null` → blocked (no release available)
- [x] Malformed `PublicId` → blocked (invalid metadata)
- [x] Version/patch не впливають на compatibility decision (API boolean = source of truth)

**Acceptance criteria (UI display — Stage 9, НЕ реалізовано):**
- [ ] Користувачу показується причина блокування

**Файли:** `Services/LocalizationStateService.cs`, `Services/LocalizationState.cs`, `Services/LocalizationStateResult.cs`, `Services/LocalizationCompatibilityService.cs`, `Services/CompatibilityResult.cs`

**Тести (v7.0 + v7.1, 26 tests):**
- [x] installation.json missing → NotInstalled
- [x] valid source=official → NotInstalled
- [x] malformed JSON → InstalledVersionUnknown
- [x] semantically invalid metadata → InstalledVersionUnknown
- [x] valid API metadata + game file missing → Corrupted
- [x] valid API metadata + hash mismatch → Corrupted
- [x] valid API metadata + matching hash + current=null → WaitingForRelease (Error=null)
- [x] current.PublicId null/empty/whitespace → WaitingForRelease + diagnostic Error
- [x] valid API metadata + matching hash + same public_id → UpToDate
- [x] same public_id + different version/patch → UpToDate
- [x] valid API metadata + matching hash + different public_id → UpdateAvailable
- [x] different public_id + same version/patch → UpdateAvailable
- [x] cancellation during hash → OCE propagated
- [x] public_id is primary identity (not version/patch)
- [x] current=null → compatibility Blocked
- [x] current.PublicId null/empty/whitespace → compatibility Blocked
- [x] compatible=false + valid PublicId → Blocked (reason explains compatibility)
- [x] compatible=true + valid PublicId → Allowed (Reason=null)
- [x] version/patch don't affect compatibility decision
- [x] compatible=false cannot be overridden by version/patch/public_id
- [x] Stage 6 guard: incompatible release → InstallError.Incompatible, 0 HTTP requests

---

### Етап 8: Basic WinForms UI

**Що реалізовано (v8.0):**
- `MainForm` — одне основне вікно з 4 блоками через TableLayoutPanel
- Game Detection блок: label game path, buttons "Знайти гру" / "Обрати вручну"
- Mode Selection блок: 3 RadioButtons з Tag-based slugs (full-ukrainian, full-ukrainian-bosia, english-items)
- Status блок: state label, details label, ProgressBar, progress %, multiline message TextBox
- Actions блок: 4 buttons (Install/Update/Restore Original/Restore Backup), disabled by default
- DPI-aware layout via AutoScaleMode.Font + TableLayoutPanel + AutoSize
- Resize-safe: MinimumSize, percent-based columns, AutoEllipsis для game path
- Presentation helper methods: SetGamePathText, SetLocalizationStateText, SetDetailsText, SetProgress, SetMessage, SetActionsEnabled, GetSelectedModeSlug
- UI без business logic: без HttpClient, без API calls, без file operations, без config/state

**Acceptance criteria:**
- [x] Одне вікно, вертикальний layout
- [x] Блок Game Detection (пошук + ручний вибір)
- [x] Блок Mode Selection (3 radio buttons)
- [x] Блок Status (state + progress bar)
- [x] Блок Actions (Install / Update / Restore Original / Restore Backup)
- [x] UI без бізнес-логіки

**Файли:** `MainForm.cs`, `MainForm.Designer.cs`

---

### Етап 9: Підключення UI до services

**Що реалізовано (v9.0):**
- Composition root у `Program.cs` — concrete dependencies, no DI container
- Startup sequence: load config → restore last_mode → load API → detect game → state/compatibility refresh
- API: `GetReleasesAsync` at startup, cached in memory for session
- API failure: message shown, actions disabled, detection still works independently
- Game detection: `GameDetector.DetectAsync` with API `install_path_patterns`
- Manual browse: `FolderBrowserDialog` → `ValidateAndSaveManualPathAsync`
- Mode persistence: `ConfigStore` load/save `LastMode` on mode change
- Mode→API mapping: slug from RadioButton.Tag → find mode in `response.Data.Modes`
- `RefreshStateAsync`: state resolution + compatibility check + UI update
- Diagnostics priority: API mode issue > state error > compatibility reason > neutral
- LocalizationState → Ukrainian UI text mapping
- Details: mode public name + version + patch
- current=null: WaitingForRelease + blocked + disabled actions
- Malformed current: WaitingForRelease + diagnostic Error + blocked
- API failure ≠ current=null (separate `_apiLoadedSuccessfully` flag)
- All action buttons disabled in v9.0 (install/update/restore not wired)

**Що реалізовано (v9.1):**
- Install button → `LocalizationInstallService.InstallReleaseAsync` (progress: null)
- Update button → same `InstallReleaseAsync` (Stage 6 distinguishes first-install/update)
- Restore Original → `RestoreOriginalService.RestoreOriginalAsync`
- Shared `HandleInstallOrUpdateAsync(isUpdate)` helper (no duplication)
- `_operationInProgress` bool guard — prevents concurrent operations
- UI controls disabled during operation (game detection, mode selection, action buttons)
- Real action availability in `RefreshStateAsync`:
  - Install: NotInstalled + compatible + current exists
  - Update: UpdateAvailable + compatible + current exists
  - Restore Original: UpToDate/UpdateAvailable/WaitingForRelease/Corrupted/InstalledVersionUnknown
  - Restore Backup: always disabled (deferred)
- Compatibility defense-in-depth: UI check + existing Stage 6 `InstallError.Incompatible` guard
- Factual state precondition: Install requires `_lastResolvedState == NotInstalled`, Update requires `UpdateAvailable` — checked before service call, not just button Enabled
- `GetSelectedApiMode()` helper — centralized mode lookup (ordinal slug comparison)
- Error mapping: `InstallError`/`RestoreError` → concise Ukrainian messages
- Critical errors (`RollbackFailed`, `RecoveryFailed`) shown with "КРИТИЧНО:" prefix
- Post-operation `RefreshStateAsync()` — always, even after failure
- Operation result message set AFTER refresh (so refresh doesn't erase it)
- Event exception safety: full outer try/catch/finally on all async void handlers
- `LocalizationInstaller` + `BackupStore` + `InstallationStateStore` passed to MainForm
- Shared `HttpClient` reused for `BdoUaApiClient` and `LocalizationInstaller`
- `LocalizationInstallService` constructed per-operation (gameRoot known after detection)
- `RestoreOriginalService` constructed per-operation from cached API `OfficialSourceUrl`/`OfficialPatch`

**НЕ реалізовано у v9.1:**
- Restore Backup (deferred — requires restore-point selection contract/UI)
- Progress/cancellation UX (Stage 10)
- OperationState enum/UI (Stage 10)

**Acceptance criteria (v9.0):**
- [x] `Знайти гру` → GameDetector.DetectAsync → UI update
- [x] Manual path selection/validation via FolderBrowserDialog
- [x] Mode selection saves config
- [x] API + state + compatibility refresh on startup and mode change
- [x] Errors shown in UI

**Acceptance criteria (v9.1):**
- [x] "Встановити" → LocalizationInstallService.InstallReleaseAsync → UI result
- [x] "Оновити" → LocalizationInstallService.InstallReleaseAsync → UI result
- [x] "Відновити оригінал" → RestoreOriginalService.RestoreOriginalAsync → UI result
- [x] Operation guard prevents concurrent operations
- [x] UI controls disabled during operation
- [x] Помилки зрозумілою мовою (InstallError/RestoreError → Ukrainian)
- [x] Critical errors (RollbackFailed/RecoveryFailed) marked explicitly
- [x] Post-operation state refresh
- [x] Action availability based on factual state + compatibility
- [ ] OperationState оновлює UI (Stage 10)
- [x] Restore Backup (v10.3)

**Файли:** `Program.cs`, `MainForm.cs`

---

### Етап 10: Progress + cancellation UX

**Що реалізовано (v10.0):**
- `OperationState` enum — `Services/OperationState.cs` (11 states: Idle, DetectingGame, LoadingApi, Downloading, Verifying, BackingUp, Installing, Restoring, Completed, Failed, Cancelled)
- `SetOperationState` helper — maps state to Ukrainian UI text in `progressLabel`
- OperationState ≠ LocalizationState (temporary operation vs persistent factual state)
- Startup: LoadingApi → DetectingGame → Idle
- Detect button: DetectingGame → Idle
- Install/Update: real `IProgress<DownloadProgress>` wired — progressBar shows actual download %
- `OnDownloadProgress` handler: reads `DownloadProgress.Percentage`, clamps 0..100
- Progress reset: 0% at start of Install/Update/Restore; actual % during download; 100% only on real completion
- Install/Update: Downloading → Completed/Failed
- Restore Original: Restoring → Completed/Failed (no real download progress — progress:null in service)
- Precondition blocked: OperationState stays Idle (not Failed)
- Unexpected exception: Failed
- BackingUp/Verifying/Installing enum values exist per contract but NOT faked in UI (service doesn't report phases)

**Що реалізовано (v10.1):**
- Per-operation `CancellationTokenSource` — created before service call, disposed in finally
- Cancel button ("Скасувати") in actionsPanel, initially disabled
- Cancel enabled only during active service call (after CTS creation)
- Cancel handler: disables button, sets "Скасування операції...", calls `_operationCts.Cancel()`
- `OperationCanceledException` catch: `OperationState.Cancelled` + Ukrainian message
- Recovery/rollback failure → `OperationState.Failed` + critical message (NOT Cancelled)
- Cancel button disabled + CTS disposed/null in finally (all paths)
- `FormClosing` safety: blocks close during active operation, requests cancellation if possible
- Startup/detection NOT cancellable (Cancel button only for Install/Update/Restore Original)

**Що реалізовано (v10.2):**
- `BackupMetadata.InstallationState` — nullable string marker: `"present"` / `"absent"` / null (backward compatible)
- `RestorePointInfo` model — Id, CreatedAt, GamePatch, Source, SizeBytes, Sha256, HasInstallationState, IsRestorable
- `BackupStore.ListRestorePointsAsync` — catalog: newest-first, hash/size validation, corrupt entries excluded
- `BackupStore.ResolveRestorePointAsync` — path traversal protection, integrity validation, IsRestorable check
- `BackupStore.CreateRestorePointAsync` — centralized: writes game file + metadata + state snapshot (if provided) + marker
- `RestoreOriginalService` — now captures pre-op installation state and passes to CreateRestorePointAsync (gap fixed)
- `LocalizationInstallService` — uses centralized CreateRestorePointAsync (no manual state snapshot write)
- `RestoreBackupService` — transactional restore:
  - Validates selected restore point (existence, integrity, IsRestorable)
  - Creates pre-operation restore point before destructive change
  - Replaces game file via BackupStore.ReplaceGameFileAsync
  - Restores installation state (present → write bytes + verify; absent → delete file)
  - Rollback on state failure: game + state restored from pre-op restore point
  - Cancellation: pre-replace → OCE propagated; post-replace → rollback with CancellationToken.None
- New `RestoreError` values: `RestorePointNotFound`, `RestorePointInvalid`, `StateRestoreFailed`
- Legacy restore points: no marker + state file → IsRestorable=true; no marker + no state file → IsRestorable=false
- 14 new tests: catalog, create, restore success, restore failure, path traversal, legacy handling

**Що реалізовано (v10.3):**
- Restore Backup button: enabled when game root found (API/mode independent)
- Button text: "Відновити копію" (no English "backup" in UI)
- `RestorePointSelectionForm` — modal ListView dialog (Дата, Патч, Операція, Розмір)
- Only `IsRestorable` points shown; non-restorable excluded
- Source labels mapped to readable Ukrainian (pre_install → "Перед встановленням/оновленням", etc.)
- No auto-selection: user explicitly selects row, clicks "Відновити"
- Confirmation MessageBox before destructive operation
- `RunRestoreBackupAsync`: full operation flow with CTS, cancellation, error mapping
- New error mappings: RestorePointNotFound, RestorePointInvalid, StateRestoreFailed, BackupIo
- RecoveryFailed → КРИТИЧНО prefix
- OperationState: Restoring → Completed/Failed/Cancelled
- FormClosing protection active (shared _operationInProgress + _operationCts)
- API/mode independence: Restore Backup works without API success or selected mode

**Acceptance criteria:**
- [x] Progress bar: % download (real DownloadProgress for Install/Update)
- [x] "Скасувати" → cancellation → файл не змінено до replace; post-replace recovery delegated to transaction safety
- [x] OperationState оновлює UI

**Файли v10.0-v10.1:** `Services/OperationState.cs`, `MainForm.cs`, `MainForm.Designer.cs`
**Файли v10.2:** `Models/BackupMetadata.cs`, `Models/RestoreResult.cs`, `Models/RestorePointInfo.cs`, `Storage/BackupStore.cs`, `Services/RestoreOriginalService.cs`, `Services/LocalizationInstallService.cs`, `Services/RestoreBackupService.cs`, tests
**Файли v10.3:** `MainForm.cs`, `MainForm.Designer.cs`, `RestorePointSelectionForm.cs`

---

### Етап 11: Logging finalization

**Що реалізовано (v11.0):**
- `FileLogger` implementing `ILogger` — `Logging/FileLogger.cs`
- Daily log file: `bdo-ua-client_yyyy-MM-dd.log` in `%LocalAppData%\BDO-UA-Client\logs\`
- Format: `yyyy-MM-dd HH:mm:ss.fff [LEVEL] message`
- All four levels: DEBUG, INFO, WARN, ERROR
- Thread-safe append via `lock`
- Multiline normalization (CR/LF → space)
- Non-throwing failure policy (catch all)
- UTF-8 encoding
- Program.cs: one shared `FileLogger`, startup/exit logs
- SimpleLogger removed
- Existing service log coverage verified: detection, API, download, install, restore, rollback, errors
- 10 FileLogger tests (format, levels, filename, append, multiline, concurrency, invalid path)

**Acceptance criteria:**
- [x] Логи: запуск, detection, API calls, download, install, errors, rollback
- [x] Формат: `{timestamp} [{level}] {message}`
- [x] Ротація: по днях
- [x] Logger contract визначено в Етапі 1, тут — реалізація

**Файли:** `Logging/FileLogger.cs`, `Program.cs`, `BdoClient.Tests/Logging/FileLoggerTests.cs`

---

### Етап 12: Release build + E2E перевірка

**Що реалізовано (v12.0):**
- `dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true`
- GitHub Actions release-build.yml workflow (workflow_dispatch)
- Pre-publish: restore → build Release → test Release → publish
- ZIP artifact: `BDO-UA-Client-win-x64.zip` with flat structure
- `build-info.txt` with commit SHA, configuration, runtime, self-contained
- Artifact uploaded: `BDO-UA-Client-win-x64`
- Local publish verified: 464 files, ~160 MB, BdoClient.exe present, no test assemblies
- `.gitignore` updated: `artifacts/` excluded
- No SingleFile, no trimming, no installer, no signing

**Що реалізовано (v12.1):**
- MainForm widened: ClientSize 640×560, MinimumSize 620×480
- Restore Backup removed from public UI (product decision)
- RestorePointSelectionForm.cs deleted
- Backend restore-point safety retained (install/Restore Original transactions)
- Update button removed — single "Встановити" for first install / mode switch / newer release
- Dynamic API-driven mode selection: RadioButtons built from `_apiResponse.Data.Modes`
- No hardcoded mode slugs (full-ukrainian, english-items, etc.) in production UI
- Only modes with `current != null` displayed
- Factual LocalizationState restored: uses INSTALLED mode's current (not selected mode)
- `LocalizationStateService.ResolveAsync` reverted: no selectedModeSlug parameter
- Installed mode info display: mode name, version, InstalledAt (local time)
- Selected mode details: name, version, patch, published_at (from API)
- `published_at` field from API used as canonical release date
- Unified Install semantics: one button handles all replacement scenarios
- Exact already-installed check: same ModeSlug + same PublicId + UpToDate → Install disabled
- Cross-mode: Install enabled when different mode selected
- Restore Original: independent from selected mode, based on factual installed state
- AGENTS.md §18.7 added: product decision on single Install button
- README.md deleted (deferred until manual E2E complete)

**Що реалізовано (v12.1.2):**
- Per-mode release info: each RadioButton shows PublicName + v{version} • patch {patch} • реліз {published_at}
- `DynamicModePolicy` pure helper: GetInstallableModes, IsStructurallyInstallable, GetDisplayName, FormatPublishedDate, FormatReleaseLine, ResolveInitialSelection
- `InstallActionPolicy` pure helper: Evaluate → CanInstall, CanRestoreOriginal, AlreadyInstalledExactTarget
- Structural installable-mode validation: PublicId, DownloadUrl, Sha256, SizeBytes, Version, Patch, HTTPS
- Malformed current excluded from display
- Blank PublicName → Slug fallback → "Невідомий режим"
- published_at: DateTimeOffset.TryParse → local time → dd.MM.yyyy; null/invalid → omitted
- HandleInstallAsync uses same InstallActionPolicy (no duplicate logic)
- RefreshStateAsync uses InstallActionPolicy
- UpdateAvailable → "Доступна новіша версія" (presentation only, enum unchanged)
- 24 new pure tests (14 DynamicModePolicy + 10 InstallActionPolicy), 300 total

**Що реалізовано (v12.2):**
- Single-file self-contained publish: `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `PublishTrimmed=false`
- Executable name: `BDO-UA-Client.exe` via `-p:AssemblyName=BDO-UA-Client` (publish-only, namespace unchanged)
- Actions artifact: single EXE uploaded directly (no nested ZIP, no build-info.txt)
- Download shape: `BDO-UA-Client-win-x64.zip → BDO-UA-Client.exe`
- EXE size: ~155 MB
- No path assumptions in production code (uses absolute %LocalAppData% paths)
- PDB generated but excluded from artifact (upload only EXE)

**Technical acceptance:**
- [x] win-x64 self-contained publish succeeds
- [x] single-file publish produces one EXE
- [x] release workflow produces downloadable single EXE

**Acceptance criteria (v12.1 manual E2E):**
- [ ] `.exe` без .NET Runtime на чистій Windows
- [ ] Detection знаходить гру (Steam)
- [ ] API повертає дані
- [ ] Встановлення success → файл замінено
- [ ] Backup створено
- [ ] Оновлення працює (via Install button)
- [ ] Повернення до стану без української локалізації через Restore Original працює
- [ ] Логи записуються

**Файли:** `MainForm.cs`, `MainForm.Designer.cs`, `Services/LocalizationStateService.cs`, `AGENTS.md`, `plan.md`

---

## Ризикові місця

| # | Ризик | Мітігация |
|---|---|---|
| 1 | Steam libraryfolders.vdf формат | Парсити тільки `path` ключі, defensive |
| 2 | appmanifest_582660.acf | Парсити тільки `installdir`, fallback на patterns |
| 3 | Registry відсутня | Detection продовжується далі |
| 4 | File locked при заміні | Попередити користувача, не force |
| 5 | Disk full | Перевіряти free space |
| 6 | API змінив формат | Defensive parsing, null checks |
| 7 | Official source недоступний | Попередити, запропонувати backup |
| 8 | Hash mismatch | Не встановлювати, видалити temp |
| 9 | Несумісність патчу | Install/Update заборонені, download не починається |
| 10 | Concurrent access | File lock на metadata |

---

## Правила комітів та версійності

### Формат коміту

```
v{ЕТАП}.{ПІДЕТАП} — {короткий опис українською}

{детальний опис того, що зроблено, що змінено, що оновлено}

Змінені файли:
- file1.cs
- file2.cs
```

### Приклади

```
v1.0 — project skeleton + API models + API client + tests

Створено проект з нуля:
- BdoUaClient.sln з двома проектами (BdoClient.csproj + BdoClient.Tests.csproj)
- WinForms skeleton (Program.cs, MainForm.cs, MainForm.Designer.cs)
- API models на основі фактичного /releases endpoint (ReleasesResponse, LocalizationMode, CurrentRelease тощо)
- ApiResult<T> — простий Result pattern без зовнішніх залежностей
- BdoUaApiClient — HttpClient + base URL + CancellationToken + timeout + error handling
- ILogger contract — мінімальний logging interface
- 14 unit tests (JSON deserialization, null current, malformed JSON, empty response, HTTP errors)

Виправлено §28.3 — прибрано "delete localization file" з whitelist.

Змінені файли:
- AGENTS.md
- BdoUaClient.sln
- BdoClient.csproj
- BdoClient.Tests/BdoClient.Tests.csproj
- Api/ApiResult.cs, BdoUaApiClient.cs
- Models/ (10 файлів)
- Logging/ILogger.cs
- Program.cs, MainForm.cs, MainForm.Designer.cs
```

```
v1.1 — API error handling + CancellationToken

- Додано обробку timeout, DNS, 4xx/5xx
- Async methods приймають CancellationToken
- Мінімальний logger contract

Змінені файли:
- Api/BdoUaApiClient.cs
```

### Правила

1. **Кожен завершений етап/підетап** — окремий коміт
2. **Номер коміту** — `v{ЕТАП}.{ПІДЕТАП}` (наприклад `v1.0`, `v1.1`, `v2.0`)
3. **Підетапи** — якщо етап великий, розбивати на логічні частини (`.0`, `.1`, `.2`)
4. **Опис** — коротко українською, що зроблено
5. **Список файлів** — перелічити всі змінені/створені файли
6. **Не комітити** — build errors, placeholder, broken code
7. **Перед комітом** — `dotnet build BdoUaClient.sln` + `dotnet test BdoUaClient.sln --no-build`
8. **Push** — після кожного коміту одразу push
9. **Звіт** — після кожного коміту/пушу ОБОВ'ЯЗКОВО повідомити: що закомічено, який message, які файли, hash, branch

### Звіт після коміту (обов'язковий формат)

```
✅ Коміт створено та запушено.

📝 Commit message: v1.0 — project skeleton + API models

📁 Змінені файли:
- BdoClient.csproj
- Models/ReleasesResponse.cs
- Api/BdoUaApiClient.cs

🔖 Hash: a1b2c3d
🌿 Branch: main → origin/main
```

### Публічний репозиторій

Репозиторій **публічний**. Перед кожним комітом перевіряти:
- Немає API keys, tokens, secrets
- Немає паролів, credentials
- Немає приватних ключів
- `.gitignore` містить виключення для секретів та .NET build artifacts

### Версіонування етапів

| Етап | Базовий номер | Підетапи |
|---|---|---|
| 1 | v1.x | v1.0 (skeleton), v1.1 (error handling), v1.2 (logger) |
| 2 | v2.x | v2.0 (storage), v2.1 (config) |
| 3 | v3.x | v3.0 (detection) |
| 4 | v4.x | v4.0 (download), v4.1 (SHA-256) |
| 5 | v5.x | v5.0 (snapshot), v5.1 (restore points), v5.2 (restore original) |
| 6 | v6.x | v6.0 (transaction), v6.1 (rollback result + atomic state), v6.2 (temp cleanup + patch semantics), v6.3 (ownership flag + finally) |
| 7 | v7.x | v7.0 (state detection), v7.1 (compatibility) || 8 | v8.x | v8.0 (UI layout) |
| 9 | v9.x | v9.0 (UI integration) |
| 10 | v10.x | v10.0 (progress UX) |
| 11 | v11.x | v11.0 (logging) |
| 12 | v12.x | v12.0 (release build) |

---

## Порядок виконання

Не реалізовувати всі етапи за один раз.

Після кожного етапу/підетапу:
1. `dotnet build BdoUaClient.sln` — має пройти без помилок
2. `dotnet test BdoUaClient.sln --no-build` — якщо є тести для цього етапу
3. Виправити compile errors
4. Створити коміт за форматом `v{ЕТАП}.{ПІДЕТАП} — {опис}`
5. Push в репозиторій
6. Коротко описати що реалізовано
7. Перелічити зміни у файлах
8. Вказати що перевірено
9. Зупинитись і дочекатись наступної команди

**Незакомічені файли:** перед кожним комітом перевіряти `git status` і додавати всі робочі файли
