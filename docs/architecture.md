# Архітектура BDO-UA Client

## 1. Структура каталогів

```
BDO-PROGRAM/
├── Program.cs                  — Composition root: створення всіх залежностей, запуск MainForm
├── MainForm.cs                 — UI логіка (WinForms), обробка подій, координація сервісів
├── MainForm.Designer.cs        — WinForms designer: контрольні елементи та layout
├── BdoClient.csproj            — Проектний файл (.NET 8.0-windows, WinForms)
├── BdoUaClient.sln             — Solution файл
│
├── Api/
│   ├── ApiResult.cs            — Result pattern: ApiResult<T> Success/Error
│   └── BdoUaApiClient.cs       — HTTP клієнт для GET /releases (base URL, timeout, error handling)
│
├── Models/
│   ├── ReleasesResponse.cs     — Кореневий DTO відповіді API
│   ├── ReleaseData.cs          — Дані релізу (official_patch, modes, progress)
│   ├── LocalizationMode.cs     — Режим локалізації (slug, public_name, current, history)
│   ├── CurrentRelease.cs       — Поточний релізу (public_id, download_url, sha256, size_bytes)
│   ├── ReleaseHistoryItem.cs   — Елемент історії релізів
│   ├── InstallPathPattern.cs   — Патерн шляху встановлення (pattern, launcher)
│   ├── GameTestInfo.cs         — Статус тестування гри
│   ├── ProgressInfo.cs         — Прогрес перекладу (total_rows, translated_percent)
│   ├── StatsInfo.cs            — Статистика (rows_in_file)
│   ├── AnnouncementsInfo.cs    — Стан розсилок (discord, telegram)
│   ├── BackupMetadata.cs       — Метадані бекапу (original snapshot)
│   ├── RestorePointInfo.cs     — Інформація про restore point
│   └── RestoreResult.cs        — Результат операції відновлення
│
├── Services/
│   ├── GameDetector.cs         — Пошук гри: registry → Steam → patterns → ручний вибір
│   ├── LocalizationInstaller.cs — Встановлення: download → validate → backup → apply → verify
│   ├── LocalizationStateService.cs — Визначення LocalizationState (NotInstalled/UpToDate/...)
│   ├── LocalizationCompatibilityService.cs — Перевірка compatible_with_official_patch
│   ├── LocalizationInstallService.cs — Додатковий сервіс встановлення
│   ├── RestoreOriginalService.cs — Відновлення оригінального файлу (official_source_url / snapshot)
│   ├── RestoreBackupService.cs — Відновлення з restore point
│   ├── LocalizationState.cs    — Enum станів локалізації
│   ├── OperationState.cs       — Enum станів операції (Idle/Downloading/Installing/...)
│   ├── LocalizationStateResult.cs — Результат обчислення стану
│   ├── CompatibilityResult.cs  — Результат перевірки сумісності
│   ├── DetectionResult.cs      — Результат пошуку гри
│   ├── DownloadResult.cs       — Результат завантаження
│   ├── InstallResult.cs        — Результат встановлення
│   ├── InstallActionPolicy.cs  — Політика дій встановлення
│   ├── DynamicModePolicy.cs    — Політика динамічних режимів
│   └── HashHelper.cs           — SHA-256 хешування файлів
│
├── Storage/
│   ├── AppPaths.cs             — Шляхи до %LocalAppData%\BDO-UA-Client\ (config, state, logs, cache, backups)
│   ├── ConfigStore.cs          — Зчитування/збереження config.json
│   ├── Config.cs               — Модель конфігурації (game_path тощо)
│   ├── InstallationStateStore.cs — Зчитування/збереження state/installation.json
│   ├── InstallationMetadata.cs — Метадані встановленої локалізації (public_id, version, sha256)
│   ├── BackupStore.cs          — Управління бекапами (original snapshot + restore points)
│   └── FileLoadResult.cs       — Результат зчитування файлу
│
├── Logging/
│   ├── ILogger.cs              — Інтерфейс логера (Debug/Info/Warning/Error)
│   └── FileLogger.cs           — Реалізація: ротація логів, запис у файл
│
├── BdoClient.Tests/            — Юніт-тести (xUnit)
└── docs/                       — Документація
```

---

## 2. Composition Root

Весь граф залежностей створюється в `Program.cs` (Manual DI). DI-контейнер не використовується.

```
Program.Main()
│
├─ AppPaths                    — базові шляхи (%LocalAppData%\BDO-UA-Client\)
│   └─ EnsureDirectories()     — створення каталогів якщо відсутні
│
├─ FileLogger(appPaths)        — єдиний логер для всього застосунку
│
├─ ConfigStore(appPaths, logger)
├─ InstallationStateStore(appPaths, logger)
│
├─ HttpClient                  — ОДИН екземпляр, спільний для:
│   ├─ BdoUaApiClient(httpClient, logger)
│   └─ LocalizationInstaller(httpClient, appPaths, logger)
│
├─ GameDetector(configStore, logger)
├─ LocalizationStateService(stateStore, logger)
├─ LocalizationCompatibilityService()   — stateless, не потребує залежностей
│
└─ MainForm(configStore, apiClient, gameDetector,
            stateService, compatService,
            localizationInstaller, backupStore, stateStore, logger)
```

---

## 3. Dependency Graph

```
MainForm
├── ConfigStore ──────────── AppPaths, ILogger
├── BdoUaApiClient ──────── HttpClient, ILogger
├── GameDetector ─────────── ConfigStore, ILogger
├── LocalizationStateService ── InstallationStateStore, ILogger
├── LocalizationCompatibilityService (stateless)
├── LocalizationInstaller ── HttpClient, AppPaths, ILogger
├── BackupStore ──────────── AppPaths, ILogger
├── InstallationStateStore ── AppPaths, ILogger
└── ILogger (FileLogger)

AppPaths ─── (no dependencies, reads %LocalAppData%)
FileLogger ── AppPaths.LogsDir
```

**Правило:** сервіси не залежать один від одного напряму. Координація відбувається в MainForm.

---

## 4. Runtime Data: %LocalAppData%\BDO-UA-Client\

```
%LocalAppData%\BDO-UA-Client\
├── config.json                    — налаштування користувача (game_path)
├── state/
│   └── installation.json          — стан встановленої локалізації
│                                    (public_id, version, sha256, installed_at, mode_slug, source)
├── logs/
│   └── bdo-client-YYYY-MM-DD.log — щоденні логи з ротацією
├── cache/                         — тимчасові завантажені файли (.tmp/.download)
└── backups/
    ├── original/                  — original snapshot (незмінна копія до першої модифікації)
    │   ├── languagedata_en.loc    — копія оригінального файлу
    │   └── metadata.json          — created_at, sha256, size_bytes
    └── restore-points/            — попередні встановлені локалізації
        └── {public_id}/
            ├── languagedata_en.loc
            └── metadata.json
```

**Примітки:**
- `config.json` зберігає шлях до гри, знайдений через auto-detection або ручний вибір.
- `installation.json` оновлюється ТІЛЬКИ після успішного встановлення (post-verify).
- `backups/original/` створюється один раз і ніколи не перезаписується.
- `backups/restore-points/` — попередні версії локалізації для rollback.

---

## 5. Shared HttpClient

Один екземпляр `HttpClient` створюється в `Program.cs` та передається в два сервіси:

```
HttpClient (singleton)
├── BdoUaApiClient     — GET /releases (API запити, JSON)
└── LocalizationInstaller — GET download_url (завантаження .loc файлів)
```

**Переваги:**
- Переиспользование TCP-з'єднань (connection pooling).
- Уникнення socket exhaustion.
- Спільний timeout та default headers.

**Примітка:** `HttpClient` не dispose-иться окремо — він живе весь час роботи застосунку.
