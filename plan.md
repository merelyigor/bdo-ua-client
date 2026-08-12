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
- `GameDetector.cs` з 6 кроками пошуку
- Валідація: наявність `ads\languagedata_en.loc`

**Acceptance criteria:**
- [ ] Порядок: saved path → registry → Steam libraryfolders → appmanifest → API patterns → manual
- [ ] Steam: читає `libraryfolders.vdf`, знаходить `appmanifest_582660.acf`, витягує `installdir`
- [ ] API `install_path_patterns` — ТІЛЬКИ hints (перебір дисків)
- [ ] Validation: файл `{game_path}\ads\languagedata_en.loc` існує
- [ ] Ручний вибір через `FolderBrowserDialog`
- [ ] Знайдений шлях зберігається в `config.json`

**Файли:** `Services/GameDetector.cs`

**Тести (v3.x):**
- [ ] Path validation: валідна директорія
- [ ] Path validation: відсутній `ads\languagedata_en.loc`
- [ ] Path validation: Unicode/пробіли у шляху

---

### Етап 4: Download + SHA-256 verification

**Що реалізовано:**
- Download у `{cache}/{unique-tmp}`
- Перевірка HTTP status, `Content-Length` vs `size_bytes`
- SHA-256 для release downloads; для official source — без hash

**Acceptance criteria:**
- [ ] `HttpClient` з timeout
- [ ] Temporary: `%LocalAppData%\BDO-UA-Client\cache\{random}.tmp`
- [ ] Retry: максимум 3 спроби, exponential backoff (1s, 2s, 4s)
- [ ] Перевірка `Content-Length` vs `size_bytes` (якщо доступно)
- [ ] SHA-256 для release files
- [ ] Для official: без SHA-256 перевірки
- [ ] При помилці — temp видаляється

**Файли:** `Services/LocalizationInstaller.cs`

**Тести (v4.x):**
- [ ] SHA-256 verification: hash збігається
- [ ] SHA-256 verification: hash не збігається → помилка
- [ ] Size validation при наявності `size_bytes`

---

### Етап 5: Backup/snapshot/restore

**Що реалізовано:**
- Original snapshot: ОДИН РАЗ перед першою модифікацією (не перезаписувати)
- Restore points: створюються ПЕРЕД replace game file
- Restore original: download з `official_source_url`; локальний snapshot — fallback

**Acceptance criteria:**
- [ ] Original snapshot: `%LocalAppData%\BDO-UA-Client\backups\original\`
- [ ] Original snapshot НЕ перезаписується
- [ ] Metadata original snapshot: `created_at`, `game_patch` (якщо достовірно), `sha256` (локально), `size_bytes`
- [ ] Локальний SHA-256 — НЕ checksum від API
- [ ] Restore points: `%LocalAppData%\BDO-UA-Client\backups\restore-points\{timestamp}\`
- [ ] Restore point створюється ПЕРЕД заміною (pre-operation snapshot)
- [ ] Кожен restore point: `languagedata_en.loc` + `metadata.json`
- [ ] Restore original: download з `official_source_url` → replace → metadata `source: "official"`
- [ ] Fallback: original snapshot використовується автоматично ТІЛЬКИ якщо достовірно встановлено, що `snapshot.game_patch == current.official_patch`. Якщо patch snapshot невідомий або не збігається — автоматичний restore заборонений, показати помилку
- [ ] При відсутності official source + неможливості fallback → помилка

**Файли:** `Services/LocalizationInstaller.cs`

**Тести (v5.x):**
- [ ] Original snapshot створюється один раз
- [ ] Original snapshot НЕ перезаписується
- [ ] Restore point створюється перед replace
- [ ] Fallback: patch mismatch → помилка

---

### Етап 6: Transactional install + rollback

**Що реалізовано:**
- Повний workflow з rollback
- pre-operation snapshot перед replace

**Acceptance criteria:**
- [ ] Кроки: release → download → HTTP check → size check → SHA-256 → pre-operation snapshot (game file + installation state) → replace → verify installed file → save state → cleanup → commit success
- [ ] Ніколи не завантажувати поверх game file
- [ ] При помилці до replace — файл гри НЕ змінено, metadata НЕ стверджує install
- [ ] При помилці після replace — rollback відновлює game file + installation state до pre-operation стану
- [ ] Якщо rollback не вдався: стан `Corrupted`, критична помилка в UI, деталі в log
- [ ] Після невдалого rollback не записувати неправдивий successful state
- [ ] Metadata ТІЛЬКИ після успіху

**Файли:** `Services/LocalizationInstaller.cs`

**Тести (v6.x):**
- [ ] Transaction: replace + save state → rollback відновлює обидва
- [ ] Transaction: failure до replace → файл НЕ змінено
- [ ] Transaction: rollback failure → `Corrupted` стан

---

### Етап 7: Localization state/update detection + compatibility

**Що реалізовано:**
- Порівняння `installed.public_id` з `current.public_id`
- Визначення LocalizationState
- Перевірка `compatible_with_official_patch`

**Acceptance criteria:**
- [ ] `NotInstalled`: metadata відсутня
- [ ] `UpToDate`: `public_id` збігаються
- [ ] `UpdateAvailable`: `public_id` відрізняються
- [ ] `WaitingForRelease`: встановлено, `current` відсутній
- [ ] `InstalledVersionUnknown`: metadata нечитабельна
- [ ] `Corrupted`: hash не збігається
- [ ] `compatible_with_official_patch == false` → Install та Update заборонені
- [ ] Download не починається при несумісності
- [ ] Користувачу показується причина блокування
- [ ] Не використовувати лише `patch`/`version`

**Файли:** `Services/LocalizationStateService.cs`

**Тести (v7.x):**
- [ ] LocalizationState resolution: кожен стан
- [ ] Compatibility blocking: `compatible_with_official_patch == false`
- [ ] Не використовувати лише `patch`/`version`

---

### Етап 8: Basic WinForms UI

**Що реалізовано:**
- `MainForm` з базовим layout
- Відображення станів

**Acceptance criteria:**
- [ ] Одне вікно, вертикальний layout
- [ ] Блок Game Detection (пошук + ручний вибір)
- [ ] Блок Mode Selection (3 radio buttons)
- [ ] Блок Status (state + progress bar)
- [ ] Блок Actions (Install / Update / Restore Original / Restore Backup)
- [ ] UI без бізнес-логіки

**Файли:** `MainForm.cs`, `MainForm.Designer.cs`

---

### Етап 9: Підключення UI до services

**Що реалізовано:**
- Прив'язка UI до services
- User actions → service calls → UI update

**Acceptance criteria:**
- [ ] "Знайти гру" → `GameDetector.DetectAsync()` → UI update
- [ ] Вибір режиму → зберігається в config
- [ ] "Встановити" → `LocalizationInstaller.InstallAsync()` → progress → UI
- [ ] OperationState оновлює UI
- [ ] Помилки зрозумілою мовою

**Файли:** `MainForm.cs`

---

### Етап 10: Progress + cancellation UX

**Що реалізовано:**
- UI progress bar для download
- UI cancellation button
- Інтеграція з `CancellationToken` з Етапу 1

**Acceptance criteria:**
- [ ] Progress bar: % download
- [ ] "Скасувати" → cancellation → файл не змінено (до replace)
- [ ] OperationState оновлює UI

**Файли:** `MainForm.cs`, `Services/*.cs`

---

### Етап 11: Logging finalization

**Що реалізовано:**
- Повноцінне persistent file logging в `%LocalAppData%\BDO-UA-Client\logs\`

**Acceptance criteria:**
- [ ] Логи: запуск, detection, API calls, download, install, errors, rollback
- [ ] Формат: `{timestamp} [{level}] {message}`
- [ ] Ротація: по днях
- [ ] Logger contract визначено в Етапі 1, тут — реалізація

**Файли:** новий клас або `Program.cs`

---

### Етап 12: Release build + E2E перевірка

**Що реалізовано:**
- `dotnet publish -c Release -r win-x64 --self-contained true`
- Ручна перевірка

**Acceptance criteria:**
- [ ] `.exe` без .NET Runtime на чистій Windows
- [ ] Detection знаходить гру (Steam)
- [ ] API повертає дані
- [ ] Встановлення success → файл замінено
- [ ] Backup створено
- [ ] Оновлення працює
- [ ] Повернення до стану без української локалізації через Restore Original працює
- [ ] Логи записуються

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
| 6 | v6.x | v6.0 (transaction), v6.1 (rollback) |
| 7 | v7.x | v7.0 (state detection), v7.1 (compatibility) |
| 8 | v8.x | v8.0 (UI layout) |
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
