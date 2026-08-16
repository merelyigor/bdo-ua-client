# API клієнт — документація

## Endpoint

Єдиний endpoint:

```
GET https://bdo-ua.com.ua/api/public/v1/releases
```

Авторизація не потрібна. Відповідь — JSON.

---

## BdoUaApiClient

Клас: `BdoClient.Api.BdoUaApiClient`

Обгортає `HttpClient`. Повертає `ApiResult<T>` замість виключень.

| Параметр | Значення |
|---|---|
| Base URL | `https://bdo-ua.com.ua/api/public/v1` |
| Timeout | 30 секунд (за замовчуванням) |
| JSON | `PropertyNameCaseInsensitive = true` |

### Методи

```csharp
Task<ApiResult<ReleasesResponse>> GetReleasesAsync(CancellationToken cancellationToken = default)
```

### Обробка помилок

| Ситуація | ApiErrorKind |
|---|---|
| HTTP 4xx/5xx | `Http` |
| Порожня відповідь | `InvalidResponse` |
| Невалідний JSON | `InvalidResponse` |
| `success == false` | `InvalidResponse` |
| `data == null` | `InvalidResponse` |
| Timeout | `Timeout` |
| Скасовано | `Cancelled` |
| Мережева помилка | `Network` |
| Інше | `Unexpected` |

---

## ApiResult<T>

Клас: `BdoClient.Api.ApiResult<T>`

| Властивість | Тип | Опис |
|---|---|---|
| `IsSuccess` | `bool` | Успіх операції |
| `Value` | `T?` | Дані (якщо успіх) |
| `ErrorKind` | `ApiErrorKind` | Тип помилки |
| `ErrorMessage` | `string?` | Текст помилки |

### ApiErrorKind

```
None, Cancelled, Timeout, Network, Http, InvalidResponse, Unexpected
```

---

## Моделі відповіді

### ReleasesResponse

Кореневий об'єкт відповіді.

| JSON поле | C# властивість | Тип |
|---|---|---|
| `success` | `Success` | `bool` |
| `generated_at` | `GeneratedAt` | `string?` |
| `data` | `Data` | `ReleaseData?` |

### ReleaseData

| JSON поле | C# властивість | Тип |
|---|---|---|
| `official_patch` | `OfficialPatch` | `int` |
| `official_patch_checked_at` | `OfficialPatchCheckedAt` | `string?` |
| `official_source_url` | `OfficialSourceUrl` | `string?` |
| `filename` | `Filename` | `string?` |
| `install_path_patterns` | `InstallPathPatterns` | `List<InstallPathPattern>?` |
| `install_guide_url` | `InstallGuideUrl` | `string?` |
| `progress` | `Progress` | `ProgressInfo?` |
| `modes` | `Modes` | `List<LocalizationMode>?` |

### LocalizationMode

| JSON поле | C# властивість | Тип |
|---|---|---|
| `slug` | `Slug` | `string?` |
| `public_name` | `PublicName` | `string?` |
| `description` | `Description` | `string?` |
| `audience` | `Audience` | `string?` |
| `current` | `Current` | `CurrentRelease?` |
| `history` | `History` | `List<ReleaseHistoryItem>?` |

**Відомі slug:**
- `full-ukrainian` — повна українська (Bosia + правки спільноти)
- `full-ukrainian-bosia` — повна українська лише від Bosia
- `english-items` — українські тексти з англійськими назвами предметів

> **Важливо:** `current` може бути `null`. Це нормальний стан — означає, що актуальний release для цього режиму ще не опубліковано. Не є помилкою API чи десеріалізації.

### CurrentRelease

| JSON поле | C# властивість | Тип |
|---|---|---|
| `public_id` | `PublicId` | `string?` |
| `version` | `Version` | `int` |
| `filename` | `Filename` | `string?` |
| `download_url` | `DownloadUrl` | `string?` |
| `size_bytes` | `SizeBytes` | `long` |
| `sha256` | `Sha256` | `string?` |
| `patch` | `Patch` | `int` |
| `compatible_with_official_patch` | `CompatibleWithOfficialPatch` | `bool` |
| `published_at` | `PublishedAt` | `string?` |
| `game_tested_at` | `GameTestedAt` | `string?` |
| `game_test` | `GameTest` | `GameTestInfo?` |
| `stats` | `Stats` | `StatsInfo?` |
| `announcements` | `Announcements` | `AnnouncementsInfo?` |

Якщо `compatible_with_official_patch == false` — Install та Update заборонені.

### ReleaseHistoryItem

| JSON поле | C# властивість | Тип |
|---|---|---|
| `public_id` | `PublicId` | `string?` |
| `version` | `Version` | `int` |
| `patch` | `Patch` | `int` |
| `status` | `Status` | `string?` |
| `published_at` | `PublishedAt` | `string?` |
| `retired_at` | `RetiredAt` | `string?` |

Статуси: `superseded`, `withdrawn`. Поточний (`current`) ніколи не з'являється в history.

### InstallPathPattern

Підказки для автоматичного пошуку гри. Не є довіреними filesystem instructions.

| JSON поле | C# властивість | Тип |
|---|---|---|
| `pattern` | `Pattern` | `string?` |
| `launcher` | `Launcher` | `string?` |
| `description` | `Description` | `string?` |

Значення `launcher`: `steam`, `official`.

### GameTestInfo

| JSON поле | C# властивість | Тип |
|---|---|---|
| `state` | `State` | `string?` |
| `label` | `Label` | `string?` |
| `note` | `Note` | `string?` |

### ProgressInfo

Глобальний прогрес перекладу (для всіх режимів).

| JSON поле | C# властивість | Тип |
|---|---|---|
| `total_rows` | `TotalRows` | `int` |
| `translated_percent` | `TranslatedPercent` | `double` |
| `manual_rows` | `ManualRows` | `int` |
| `manual_percent` | `ManualPercent` | `double` |
| `machine_rows` | `MachineRows` | `int` |
| `machine_percent` | `MachinePercent` | `double` |

### StatsInfo

| JSON поле | C# властивість | Тип |
|---|---|---|
| `rows_in_file` | `RowsInFile` | `int` |

`rows_in_file` може відрізнятися від `progress.total_rows`.

### AnnouncementsInfo

| JSON поле | C# властивість | Тип |
|---|---|---|
| `discord_releases` | `DiscordReleases` | `AnnouncementChannel?` |
| `telegram_main` | `TelegramMain` | `AnnouncementChannel?` |

### AnnouncementChannel

| JSON поле | C# властивість | Тип |
|---|---|---|
| `sent` | `Sent` | `bool` |
| `sent_at` | `SentAt` | `string?` |

---

## Ієрархія моделей

```
ReleasesResponse
├── success: bool
├── generated_at: string?
└── data: ReleaseData?
    ├── official_patch: int
    ├── official_patch_checked_at: string?
    ├── official_source_url: string?
    ├── filename: string?
    ├── install_guide_url: string?
    ├── install_path_patterns: List<InstallPathPattern>?
    │   └── InstallPathPattern { pattern, launcher, description }
    ├── progress: ProgressInfo?
    │   └── ProgressInfo { total_rows, translated_percent, manual_rows, manual_percent, machine_rows, machine_percent }
    └── modes: List<LocalizationMode>?
        └── LocalizationMode { slug, public_name, description, audience, current?, history? }
            ├── current: CurrentRelease?  ← nullable, null = release не опубліковано
            │   └── CurrentRelease { public_id, version, filename, download_url, size_bytes, sha256, patch, compatible_with_official_patch, published_at, game_tested_at, game_test?, stats?, announcements? }
            │       ├── game_test: GameTestInfo? { state, label, note }
            │       ├── stats: StatsInfo? { rows_in_file }
            │       └── announcements: AnnouncementsInfo? { discord_releases?, telegram_main? }
            │           └── AnnouncementChannel { sent, sent_at }
            └── history: List<ReleaseHistoryItem>?
                └── ReleaseHistoryItem { public_id, version, patch, status, published_at, retired_at }
```
