# AGENTS.md

---

## 📑 ЗМІСТ

| § | Розділ | Опис |
|---|---|---|
| [§1](#1--мова)] | 🌍 Мова | Спілкування, код, документація |
| [§2](#2--призначення) | 🎯 Призначення | Що робить застосунок |
| [§3](#3--принцип-роботи) | 🧠 Принцип роботи | Як агент підходить до задач |
| [§4](#4--пріоритети) | ⚖️ Пріоритети | Порядок важливості |
| [§5](#5--код) | 💻 Код | Як писати код |
| [§6](#6--не-ускладнювати) | 🧩 Не ускладнювати | Зайві абстракції |
| [§7](#7--архітектура) | 🏗️ Архітектура | Розділення відповідальності |
| [§8](#8--ui) | 🖥️ UI | Межі UI шару |
| [§9](#9--не-блокувати-ui) | ⚡ Не блокувати UI | Async/background |
| [§10](#10--api) | 🔌 API | Взаємодія з сервером |
| [§11](#11--api-contract-bdo-uacomua) | 📋 API Contract | Структура відповідей |
| [§12](#12--secrets) | 🔐 Secrets | Заборонені дані |
| [§13](#13--пошук-гри) | 🔍 Пошук гри | Game detection |
| [§14](#14--файли-ta-backup) | 💾 Файли та backup | Захист файлів, бекапи |
| [§15](#15--api-release-metadata) | 📦 API Release Metadata | Installation workflow |
| [§16](#16--захист-шляхів) | 🛡️ Захист шляхів | Path traversal |
| [§17](#17--download) | ⬇️ Download | Завантаження файлів |
| [§18](#18--стани-та-оновлення) | 🔄 Стани та оновлення | LocalizationState / OperationState |
| [§19](#19--видалення) | 🗑️ Видалення | Uninstall |
| [§20](#20--windows) | 🪟 Windows | Платформа |
| [§21](#21--ux) | 🎨 UX | Інтерфейс |
| [§22](#22--логування-та-exceptions) | 📝 Логування та exceptions | Обробка помилок |
| [§23](#23--конфігурація-та-cache) | ⚙️ Конфігурація та cache | Збереження даних |
| [§24](#24--мережа) | 🌐 Мережа | Retry, timeout |
| [§25](#25--залежності) | 📚 Залежності | Бібліотеки |
| [§26](#26--build-ta-exe) | 📦 Build та .exe | Збірка |
| [§27](#27--тести) | 🧪 Тести | Тестування |
| [§28](#28--security) | 🔒 Security | Захист |
| [§29](#29--сумісність) | 🔗 Сумісність | Patch compatibility |
| [§30](#30--правила-змін) | ✏️ Правила змін | Як вносити зміни |
| [§31](#31--коментарі-та-документація) | 📄 Коментарі та документація | Як документувати |
| [§32](#32--не-вигадувати) | 🚫 Не вигадувати | Що заборонено |
| [§33](#33--definition-of-done) | ✅ Definition of Done | Коли задача виконана |
| [§34](#34--звіт-та-коміти) | 📊 Звіт та коміти | Як комітити |
| [§35](#35--публічний-репозиторій) | 🔐 Публічний репозиторій | Безпека даних |
| [§36](#36--заборони) | 🚫 Заборони | Що не можна |
| [§37](#37--правило-сумнівів) | 🤔 Правило сумнівів | Надійність > швидкість |
| [§38](#38--головний-принцип) | 💎 Головний принцип | Головне правило |

---

## §1 🌍 Мова

§1.1 Спілкування, коментарі, звіти — **тільки українською**.

§1.2 Англійська дозволена для: назв технологій, API, бібліотек, ідентифікаторів, системних термінів.

§1.3 Код — англійськими назвами змінних/функцій.

§1.4 Не змішувати українську з російською. Російську не використовувати.

---

## §2 🎯 Призначення

§2.1 Windows `.exe` застосунок для встановлення локалізацій у комп'ютерні ігри.

§2.2 Можливості:
- Автоматичний/ручний пошук гри
- Валідація директорії
- Отримання локалізацій через API
- Завантаження, встановлення, оновлення
- Перевірка стану, видалення/відновлення
- Повідомлення про помилки

---

## §3 🧠 Принцип роботи

§3.1 Агент працює як інженер: зрозуміти структуру → знайти пов'язані файли → зрозуміти data flow → визначити мінімум змін → редагувати.

§3.2 Не створювати нову архітектуру без потреби.

§3.3 Не переписувати великі частини заради дрібної зміни.

---

## §4 ⚖️ Пріоритети

§4.1 Коректність.

§4.2 Безпека даних.

§4.3 Неможливість пошкодження файлів гри.

§4.4 Стабільність.

§4.5 Простота підтримки.

§4.6 Зрозуміла архітектура.

§4.7 Хороший UX.

§4.8 Продуктивність.

§4.9 Краса коду.

§4.10 Не жертвувати коректністю заради коротшого коду.

§4.11 Не жертвувати надійністю заради швидкого завершення задачі.

---

## §5 💻 Код

§5.1 Код повинен бути: простим, читабельним, передбачуваним, типізованим.

§5.2 Розділений за відповідальністю.

§5.3 Без зайвих абстракцій та дублювання.

§5.4 Зрозумілі назви: `game_installation_path` ≠ `data`, `temp`, `obj`.

---

## §6 🧩 Не ускладнювати

§6.1 Не використовувати складні patterns без реальної потреби.

§6.2 Не створювати: зайві interfaces, зайві abstract classes, фабрики для одного об'єкта, dependency injection без необхідності, надмірно глибоку ієрархію класів.

§6.3 Кожна абстракція повинна вирішувати конкретну проблему.

§6.4 Якщо просте рішення достатнє — використовувати просте рішення.

---

## §7 🏗️ Архітектура

§7.1 Не змішувати в одному місці: UI / API / файлову роботу / пошук гри / установку / конфігурацію / логування.

§7.2 Бажане розділення:
```
UI → Application/Services → API Client → Game Detection
                                         → Localization Installer
                                         → File System
                                         → Configuration / Logging / Models
```

---

## §8 🖥️ UI

§8.1 UI показує інформацію та викликає service-методи.

§8.2 UI **не** повинен: будувати API-запити, копіювати файли, перевіряти структуру гри, реалізовувати алгоритм встановлення.

---

## §9 ⚡ Не блокувати UI

§9.1 Довгі операції (HTTP, download, scan, розпакування, копіювання, checksum) — в async/background.

§9.2 UI має залишатися responsive.

---

## §10 🔌 API

§10.1 Вся взаємодія через окремий API client (base URL, headers, timeout, serialization, error handling).

§10.2 Не вважати API ідеальним: обробляти timeout, DNS error, 4xx/5xx, порожню відповідь, malformed JSON.

§10.3 Відповіді перетворювати на моделі через `ApiResult<T>`. `null` НЕ використовується як generic signal failure.

§10.4 Base URL — централізовано (Config/Environment).

§10.5 API версію враховувати явно.

§10.6 API-контракт не є статичним. Сервер може додавати нові поля, змінювати структуру, вводити нові endpoint-и. При зміні API потрібно:
1. Оновити моделі клієнта.
2. Оновити розділ §11.
3. Перевірити backward/forward compatibility.

---

## §11 📋 API Contract: bdo-ua.com.ua

Base URL: `https://bdo-ua.com.ua/api/public/v1`

**GET /releases** — єдиний endpoint. Клієнт не повинен сам вирішувати, яка локалізація актуальна.

§11.1 Структура відповіді:
```json
{
  "success": true,
  "generated_at": "ISO datetime",
  "data": {
    "official_patch": int,
    "official_patch_checked_at": "ISO datetime",
    "official_source_url": "https://naeu-o-dn.playblackdesert.com/.../languagedata_en.loc",
    "filename": "languagedata_en.loc",
    "install_path_patterns": [
      { "pattern": "{drive}:\\...\\Black Desert Online\\ads\\", "launcher": "steam|official", "description": "..." }
    ],
    "install_guide_url": "https://bdo-ua.com.ua/download",
    "progress": { "total_rows", "translated_percent", "manual_rows", "manual_percent", "machine_rows", "machine_percent" },
    "modes": [
      {
        "slug": "full-ukrainian|full-ukrainian-bosia|english-items",
        "public_name": "...",
        "description": "...",
        "audience": "...",
        "current": {
          "public_id": "ULID (26 chars)",
          "version": int,
          "filename": "languagedata_en.loc",
          "download_url": "https://bdo-ua.com.ua/download/releases/{public_id}",
          "size_bytes": int,
          "sha256": "hex string",
          "patch": int,
          "compatible_with_official_patch": bool,
          "published_at": "ISO datetime",
          "game_tested_at": "ISO datetime",
          "game_test": { "state": "known_issues|ok|...", "label": "...", "note": "string|null" },
          "stats": { "rows_in_file": int },
          "announcements": { "discord_releases": {"sent": bool, "sent_at": str|null}, "telegram_main": {"sent": bool, "sent_at": str|null} }
        },
        "history": [ { "public_id", "version", "patch", "status", "published_at", "retired_at" } ]
      }
    ]
  }
}
```

§11.2 `current` відсутній для режиму — нормальний стан (актуальний release ще не опубліковано). `current` є nullable. `current == null` — валідний бізнес-стан, не deserialization/API error.

§11.3 `history` використовувати лише для інформації. Завантажувати старі версії ЗАБОРОНЕНО.

§11.4 `official_source_url` — для відновлення оригіналу. SHA-256 для нього НЕ надається.

§11.5 `install_path_patterns` — лише hints для detection, не довірені filesystem instructions.

§11.6 Download: прямий (без redirect), авторизація не потрібна.

§11.7 `progress` — глобальний для всіх режимів. `stats.rows_in_file` може відрізнятися від `progress.total_rows`.

§11.8 Доступні режими (slug):
- `full-ukrainian` — повна українська (Bosia + правки спільноти)
- `full-ukrainian-bosia` — повна українська лише від Bosia
- `english-items` — українські тексти з англійськими назвами предметів

§11.9 Статуси history: `superseded` / `withdrawn` ( `current` ніколи не з'являється в history ).

---

## §12 🔐 Secrets

§12.1 Ніколи не додавати в репозиторій: API keys, tokens, passwords, signing secrets, credentials.

§12.2 Desktop `.exe` не може надійно приховати секрет.

---

## §13 🔍 Пошук гри

§13.1 Game detection — окремий модуль.

§13.2 Порядок: збережений шлях → registry → Steam libraryfolders → appmanifest_582660.acf → `install_path_patterns` з API (hints) → ручний вибір.

§13.3 Steam detection: читати `libraryfolders.vdf`, знаходити `appmanifest_582660.acf`, витягувати `installdir`.

§13.4 `install_path_patterns` з API — лише hints для перебору дисків, не довірені filesystem instructions.

§13.5 Директорія валідується: наявність `{game_path}\ads\languagedata_en.loc`.

§13.6 Ручний вибір завжди доступний.

---

## §14 💾 Файли та backup

§14.1 Перед зміною/видаленням файлу знати, як його відновити.

§14.2 Бекап створювати один раз, не перезаписувати good backup модифікованим файлом.

§14.3 Атомарна установка: download у temp → validation → backup → apply → verify → cleanup.

§14.4 Temporary files: `.tmp`/`.download`, переміщати після перевірки.

§14.5 Перевірка hash (SHA-256) при наявності від сервера.

§14.6 **Розділення backup:**
- **Original snapshot** — незмінна копія `languagedata_en.loc`, яка існувала перед першою модифікацією клієнтом. Не перезаписувати. Не трактувати як гарантовано актуальний original після майбутніх патчів гри.
- **Restore points** — попередні встановлені локалізації. Створюються ПЕРЕД заміною game file (pre-operation snapshot). Не вважати їх оригінальним game file.

§14.7 **Metadata original snapshot:** `created_at`, `game_patch` (якщо достовірно), `sha256` (локально, не з API), `size_bytes`.

§14.8 **Чотири операції:**
1. `Встановити` — перша установка
2. `Оновити` — заміна на новіший release
3. `Відновити оригінал` — у першу чергу завантаження з `official_source_url`; локальний original snapshot — fallback
4. `Відновити backup` — повернення до попереднього restore point

§14.9 Не видаляти `languagedata_en.loc` фізично як спосіб uninstall. Повернення до стану без української = відновлення official/original `.loc`.

---

## §15 📦 API Release Metadata

API надає release metadata через `GET /releases`. Клієнт виконує валідовані операції.

§15.1 **Installation Safety Workflow:**
1. Отримати release з API
2. Завантажити у cache/temp
3. Перевірити HTTP result
4. Перевірити `size_bytes` (якщо доступний)
5. Перевірити SHA-256 (для release files; для official source — ні)
6. Створити pre-operation snapshot/restore point
7. Підготувати заміну
8. Замінити game file
9. Перевірити встановлений файл
10. Записати installation state ТІЛЬКИ після успіху
11. Видалити temporary file
12. Commit success

§15.2 Якщо помилка на будь-якому кроці до replace — файл гри НЕ змінено, metadata НЕ стверджує, що release встановлено.

§15.3 Якщо помилка сталася після replace — клієнт повинен спробувати rollback до pre-operation snapshot. Якщо rollback не вдався:
- не заявляти успішну установку;
- стан вважати пошкодженим (`Corrupted`);
- показати користувачу критичну помилку;
- записати деталі в log.

---

## §16 🛡️ Захист шляхів

§16.1 Path traversal protection: нормалізація + перевірка, що файл в межах дозволеної директорії.

§16.2 Не довіряти filenames від API: не писати в system directories, не запускати executable.

---

## §17 ⬇️ Download

§17.1 Підтримка: timeout, progress, cancellation, error handling, partial cleanup, hash check, streaming для великих файлів.

---

## §18 🔄 Стани та оновлення

§18.1 Перевіряти фактичний стан файлів, а не лише config flag.

§18.2 Перед update: поточна версія, серверна версія, сумісність.

§18.3 `compatible_with_official_patch == false` → Install та Update заборонені, download не починається.

§18.4 **LocalizationState (постійний стан файлу):**
- `NotInstalled` — файл не встановлено
- `UpToDate` — `installed.public_id == current.public_id`
- `UpdateAvailable` — `installed.public_id != current.public_id`
- `WaitingForRelease` — встановлено, але `current` відсутній
- `InstalledVersionUnknown` — metadata немає або нечитабельна
- `Corrupted` — hash не збігається

§18.5 **OperationState (тимчасовий стан операції):**
- `Idle` / `DetectingGame` / `LoadingApi` / `Downloading` / `Verifying`
- `BackingUp` / `Installing` / `Restoring` / `Completed` / `Failed` / `Cancelled`

§18.6 Основний ідентифікатор release — `public_id`, а не `patch` чи `version`.

---

## §19 🗑️ Видалення

§19.1 **Розділення backup:**
- **Original snapshot** — незмінна копія `languagedata_en.loc`, яка існувала перед першою модифікацією клієнтом. Не перезаписувати. Не трактувати як гарантовано актуальний original після майбутніх патчів гри.
- **Restore points** — попередні встановлені локалізації. Створюються ПЕРЕД заміною game file (pre-operation snapshot). Не вважати їх оригінальним game file.

§19.2 **Metadata original snapshot:** `created_at`, `game_patch` (якщо достовірно), `sha256` (локально, не з API), `size_bytes`.

§19.3 **Чотири операції:**
1. `Встановити` — перша установка
2. `Оновити` — заміна на новіший release
3. `Відновити оригінал` — у першу чергу завантаження з `official_source_url`; локальний original snapshot — fallback ТІЛЬКИ якщо `snapshot.game_patch == current.official_patch`
4. `Відновити backup` — повернення до попереднього restore point

§19.4 Не видаляти `languagedata_en.loc` фізично як спосіб uninstall. Повернення до стану без української = відновлення official/original `.loc`.

---

## §20 🪟 Windows

§20.1 Коректні Windows paths, Unicode, пробіли, permissions, read-only/locked files.

§20.2 Не вимагати Admin без потреби. Permission error → зрозуміле повідомлення.

---

## §21 🎨 UX

§21.1 Простий інтерфейс: де гра, яка локалізація, версія, стан, прогрес, результат.

§21.2 Помилки — зрозумілою мовою, деталі в log.

---

## §22 📝 Логування та exceptions

§22.1 Структуроване логування (DEBUG/INFO/WARNING/ERROR).

§22.2 Логувати: запуск, detection, API errors, download failures, installation stages, exceptions.

§22.3 Не логувати: passwords, tokens, secrets.

§22.4 **Заборонено** порожні `catch/pass`. Кожна помилка обробляється/логується/re-throw.

---

## §23 ⚙️ Конфігурація та cache

§23.1 User settings у Windows user-data location (не поруч з `.exe` в Program Files).

§23.2 Cache окремо від game files. Не змішувати cache/backup/config/logs/game files.

§23.3 **Структура даних (%LocalAppData%\BDO-UA-Client\):**
```
BDO-UA-Client/
├── config.json
├── state/
│   └── installation.json
├── logs/
├── cache/
└── backups/
    ├── original/
    └── restore-points/
```

---

## §24 🌐 Мережа

§24.1 Retry тільки для безпечних операцій, з backoff та лімітом.

§24.2 Кожна мережева операція має timeout.

---

## §25 📚 Залежності

§25.1 Не додавати dependency, якщо standard library справляється.

§25.2 Не змінювати framework/build system/package manager без потреби.

---

## §26 📦 Build та .exe

§26.1 Після змін: syntax check → build → tests → виправлення.

§26.2 Пакетований `.exe` може відрізнятися від dev mode (working directory, bundled resources).

---

## §27 🧪 Тести

§27.1 Тестувати без UI: path validation, release metadata parsing, checksum, game detection, version comparison.

§27.2 Tests використовують temp directories, не працюють з реальними game files.

§27.3 Edge cases: гра не знайдена, кілька копій, Unicode/пробіли, locked files, переповнений диск, перерваний download, пошкоджені файли.

§27.4 Test project: `BdoClient.Tests`. Framework: xUnit (test-only NuGet dependency). Не тестувати WinForms layout. TargetFramework: `net8.0-windows` (для ProjectReference на WinForms app).

§27.5 Тести додавати разом з testable logic (Етапи 1-7), а не відкладати все до фіналу.

---

## §28 🔒 Security

§28.1 Усе зовнішнє — недовірене: API responses, filenames, URLs, manifests.

§28.2 TLS verification ніколи не вимикати.

§28.3 Не запускати executable отриманий з сервера (окрім whitelist операцій: copy/replace/delete localization file/create dir/restore backup).

§28.4 Невідомі operation відхиляти.

---

## §29 🔗 Сумісність

§29.1 `compatible_with_official_patch == false` → Install та Update заборонені. Download не починається. Користувачу показується причина. Ніякого override в MVP.

---

## §30 ✏️ Правила змін

§30.1 Мінімальний patch. Не змішувати feature/refactor/formatting.

§30.2 Не чіпати непов'язаний код.

§30.3 Refactor не змінює поведінку ненавмисно.

§30.4 Не залишати TODO замість реалізації.

§30.5 Не підміняти реалізацію placeholder/mock.

§30.6 Не hardcode: шляхи, usernames, tokens, credentials.

---

## §31 📄 Коментарі та документація

§31.1 Коментарі пояснюють **чому**, а не **що**.

§31.2 Оновлювати документацію при зміні build/config/API/structure.

§31.3 Невідомий код — дослідити перед зміною.

---

## §32 🚫 Не вигадувати

§32.1 Використовувати існуючий API contract.

§32.2 Не вигадувати API бібліотек.

§32.3 Складні задачі — маленькими кроками.

---

## §33 ✅ Definition of Done

Задача виконана, коли:

§33.1 Реалізована функціональність без placeholder.

§33.2 Оброблені error cases.

§33.3 UI responsive.

§33.4 Файлові операції захищені.

§33.5 Код відповідає архітектурі.

§33.6 `dotnet build BdoUaClient.sln` проходить без помилок.

§33.7 `dotnet test BdoUaClient.sln --no-build` проходить (якщо є тести).

---

## §34 📊 Звіт та коміти

§34.1 Коротко: що змінено, ключові файли, що перевірено, build/tests, обмеження. Не заявляти "все працює" без перевірки.

§34.2 **Правила комітів:**
- Формат: `v{ЕТАП}.{ПІДЕТАП} — {короткий опис}`
- Кожен завершений етап/підетап — окремий коміт
- Опис українською + список змінених файлів
- Перед комітом: `dotnet build` без помилок
- Після коміту: одразу push
- Деталі в `plan.md` (розділ "Правила комітів та версійності")

§34.3 **Звіт після коміту/пушу — ОБОВ'ЯЗКОВИЙ.** Після кожного коміту та пушу агент повинен чітко повідомити:

1. **Що закомічено:** короткий опис змін
2. **Який коміт message:** повний текст коміту
3. **Які файли змінено:** список
4. **Що запушено:** підтвердження push + branch
5. **Hash коміту:** короткий hash

Приклад звіту:
```
✅ Коміт створено та запушено.

📝 Commit message: v1.0 — project skeleton + API models

📁 Змінені файли:
- BdoClient.csproj
- Models/ReleasesResponse.cs
- Models/LocalizationMode.cs
- Api/BdoUaApiClient.cs
- Api/ApiResult.cs

🔖 Hash: a1b2c3d
🌿 Branch: main → origin/main
```

§34.4 Не залишати незакомічені файли без уваги. Якщо є untracked/modified файли — агент повинен або закомітити, або явно повідомити про них.

---

## §35 🔐 Публічний репозиторій

§35.1 Репозиторій **публічний**. Все, що потрапляє в git, стає доступним для всіх.

§35.2 **Перед кожним комітом агент зобов'язаний перевірити `git status` та `git diff` на наявність:**
- API keys, tokens, secrets
- Паролі, credentials
- Приватні ключі (SSH, TLS)
- Access tokens (GitHub, AWS, інші)
- Будь-які дані, які не повинні бути публічними

§35.3 **ЗАБОРОНЕНО** додавати в коміт:
- `.env` файли
- Файли з секретами навіть для "тимчасового" зберігання
- Будь-які credentials, отримані від користувача
- Token-и, створені для тестування

§35.4 Якщо випадково секрет потрапив в коміт:
1. Не пушити (якщо ще не запушено)
2. Видалити секрет з коду
3. Перезаписати git history (якщо вже запушено) — повідомити користувача
4. Попередити про необхідність ротації скомпрометованого секрету

§35.5 `.gitignore` повинен містити типові виключення для .NET проекту та секретів.

---

## §36 🚫 Заборони

Без прямої необхідності не:

§36.1 Видаляти великі частини проєкту.

§36.2 Змінювати framework/architecture/API contract.

§36.3 Оновлювати всі dependencies.

§36.4 Форматувати весь repository.

§36.5 Відключати security/TLS.

§36.6 Приховувати помилки.

---

## §37 🤔 Правило сумнівів

§37.1 Між швидким-небезпечним та складнішим-надійним — обирати надійне. Особливо для game files, backup, rollback, download validation, path handling.

---

## §38 💎 Головний принцип

§38.1 Користувач запускає → знаходить гру → вибирає локалізацію → встановлює без розуміння internals.

§38.2 **Ніколи не залишати гру у пошкодженому стані заради "успішної" операції.**
