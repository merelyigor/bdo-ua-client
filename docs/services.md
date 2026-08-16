# Services — сервісний шар застосунку

---

## 1. GameDetector

Визначає шлях до встановленої гри. Окремий модуль, не змішує UI та filesystem логіку.

### Порядок детекції (`DetectAsync`)

1. **SavedConfig** — збережений шлях у `ConfigStore`
2. **Registry** — реєстр Windows
3. **Steam** — `libraryfolders.vdf` → `appmanifest_582660.acf` → `installdir`
4. **ApiPattern** — `install_path_patterns` з API (hints для перебору дисків)
5. **NotFound** — гра не знайдена

### Сигнатури

```csharp
public sealed class GameDetector
{
    public GameDetector(ConfigStore configStore, ILogger logger);

    public static bool ValidateGamePath(string gamePath);

    public async Task<DetectionResult> DetectAsync(
        IReadOnlyList<InstallPathPattern>? apiPatterns = null,
        CancellationToken cancellationToken = default);

    public async Task<DetectionResult> ValidateAndSaveManualPathAsync(
        string gamePath,
        CancellationToken cancellationToken = default);

    public static ManualResolveResult ResolveManualGameRoot(string selectedPath);
}
```

### `ValidateGamePath`

Перевіряє наявність `{gamePath}\ads\languagedata_en.loc`. Статичний метод, без побічних ефектів.

### `ResolveManualGameRoot`

Логіка ручного вибору користувача:

1. **Exact root** — якщо `selectedPath` вже містить `ads\languagedata_en.loc` → `Found`
2. **Unique child** — один підкаталог з `ads\languagedata_en.loc` → `Found`
3. **Ambiguous** — кілька підкаталогів з файлом → `Ambiguous`
4. **NotFound** — нічого не знайдено

### DetectionResult / ManualResolveResult

```csharp
public enum DetectionSource { SavedConfig, Registry, Steam, ApiPattern, Manual }

public sealed class DetectionResult
{
    public bool IsFound { get; }
    public string? GamePath { get; }
    public DetectionSource? Source { get; }
    public bool Persisted { get; }

    public static DetectionResult Found(string gamePath, DetectionSource source, bool persisted = true);
    public static DetectionResult NotFound();
}

public enum ManualResolveStatus { Found, NotFound, Ambiguous }

public sealed class ManualResolveResult
{
    public ManualResolveStatus Status { get; }
    public string? GamePath { get; }

    public static ManualResolveResult Found(string gamePath);
    public static ManualResolveResult NotFound();
    public static ManualResolveResult Ambiguous();
}
```

---

## 2. LocalizationInstaller

Відповідає за завантаження файлів локалізації та official source. Retry з backoff, SHA-256, progress.

### Сигнатури

```csharp
public sealed class LocalizationInstaller
{
    public LocalizationInstaller(HttpClient httpClient, AppPaths appPaths, ILogger logger,
        int timeoutSeconds = 60, int[]? retryDelaysMs = null);

    public LocalizationInstaller(AppPaths appPaths, ILogger logger,
        int timeoutSeconds = 60, int[]? retryDelaysMs = null);

    public async Task<DownloadResult> DownloadReleaseAsync(
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public async Task<DownloadResult> DownloadOfficialSourceAsync(
        string officialUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### `DownloadReleaseAsync`

Завантажує release файл за `release.DownloadUrl`. Перевіряє HTTP result, `size_bytes`, SHA-256. Retry до 4 спроб (1 + 3 retry) з затримками `[1000, 2000, 4000]` мс.

### `DownloadOfficialSourceAsync`

Завантажує оригінальний `languagedata_en.loc` за `official_source_url`. SHA-256 для official source НЕ надається сервером — перевірка лише розміру. Аналогічний retry.

### DownloadResult / DownloadProgress

```csharp
public enum DownloadError
{
    InvalidMetadata, Timeout, Network, Http,
    SizeMismatch, HashMismatch, Io, Unexpected
}

public sealed class DownloadResult
{
    public bool IsSuccess { get; }
    public string? TempFilePath { get; }
    public long? SizeBytes { get; }
    public string? Sha256 { get; }
    public DownloadError? Error { get; }
    public string? ErrorMessage { get; }
    public bool IsRetryable { get; }

    public static DownloadResult Success(string tempFilePath, long sizeBytes, string sha256);
    public static DownloadResult SuccessWithoutHash(string tempFilePath, long sizeBytes);
    public static DownloadResult Failure(DownloadError error, string? message = null, bool isRetryable = false);
}

public sealed class DownloadProgress
{
    public long BytesDownloaded { get; }
    public long? TotalBytes { get; }
    public double? Percentage { get; }
}
```

---

## 3. LocalizationInstallService

Транзакційна установка локалізації. Кожен фаза може повернути помилку; файл гри не змінюється до моменту заміни.

### Сигнатури

```csharp
public sealed class LocalizationInstallService
{
    public LocalizationInstallService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot);

    public async Task<InstallResult> InstallReleaseAsync(
        string modeSlug,
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### Фази `InstallReleaseAsync`

| # | Фаза | Опис |
|---|------|------|
| 1 | **Validate input** | Перевірка `modeSlug`, `release.PublicId`, `Version`, `Patch`, `DownloadUrl` (HTTPS), `SizeBytes`, `Sha256`, `CompatibleWithOfficialPatch` |
| 2 | **Validate pre-state** | `InstallationStateStore.Load()` — якщо Invalid → `PreOperationStateFailed` |
| 3 | **Download** | `LocalizationInstaller.DownloadReleaseAsync` → temp файл |
| 4 | **Original snapshot** | Якщо original snapshot ще не існує — створити копію поточного game file |
| 5 | **Restore point** | Створити pre-operation snapshot (restore point) поточного game file |
| 6 | **Replace** | Замінити game file на завантажений release |
| 7 | **Verify** | SHA-256 встановленого файлу == очікуваному hash |
| 8 | **Save state** | Записати `installation.json` (metadata) |
| 9 | **Rollback** | При помилці після replace — rollback до pre-operation snapshot |

### Rollback

Якщо replace успішний, але verify/state save невдалий:
- Спроба відновити game file з restore point
- Спроба відновити стан (`installation.json`)
- Якщо rollback не вдався — стан `Corrupted`, критична помилка користувачу

### InstallResult / InstallError

```csharp
public enum InstallError
{
    InvalidGamePath, InvalidRelease, Incompatible, DownloadFailed,
    OriginalSnapshotFailed, PreOperationStateFailed, BackupFailed,
    ReplaceFailed, VerificationFailed, StateSaveFailed, RollbackFailed
}

public sealed class InstallResult
{
    public bool IsSuccess { get; }
    public InstallError? Error { get; }
    public string? ErrorMessage { get; }

    public static InstallResult Success();
    public static InstallResult Failure(InstallError error, string? message = null);
}
```

---

## 4. RestoreOriginalService

Відновлює оригінальний `languagedata_en.loc`. Спочатку пробує завантажити з `official_source_url`; fallback на local original snapshot ТІЛЬКИ якщо `snapshot.game_patch == current.official_patch`.

### Сигнатури

```csharp
public sealed class RestoreOriginalService
{
    public RestoreOriginalService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot,
        string officialSourceUrl,
        int? currentOfficialPatch);

    public async Task<RestoreResult> RestoreOriginalAsync(CancellationToken cancellationToken = default);
}
```

### Логіка

1. Перевірити наявність game file
2. **Спроба 1: Download** — `DownloadOfficialSourceAsync(officialSourceUrl)`
   - Якщо `FallbackNotAllowed` (original snapshot відсутній) — помилка
   - Якщо `PatchMismatch` — помилка (snapshot не відповідає поточному official patch)
3. **Спроба 2: Local snapshot** — fallback на original snapshot з `BackupStore`
4. Replace game file → verify → save state (source = "official")

---

## 5. RestoreBackupService

Відновлює гру з обраного restore point (попередньої встановленої локалізації).

### Сигнатури

```csharp
public sealed class RestoreBackupService
{
    public RestoreBackupService(
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot);

    internal Action? OnPostGameReplaceHook { get; set; } // test seam

    public async Task<RestoreResult> RestoreAsync(
        string restorePointId,
        CancellationToken cancellationToken = default);
}
```

### Логіка `RestoreAsync`

1. Перевірити наявність game file
2. Знайти restore point за `restorePointId` (BackupStore)
3. Створити pre-operation snapshot поточного стану (для rollback)
4. Замінити game file на файл з restore point
5. Verify SHA-256
6. Оновити `installation.json` (metadata з restore point)
7. При помилці — rollback game file + rollback state

---

## 6. DynamicModePolicy

Визначає, які режими доступні для установки, та формулює UI-рядки. Статичний клас.

### Сигнатури

```csharp
internal static class DynamicModePolicy
{
    public static List<LocalizationMode> GetInstallableModes(List<LocalizationMode>? allModes);

    public static bool IsStructurallyInstallable(LocalizationMode mode);

    public static string GetDisplayName(LocalizationMode mode);

    public static string FormatReleaseLine(LocalizationMode mode);

    public static string? FormatPublishedDate(string? publishedAt);

    public static string? ResolveInitialSelection(
        string? savedLastMode,
        List<LocalizationMode> installableModes);
}
```

### `GetInstallableModes`

Фільтрує `allModes` — залишає лише режими, де `IsStructurallyInstallable == true`.

### `IsStructurallyInstallable`

Перевіряє наявність обов'язкових полів: `Slug`, `Current.PublicId`, `Current.DownloadUrl` (HTTPS), `Current.Sha256`, `Current.SizeBytes > 0`, `Current.Version > 0`, `Current.Patch > 0`.

### `GetDisplayName`

`PublicName` → `Slug` → `"Невідомий режим"`.

### `FormatReleaseLine`

Формат: `v{version} • patch {patch} • реліз {dd.MM.yyyy}`.

### `FormatPublishedDate`

Парсинг ISO datetime → `dd.MM.yyyy` (local time). `null` якщо невалідний.

### `ResolveInitialSelection`

Збіг з `savedLastMode` (ordinal) → перший зі списку → `null` якщо порожній.

---

## 7. InstallActionPolicy

Визначає, які дії доступні користувачу (Install / Restore Original). Статичний клас.

### Сигнатури

```csharp
internal sealed class InstallActionPolicyResult
{
    public bool CanInstall { get; }
    public bool CanRestoreOriginal { get; }
    public bool AlreadyInstalledExactTarget { get; }
}

internal static class InstallActionPolicy
{
    public static bool IsExactInstalledTarget(
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode? mode);

    public static InstallActionPolicyResult Evaluate(
        LocalizationState factualState,
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode? selectedMode,
        CurrentRelease? selectedCurrent,
        CompatibilityResult compatResult,
        bool operationInProgress);
}
```

### `IsExactInstalledTarget`

Порівнює `installedModeSlug` та `installedPublicId` з `mode.Slug` та `mode.Current.PublicId` (ordinal exact). Обидва повинні збігатися.

### `Evaluate`

| Поле | Умова |
|------|-------|
| `CanInstall` | `!operationInProgress` AND `structurallyValid` AND `compatResult.IsAllowed` AND `!alreadyInstalled` |
| `CanRestoreOriginal` | `!operationInProgress` AND state ∈ {UpToDate, UpdateAvailable, WaitingForRelease, Corrupted, InstalledVersionUnknown} |
| `AlreadyInstalledExactTarget` | state == UpToDate AND installedModeSlug == selectedMode.Slug AND installedPublicId == selectedCurrent.PublicId |

---

## 8. LocalizationStateService

Визначає фактичний стан встановленої локалізації. Не приймає "selected mode" як вхідний параметр — працює лише з файловою реальністю та metadata.

### Сигнатури

```csharp
public sealed class LocalizationStateService
{
    public LocalizationStateService(InstallationStateStore stateStore, ILogger logger);

    public async Task<LocalizationStateResult> ResolveAsync(
        CurrentRelease? current,
        string gameLocFilePath,
        CancellationToken cancellationToken = default);
}
```

### Логіка `ResolveAsync`

| Крок | Умова | Результат |
|------|-------|-----------|
| 1 | `installation.json` missing | `NotInstalled` |
| 2 | `installation.json` invalid | `InstalledVersionUnknown` |
| 3 | `metadata.Source == "official"` | `NotInstalled` |
| 4 | Game file missing | `Corrupted` |
| 5 | SHA-256 mismatch | `Corrupted` |
| 6 | `current == null` | `WaitingForRelease` |
| 7 | `current.PublicId` empty/null/whitespace | `WaitingForRelease` + Warning |
| 8 | `metadata.PublicId == current.PublicId` (ordinal) | `UpToDate` |
| 9 | Інше | `UpdateAvailable` |

### LocalizationStateResult

```csharp
public sealed class LocalizationStateResult
{
    public LocalizationState State { get; }
    public string? Error { get; }

    public static LocalizationStateResult Success(LocalizationState state);
    public static LocalizationStateResult WithWarning(LocalizationState state, string error);
}
```

---

## 9. LocalizationCompatibilityService

Перевіряє `compatible_with_official_patch` з API.

### Сигнатури

```csharp
public sealed class LocalizationCompatibilityService
{
    public CompatibilityResult Check(CurrentRelease? current);
}
```

### Логіка `Check`

| Умова | Результат |
|-------|-----------|
| `current == null` | `Blocked("Current release is not available.")` |
| `current.PublicId` empty/null/whitespace | `Blocked("Current release metadata is invalid: public_id is empty.")` |
| `!current.CompatibleWithOfficialPatch` | `Blocked("Release is not compatible with the current official game patch.")` |
| Інше | `Allowed()` |

### CompatibilityResult

```csharp
public sealed class CompatibilityResult
{
    public bool IsAllowed { get; }
    public string? Reason { get; }

    public static CompatibilityResult Allowed();
    public static CompatibilityResult Blocked(string reason);
}
```

---

## 10. HashHelper

Утиліта для обчислення SHA-256 та копіювання файлів. `internal static`.

```csharp
internal static class HashHelper
{
    public static string ComputeFileSha256(string filePath);
    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default);
    public static string ComputeSha256(byte[] data);
    public static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
```

SHA-256 повертається у форматі lowercase hex string.
