# План реалізації bdo-ua-client

## Техстек

| Компонент | Вибір | Обґрунтування |
|---|---|---|
| Мова | C# 12 / .NET 8 | Нативний Windows .exe, WinForms |
| UI | WinForms | Простіше для .exe, менше залежностей |
| HTTP | `HttpClient` (built-in) | Стандартна бібліотека |
| Serialization | `System.Text.Json` | Вбудований, швидкий |
| Hash | `System.Security.Cryptography` | SHA-256 вбудований |
| Side dependencies | **Немає** | Все з .NET 8 SDK |

---

## Архітектура (source code)

```
bdo-ua-client/
├── Program.cs
├── MainForm.cs
├── MainForm.Designer.cs
│
├── Api/
│   └── BdoUaApiClient.cs
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
└── app.manifest
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
- [ ] API client повертає `null` або `ApiResult<T>` замість виключення при помилці мережі
- [ ] SHA-256 НЕ використовується для official source (лише для release downloads)

**Файли:** `BdoClient.csproj`, `Models/*.cs`, `Api/BdoUaApiClient.cs`

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

---

### Етап 5: Backup + restore logic

**Що реалізовано:**
- Original backup: ОДИН РАЗ перед першою модифікацією
- Restore points: кожна нова встановлена локалізація
- Restore original: download з `official_source_url` + заміна

**Acceptance criteria:**
- [ ] Original: `%LocalAppData%\BDO-UA-Client\backups\original\`
- [ ] Original НЕ перезаписується
- [ ] Restore points: `%LocalAppData%\BDO-UA-Client\backups\restore-points\{timestamp}\`
- [ ] Кожен restore point: `languagedata_en.loc` + `metadata.json`
- [ ] Restore original: download → replace → metadata `source: "official"`
- [ ] При відсутності original + official → помилка

**Файли:** `Services/LocalizationInstaller.cs`

---

### Етап 6: Safe installation transaction

**Що реалізовано:**
- Повний workflow: download → verify → backup → replace → verify → save state → cleanup

**Acceptance criteria:**
- [ ] Ніколи не завантажувати поверх game file
- [ ] Кроки: release → download → HTTP check → size check → SHA-256 → backup → replace → verify → save state → cleanup
- [ ] При помилці — файл гри НЕ змінено
- [ ] Metadata ТІЛЬКИ після успіху
- [ ] При помилці metadata НЕ стверджує install

**Файли:** `Services/LocalizationInstaller.cs`

---

### Етап 7: Localization state/update detection

**Що реалізовано:**
- Порівняння `installed.public_id` з `current.public_id`
- Визначення LocalizationState

**Acceptance criteria:**
- [ ] `NotInstalled`: metadata відсутня
- [ ] `UpToDate`: `public_id` збігаються
- [ ] `UpdateAvailable`: `public_id` відрізняються
- [ ] `WaitingForRelease`: встановлено, `current` відсутній
- [ ] `InstalledVersionUnknown`: metadata нечитабельна
- [ ] `Corrupted`: hash не збігається
- [ ] Не використовувати лише `patch`/`version`

**Файли:** `Services/LocalizationStateService.cs`

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

### Етап 10: Progress + cancellation + error handling

**Що реалізовано:**
- Progress reporting для download
- Cancellation token
- Error handling без `catch/pass`

**Acceptance criteria:**
- [ ] Progress bar: % download
- [ ] `CancellationToken` в усіх async операціях
- [ ] "Скасувати" → cancellation → файл не змінено
- [ ] Кожна помилка логується + показується
- [ ] Немає порожніх `catch/pass`

**Файли:** `MainForm.cs`, `Services/*.cs`

---

### Етап 11: Logging

**Що реалізовано:**
- File-based logging в `%LocalAppData%\BDO-UA-Client\logs\`

**Acceptance criteria:**
- [ ] Логи: запуск, detection, API calls, download, install, errors
- [ ] Формат: `{timestamp} [{level}] {message}`
- [ ] Ротація: по днях

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
- [ ] Відновлення оригіналу працює
- [ ] Видалення працює
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
| 9 | Несумісність патчу | Попередити, але дозволити |
| 10 | Concurrent access | File lock на metadata |

---

## Порядок виконання

Не реалізовувати всі етапи за один раз.

Після кожного етапу:
1. `dotnet build`
2. Виправити compile errors
3. Коротко описати що реалізовано
4. Перелічити зміни у файлах
5. Вказати що перевірено
6. Зупинитись і дочекатись наступної команди
