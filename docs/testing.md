# Тестування

## Framework

- **xUnit** — єдиний test framework для проєкту
- **net8.0-windows** — target framework для test project (`BdoClient.Tests`)
- ProjectReference на основний `BdoClient.csproj`

## Кількість тестів

**335** автоматизованих тестів.

## Категорії тестів

### API тести

| Файл | Що тестує |
|------|-----------|
| `Api/ApiResultTests.cs` | `ApiResult<T>` — success/failure створення, значення, помилки |
| `Api/BdoUaApiClientTests.cs` | JSON deserialization, HTTP помилки (4xx, 5xx, timeout, DNS), null `current`, malformed JSON, порожня відповідь, cancellation |

### Model тести

| Файл | Що тестує |
|------|-----------|
| `Models/ReleasesResponseTests.cs` | `ReleasesResponse` serialization/deserialization, edge cases |

### Service тести

| Файл | Що тестує |
|------|-----------|
| `Services/GameDetectorTests.cs` | Path validation, Steam `libraryfolders.vdf` parsing, manual resolution (`ResolveManualGameRoot`), registry fallback, Unicode/пробіли |
| `Services/LocalizationInstallerTests.cs` | Download, SHA-256 перевірка, retry з backoff, progress reporting, cancellation |
| `Services/LocalizationInstallServiceTests.cs` | Transactional install (atomic workflow), rollback при помилці, cancellation mid-operation |
| `Services/LocalizationStateServiceTests.cs` | State resolution: `NotInstalled`, `UpToDate`, `UpdateAvailable`, `WaitingForRelease`, `InstalledVersionUnknown`, `Corrupted` |
| `Services/RestoreOriginalServiceTests.cs` | Restore через `official_source_url` (primary), local original snapshot (fallback), snapshot patch mismatch |
| `Services/RestoreBackupServiceTests.cs` | Catalog restore points, create restore point, restore success/failure, cancellation |
| `Services/DynamicModePolicyTests.cs` | Mode filtering (visibility, compatibility) |
| `Services/InstallActionPolicyTests.cs` | Install policy (allowed/blocked actions), exact target resolution |
| `Services/LocalizationCompatibilityServiceTests.cs` | Compatibility checks, `compatible_with_official_patch` |
| `Services/StartupOrchestrationTests.cs` | Parallel startup orchestration: final game outcome (Found/NotFound), API-first/local-first ordering, fallback Found/NotFound, no-patterns, API failure, callback exception safety |
| `Services/ApiErrorPresentationTests.cs` | `ApiErrorKind` → Ukrainian UI message mapping (all 6 kinds + None fallback) |

### Storage тести

| Файл | Що тестує |
|------|-----------|
| `Storage/BackupStoreTests.cs` | Original snapshot створення/захист, restore points каталог, replace, recovery, metadata |
| `Storage/ConfigStoreTests.cs` | Config load/save, default values, corrupt file recovery |
| `Storage/InstallationStateStoreTests.cs` | Installation state persistence, validation, migration |
| `Storage/AppPathsTests.cs` | App paths resolution, directory structure |

### Logging тести

| Файл | Що тестує |
|------|-----------|
| `Logging/FileLoggerTests.cs` | Log levels (DEBUG/INFO/WARNING/ERROR), file format, concurrent write safety, invalid path handling |

## Тестові патерни

### Temp directories

Тести створюють тимчасові директорії через `Path.GetTempPath()` + унікальний суфікс. Кожен тест прибирає за собою в `Dispose()`.

### IDisposable

Test класи реалізують `IDisposable` для cleanup temp files, відновлення стану.

### MockHttpHandler

`MockHttpHandler` (або аналогічний stub) — мок для `HttpMessageHandler`, що дозволяє:
- Повертати задані HTTP відповіді
- Імітувати timeout, DNS error, 4xx/5xx
- Перевіряти кількість та зміст запитів

### Test seam subclasses

Для тестування error paths використовуються спеціалізовані підкласи:
- `ThrowingBackupStore` — імітує помилку при backup операціях
- Інші аналогічні stubs для boundary testing

## Команди запуску

```bash
# Всі тести
dotnet test BdoUaClient.sln

# Без rebuild (якщо вже зібрано)
dotnet test BdoUaClient.sln --no-build

# З verbose output
dotnet test BdoUaClient.sln --verbosity normal

# Конкретний клас тестів
dotnet test BdoUaClient.sln --filter "FullyQualifiedName~GameDetectorTests"
```
