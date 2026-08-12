# AGENTS.md

## 1. Мова

- Спілкування, коментарі, звіти — **тільки українською**.
- Англійська дозволена для: назв технологій, API, бібліотек, ідентифікаторів, системних термінів.
- Код — англійськими назвами змінних/функцій.
- Не змішувати українську з російською. Російську не використовувати.

---

## 2. Призначення

Windows `.exe` застосунок для встановлення локалізацій у комп'ютерні ігри.

Можливості:
- Автоматичний/ручний пошук гри
- Валідація директорії
- Отримання локалізацій через API
- Завантаження, встановлення, оновлення
- Перевірка стану, видалення/відновлення
- Повідомлення про помилки

---

## 3. Принцип роботи

1. Зрозуміти структуру → знайти пов'язані файли → зрозуміти data flow → визначити мінімум змін → редагувати.
2. Не створювати нову архітектуру без потреби.
3. Не переписувати великі частини заради дрібної зміни.

---

## 4. Пріоритети

1. Коректність
2. Безпека даних
3. Неможливість пошкодження файлів гри
4. Стабільність
5. Простота підтримки
6. Зрозуміла архітектура
7. Хороший UX
8. Продуктивність
9. Краса коду

---

## 5. Код

- Простий, читабельний, передбачуваний, типізований
- Розділений за відповідальністю
- Без зайвих абстракцій та дублювання
- Зрозумілі назви: `game_installation_path` ≠ `data`, `temp`, `obj`

---

## 6. Не ускладнювати

Не використовувати складні patterns без реальної потреби:
- зайві interfaces/abstract classes
- фабрики для одного об'єкта
- dependency injection без необхідності
- надмірно глибоку ієрархію класів

---

## 7. Архітектура

Не змішувати в одному місці: UI / API / файлову роботу / пошук гри / установку / конфігурацію / логування.

Бажане розділення:
```
UI → Application/Services → API Client → Game Detection
                                         → Localization Installer
                                         → File System
                                         → Configuration / Logging / Models
```

---

## 8. UI

UI показує інформацію та викликає service-методи. UI **не** повинен:
- будувати API-запити
- копіювати файли
- перевіряти структуру гри
- реалізовувати алгоритм встановлення

---

## 9. Не блокувати UI

Довгі операції (HTTP, download, scan, розпакування, копіювання, checksum) — в async/background. UI має залишатися responsive.

---

## 10-14. API

- Вся взаємодія через окремий API client (base URL, headers, timeout, serialization, error handling).
- Не вважати API ідеальним: обробляти timeout, DNS error, 4xx/5xx, порожню відповідь, malformed JSON.
- Відповіді перетворювати на моделі (`Game`, `Localization`, `ApiError` тощо).
- Base URL — централізовано (Config/Environment).
- API версію враховувати явно.

---

## 14a. API Contract: bdo-ua.com.ua

Base URL: `https://bdo-ua.com.ua/api/public/v1`

**GET /releases** — єдиний endpoint. Клієнт не повинен сам вирішувати, яка локалізація актуальна.

Структура відповіді:
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

Ключові правила:
- `current` відсутній для режиму — нормальний стан (актуальний release ще не опубліковано)
- `history` використовувати лише для інформації. Завантажувати старі версії ЗАБОРОНЕНО
- `official_source_url` — для відновлення оригіналу. SHA-256 для нього НЕ надається
- `install_path_patterns` — лише hints для detection, не довірені filesystem instructions
- Download: прямий (без redirect), авторизація не потрібна
- `progress` — глобальний для всіх режимів. `stats.rows_in_file` може відрізнятися від `progress.total_rows`

Доступні режими (slug):
- `full-ukrainian` — повна українська (Bosia + правки спільноти)
- `full-ukrainian-bosia` — повна українська лише від Bosia
- `english-items` — українські тексти з англійськими назвами предметів

Статуси history: `superseded` / `withdrawn` ( `current` ніколи не з'являється в history )

---

## 15. Secrets

Ніколи не додавати в репозиторій: API keys, tokens, passwords, signing secrets, credentials. Desktop `.exe` не може надійно приховати секрет.

---

## 16-18. Пошук гри

- Game detection — окремий модуль.
- Порядок: збережений шлях → registry → Steam libraryfolders → appmanifest_582660.acf → `install_path_patterns` з API (hints) → ручний вибір.
- Steam detection: читати `libraryfolders.vdf`, знаходити `appmanifest_582660.acf`, витягувати `installdir`.
- `install_path_patterns` з API — лише hints для перебору дисків, не довірені filesystem instructions.
- Директорія валідується: наявність `{game_path}\ads\languagedata_en.loc`.
- Ручний вибір завжди доступний.

---

## 19-23. Файли та backup

- Перед зміною/видаленням файлу знати, як його відновити.
- Бекап створювати один раз, не перезаписувати good backup модифікованим файлом.
- Атомарна установка: download у temp → validation → backup → apply → verify → cleanup.
- Temporary files: `.tmp`/`.download`, переміщати після перевірки.
- Перевірка hash (SHA-256) при наявності від сервера.

**Розділення backup:**
- **Original snapshot** — незмінна копія `languagedata_en.loc`, яка існувала перед першою модифікацією клієнтом. Не перезаписувати. Не трактувати як гарантовано актуальний original після майбутніх патчів гри.
- **Restore points** — попередні встановлені локалізації. Створюються ПЕРЕД заміною game file (pre-operation snapshot). Не вважати їх оригінальним game file.

**Metadata original snapshot:**
- `created_at` — час створення
- `game_patch` — якщо його можливо достовірно визначити
- `sha256` — обчислений локально (не з API)
- `size_bytes`

**Чотири операції:**
1. `Встановити` — перша установка
2. `Оновити` — заміна на новіший release
3. `Відновити оригінал` — у першу чергу завантаження з `official_source_url`; локальний original snapshot — fallback
4. `Відновити backup` — повернення до попереднього restore point

Не видаляти `languagedata_en.loc` фізично як спосіб uninstall. Повернення до стану без української = відновлення official/original `.loc`.

---

## 24. API Release Metadata

API надає release metadata через `GET /releases`. Клієнт виконує валідовані операції.

**Installation Safety Workflow:**
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

Якщо помилка на будь-якому кроці до replace — файл гри НЕ змінено, metadata НЕ стверджує, що release встановлено.

Якщо помилка сталася після replace — клієнт повинен спробувати rollback до pre-operation snapshot. Якщо rollback не вдався:
- не заявляти успішну установку;
- стан вважати пошкодженим (`Corrupted`);
- показати користувачу критичну помилку;
- записати деталі в log.

---

## 25-26. Захист шляхів

- Path traversal protection: нормалізація + перевірка, що файл в межах дозволеної директорії.
- Не довіряти filenames від API: не писати в system directories, не запускати executable.

---

## 27-29. Download

Підтримка: timeout, progress, cancellation, error handling, partial cleanup, hash check, streaming для великих файлів.

---

## 30-32. Стани та оновлення

- Перевіряти фактичний стан файлів, а не лише config flag.
- Перед update: поточна версія, серверна версія, сумісність.
- `compatible_with_official_patch == false` → Install та Update заборонені, download не починається.

**LocalizationState (постійний стан файлу):**
- `NotInstalled` — файл не встановлено
- `UpToDate` — `installed.public_id == current.public_id`
- `UpdateAvailable` — `installed.public_id != current.public_id`
- `WaitingForRelease` — встановлено, але `current` відсутній
- `InstalledVersionUnknown` — metadata немає або нечитабельна
- `Corrupted` — hash не збігається

**OperationState (тимчасовий стан операції):**
- `Idle` / `DetectingGame` / `LoadingApi` / `Downloading` / `Verifying`
- `BackingUp` / `Installing` / `Restoring` / `Completed` / `Failed` / `Cancelled`

Основний ідентифікатор release — `public_id`, а не `patch` чи `version`.

---

## 33. Видалення

Uninstall: визначити зміни → відновити backup → видалити лише localization files → перевірити.

---

## 34-35. Windows

- Коректні Windows paths, Unicode, пробіли, permissions, read-only/locked files.
- Не вимагати Admin без потреби. Permission error → зрозуміле повідомлення.

---

## 36-37. UX

Простий інтерфейс: де гра, яка локалізація, версія, стан, прогрес, результат. Помилки — зрозумілою мовою, деталі в log.

---

## 38-41. Логування та exceptions

- Структуроване логування (DEBUG/INFO/WARNING/ERROR).
- Логувати: запуск, detection, API errors, download failures, installation stages, exceptions.
- Не логувати: passwords, tokens, secrets.
- **Заборонено** порожні `catch/pass`. Кожна помилка обробляється/ліогується/re-throw.

---

## 42-43. Конфігурація та cache

- User settings у Windows user-data location (не поруч з `.exe` в Program Files).
- Cache окремо від game files. Не змішувати cache/backup/config/logs/game files.

**Структура даних (%LocalAppData%\BDO-UA-Client\):**
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

## 44-45. Мережа

- Retry тільки для безпечних операцій, з backoff та лімітом.
- Кожна мережева операція має timeout.

---

## 46-47. Залежності

- Не додавати dependency, якщо standard library справляється.
- Не змінювати framework/build system/package manager без потреби.

---

## 48-49. Build та .exe

- Після змін: syntax check → build → tests → виправлення.
- Пакетований `.exe` може відрізнятися від dev mode (working directory, bundled resources).

---

## 50-52. Тести

- Тестувати без UI: path validation, release metadata parsing, checksum, game detection, version comparison.
- Тests використовують temp directories, не працюють з реальними game files.
- Edge cases: гра не знайдена, кілька копій, Unicode/пробіли, locked files, переповнений диск, перерваний download, пошкоджені файли.

---

## 53-56. Security

- Усе зовнішнє — недовірене: API responses, filenames, URLs, manifests.
- TLS verification ніколи не вимикати.
- Не запускати executable отриманий з сервера (окрім whitelist операцій: copy/replace/delete localization file/create dir/restore backup).
- Невідомі operation відхиляти.

---

## 57. Сумісність

`compatible_with_official_patch == false` → Install та Update заборонені. Download не починається. Користувачу показується причина. Ніякого override в MVP.

---

## 58-63. Правила змін

- Мінімальний patch. Не змішувати feature/refactor/formatting.
- Не чіпати непов'язаний код.
- Refactor не змінює поведінку ненавмисно.
- Не залишати TODO замість реалізації.
- Не підміняти реалізацію placeholder/mock.
- Не hardcode: шляхи, usernames, tokens, credentials.

---

## 64-66. Коментарі та документація

- Коментарі пояснюють **чому**, а не **що**.
- Оновлювати документацію при зміні build/config/API/structure.
- Невідомий код — дослідити перед зміною.

**API-контракт не є статичним.** Сервер може додавати нові поля, змінювати структуру відповідей, вводити нові endpoint-и або deprecated-и. При зміні API потрібно:
1. Оновити моделі клієнта (відповідність новій структурі).
2. Оновити цей документ (розділ 14a).
3. Перевірити backward/forward compatibility.
4. Попередити користувача про несумісність, якщо версія API недоступна.

---

## 67-69. Не вигадувати

- Використовувати існуючий API contract.
- Не вигадувати API бібліотек.
- Складні задачі — маленькими кроками.

---

## 70. Definition of Done

Задача виконана, коли:
- Реалізована функціональність без placeholder
- Оброблені error cases
- UI responsive
- Файлові операції захищені
- Код відповідає архітектурі
- Build проходить
- Tests проходять (якщо є)

---

## 71-72. Звіт та коміти

Коротко: що змінено, ключові файли, що перевірено, build/tests, обмеження. Не заявляти "все працює" без перевірки.

**Правила комітів:**
- Формат: `v{ЕТАП}.{ПІДЕТАП} — {короткий опис}`
- Кожен завершений етап/підетап — окремий коміт
- Опис українською + список змінених файлів
- Перед комітом: `dotnet build` без помилок
- Після коміту: одразу push
- Деталі в `plan.md` (розділ "Правила комітів та версійності")

---

## 73. Заборони

Без прямої необхідності не:
- видаляти великі частини проєкту
- змінювати framework/architecture/API contract
- оновлювати всі dependencies
- форматувати весь repository
- відключати security/TLS
- приховувати помилки

---

## 74. Правило сумнівів

Між швидким-небезпечним та складнішим-надійним — обирати надійне. Особливо для game files, backup, rollback, download validation, path handling.

---

## 75. Головний принцип

Користувач запускає → знаходить гру → вибирає локалізацію → встановлює без розуміння internals.

**Ніколи не залишати гру у пошкодженому стані заради "успішної" операції.**
