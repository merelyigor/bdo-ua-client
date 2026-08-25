# Архітектура BDO-UA Client

## 1. Структура каталогів

```
BDO-PROGRAM/
├── Program.cs                  — Composition root: normal mode + --apply-update helper mode
├── MainForm.cs                 — UI логіка (WinForms), обробка подій, координація сервісів
├── MainForm.Designer.cs        — WinForms designer: контрольні елементи та layout
├── BdoClient.csproj            — Проектний файл (.NET 8.0-windows, WinForms)
├── BdoUaClient.sln             — Solution файл
│
│   ── UI-компоненти (корінь) ──
├── BdoSurfacePanel.cs          — Кастомна панель поверх BDO-фону (тема)
├── BdoProgressBar.cs           — Кастомний progress bar із семантичними станами
├── LocalizationModeCard.cs     — Клікабельна картка режиму локалізації (замість RadioButton)
├── ModeCardPresentation.cs     — Політика презентації карток режимів (ModeCardPresentationPolicy)
├── UiTheme.cs                  — BDO-тема: кольори, шрифти, масштабування
├── WindowChromeHelper.cs       — Custom title bar / window chrome
├── UpdateApplyingForm.cs       — UI helper mode (--apply-update)
├── InstallButtonLabelPolicy.cs — Контекстний текст кнопки («Встановити»/«Оновити»/«✓ Встановлено»)
├── LocalizationFlagParser.cs   — Парсинг UA/GB прапорців для карток режимів
├── ThemePrototype.cs.reference.txt — Історичний референс теми (не компілюється)
│
├── Api/
│   ├── ApiResult.cs            — Result pattern: ApiResult<T> Success/Error
│   ├── BdoUaApiClient.cs       — HTTP клієнт GET /releases + WarmupConnectionAsync
│   ├── BdoUaHttpClientConfiguration.cs — Конфігурація HttpClient (User-Agent, proxy, handler)
│   ├── NetworkDiagnostics.cs   — Форматування мережевих помилок для логів
│   └── ResilientConnectionConnector.cs — Happy-eyeballs TCP connect (parallel DNS attempts)
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
│   └── HashHelper.cs           — SHA-256 хешування та захищене копіювання файлів
│
├── Update/                     — Self-update клієнта (Stage 13, див. docs/update.md)
│   ├── ApplicationCommandLine.cs — Парсинг --apply-update <session-id>
│   ├── AppVersion.cs / AppVersionInfo.cs — Numeric версія та детекція поточної версії EXE
│   ├── GitHubRelease.cs / GitHubResult.cs / GitHubUpdateClient.cs — Клієнт GitHub Releases (без токена)
│   ├── UpdateSelectionPolicy.cs — Вибір релізу: numeric comparison + channel policy
│   ├── UpdateManifest.cs / UpdateManifestValidator.cs — Schema-2 manifest + валідація
│   ├── ExecutableVersionValidator.cs — Перевірка версії staged EXE
│   ├── UpdatePackageService.cs / UpdatePackageResult.cs — Завантаження та розпакування ZIP
│   ├── ReplacementWorkspace.cs  — Staging-директорія для candidate EXE
│   ├── PreparedAttemptCleanup.cs — Очищення незавершених сесій оновлення
│   ├── UpdateSession.cs / UpdateSessionStore.cs — Сесійний стан у updates/<GUID>/
│   ├── SelfUpdatePreparationService.cs — Підготовка сесії (download → validate → stage)
│   ├── SelfUpdateApplier.cs     — Helper mode: заміна EXE + restart, exit codes
│   ├── StartupUpdateLifecycleCoordinator.cs — Startup maintenance (cleanup)
│   ├── UpdateLifecycleService.cs — Координація перевірки/підготовки оновлення
│   ├── UpdateButtonState.cs     — Стани кнопки «Оновити до vX.Y.Z»
│   └── ForegroundWindowHelper.cs — Допоміжний клас для фокусу вікон
│   ├── LocalizationStatePresentation.cs — UI-тексти станів локалізації
│   ├── ApiErrorPresentation.cs — ApiErrorKind → українські UI повідомлення
│   ├── AdsFilesPatchReader.cs  — Читання патчу гри з ads_files
│   ├── ReleaseFeedPoller.cs    — Background polling /releases (15 с)
│   ├── FeedChangeDetector.cs   — Семантичне порівняння feed-кандидатів
│   ├── FeedApplicationCoordinator.cs — Застосування feed-змін (pending черга)
│   └── StartupCoordinator.cs   — Паралельний startup: API + local detection
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

### Normal mode (`RunNormalMode`)

```
Program.Main()
│
├─ ApplicationCommandLine.Parse(args)
│   └─ --apply-update <session-id> → RunHelperMode() (див. docs/update.md)
│
├─ AppPaths                    — базові шляхи (%LocalAppData%\BDO-UA-Client\)
│   └─ EnsureDirectories()     — створення каталогів якщо відсутні
│
├─ FileLogger(appPaths)        — єдиний логер для всього застосунку
│
├─ ConfigStore(appPaths, logger)
├─ InstallationStateStore(appPaths, logger)
├─ AppVersionInfo.Detect()     — версія поточного EXE
│
├─ HttpClient                  — ОДИН екземпляр через BdoUaHttpClientConfiguration:
│   ├─ BdoUaApiClient(httpClient, logger)
│   └─ LocalizationInstaller(httpClient, appPaths, logger)
│
├─ GameDetector(configStore, logger)
├─ LocalizationStateService(stateStore, logger)
├─ LocalizationCompatibilityService()   — stateless, не потребує залежностей
│
├─ GitHub HttpClient           — ОКРЕМИЙ HttpClient (UseProxy = false):
│   └─ GitHubUpdateClient → UpdateSelectionPolicy
│
└─ MainForm(configStore, apiClient, gameDetector,
            stateService, compatService,
            localizationInstaller, backupStore, stateStore, logger,
            appVersionInfo, gitHubClient, selectionPolicy, appPaths)
```

MainForm всередині себе додатково створює: `UpdateSessionStore`, `UpdateManifestValidator`, `UpdatePackageService`, `SelfUpdatePreparationService`, `UpdateLifecycleService`, feed-сервіси (`ReleaseFeedPoller`, `FeedApplicationCoordinator`) та `StartupCoordinator`.

---

## 3. Dependency Graph

```
MainForm
├── ConfigStore ─────────────── AppPaths, ILogger
├── BdoUaApiClient ──────────── HttpClient, ILogger
├── GameDetector ────────────── ConfigStore, ILogger
├── LocalizationStateService ── InstallationStateStore, ILogger
├── LocalizationCompatibilityService (stateless)
├── LocalizationInstaller ───── HttpClient, AppPaths, ILogger
├── BackupStore ─────────────── AppPaths, ILogger
├── InstallationStateStore ──── AppPaths, ILogger
├── GitHubUpdateClient ──────── GitHub HttpClient, ILogger
├── UpdateLifecycleService ──── GitHubUpdateClient, SelectionPolicy, PreparationService, ...
└── ILogger (FileLogger)

AppPaths ─── (no dependencies, reads %LocalAppData%)
FileLogger ── AppPaths.LogsDir
```

**Правило:** сервіси localization-домену не залежать один від одного напряму. Self-update утворює власну ієрархію (`UpdateLifecycleService` координує preparation/applier). Координація відбувається в MainForm.

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
└── updates/                          — self-update сесії (Stage 13)
    └── {GUID}/                       — одна сесія оновлення
        ├── update-session.json       — стан сесії (див. docs/update.md)
        └── ...                       — staged candidate EXE та manifest
```

**Примітки:**
- `config.json` зберігає шлях до гри, знайдений через auto-detection або ручний вибір.
- `installation.json` оновлюється ТІЛЬКИ після успішного встановлення (post-verify).
- `backups/original/` створюється один раз і ніколи не перезаписується.
- `backups/restore-points/` — попередні версії локалізації для rollback.
- `updates/<GUID>/` — staged candidate нового EXE; current EXE не змінюється до повної верифікації.

---

## 5. HttpClient instances

Створюються в `Program.cs`, два окремі екземпляри:

```
HttpClient #1 (BdoUaHttpClientConfiguration.CreateHttpClient)
│   SocketsHttpHandler + ResilientConnectionConnector (happy-eyeballs connect)
│   User-Agent: BdoUaClient/<version> (+https://bdo-ua.com.ua), UseProxy = false
├── BdoUaApiClient          — GET /releases (API запити, JSON)
└── LocalizationInstaller   — GET download_url / official_source_url

HttpClient #2 (GitHub updater)
│   HttpClientHandler, UseProxy = false, без токена
└── GitHubUpdateClient      — GitHub Releases API (self-update)
```

**Переваги спільного HttpClient #1:**
- Переиспользование TCP-з'єднань (connection pooling).
- Уникнення socket exhaustion.
- Спільний timeout та default headers.

**Примітка:** `HttpClient` не dispose-иться окремо — живе весь час роботи застосунку.
