# BDO UA Client

> Клієнт для встановлення української локалізації у Black Desert Online

[![CI](https://github.com/merelyigor/bdo-ua-client/actions/workflows/ci.yml/badge.svg)](https://github.com/merelyigor/bdo-ua-client/actions/workflows/ci.yml)
[![Release Build](https://github.com/merelyigor/bdo-ua-client/actions/workflows/release-build.yml/badge.svg)](https://github.com/merelyigor/bdo-ua-client/actions/workflows/release-build.yml)

---

## Про проект

**BDO UA Client** — це Windows-застосунок, який автоматизує встановлення, оновлення та керування українською локалізацією для [Black Desert Online](https://www.blackdesertonline.com/).

Застосунок працює разом з [проектом українізації BDO](https://bdo-ua.com.ua/) та отримує актуальні релізи локалізації через API.

<!-- SCREENSHOT: Головне вікно програми -->
<!-- ![Головне вікно](docs/screenshot-main.png) -->

### Можливості

- **Автоматичний пошук гри** — через Steam, реєстр Windows або API
- **Встановлення локалізації** — з API з перевіркою SHA-256
- **Оновлення** — автоматичне визначення нової версії
- **Відновлення оригіналу** — повернення до офіційного файлу гри
- **Відновлення з копії** — вибір конкретної точки відновлення
- **Резервне копіювання** — автоматичні бекапи перед кожною зміною
- **Безпека** — SHA-256 перевірка, атомарна заміна, rollback при помилках
- **Логування** — детальні логи у `%LocalAppData%\BDO-UA-Client\logs\`

---

## Завантаження

<!-- SCREENSHOT: Сторінка релізів -->
<!-- ![Релізи](docs/screenshot-releases.png) -->

Перейдіть на сторінку **[Релізів](https://github.com/merelyigor/bdo-ua-client/releases)** та завантажте останній ZIP-файл:

```
BDO-UA-Client-win-x64.zip
```

### Системні вимоги

| Параметр | Значення |
|---|---|
| ОС | Windows 10/11 x64 |
| .NET Runtime | **не потрібен** (self-contained) |
| Місце на диску | ~200 MB |
| Інтернет | потрібен для завантаження локалізації |

---

## Встановлення

### 1. Завантажте

Завантажте `BDO-UA-Client-win-x64.zip` з [релізів](https://github.com/merelyigor/bdo-ua-client/releases).

### 2. Розпакуйте

Розпакуйте ZIP у будь-яку зручну папку, наприклад:
```
C:\Programs\BDO-UA-Client\
```

<!-- SCREENSHOT: Розпакована папка -->
<!-- ![Папка](docs/screenshot-folder.png) -->

### 3. Запустіть

Запустіть `BdoClient.exe`.

<!-- SCREENSHOT: Запуск -->
<!-- ![Запуск](docs/screenshot-launch.png) -->

### 4. Знайдіть гру

Застосунок спробує знайти гру автоматично. Якщо гра не знайдена — натисніть **"Обрати вручну"** та вкажіть папку встановлення Black Desert Online.

<!-- SCREENSHOT: Пошук гри -->
<!-- ![Пошук гри](docs/screenshot-detection.png) -->

### 5. Оберіть режим локалізації

| Режим | Опис |
|---|---|
| **Повна українська** | Повний переклад (Bosia + правки спільноти) |
| **Повна українська (Bosia)** | Тільки переклад від Bosia |
| **Англійські назви предметів** | Український текст, англійські назви предметів |

<!-- SCREENSHOT: Вибір режиму -->
<!-- ![Режим](docs/screenshot-mode.png) -->

### 6. Встановіть

Натисніть **"Встановити"** та дочекайтеся завершення.

<!-- SCREENSHOT: Встановлення -->
<!-- ![Встановлення](docs/screenshot-install.png) -->

---

## Використання

### Оновлення локалізації

Коли з'являється нова версія локалізації, кнопка **"Оновити"** стає активною. Натисніть її для оновлення.

<!-- SCREENSHOT: Оновлення доступне -->
<!-- ![Оновлення](docs/screenshot-update.png) -->

### Відновлення оригіналу

Натисніть **"Відновити оригінал"** щоб повернути офіційний файл гри. Це завантажить оригінальний файл з сервера або використає локальну копію.

### Відновлення з копії

Натисніть **"Відновити копію"** щоб обрати конкретну точку відновлення зі списку створених бекапів.

<!-- SCREENSHOT: Вибір копії -->
<!-- ![Копія](docs/screenshot-restore-point.png) -->

### Скасування операції

Під час завантаження або встановлення можна натиснути **"Скасувати"** для переривання операції.

---

## Як це працює

```
┌─────────────┐     ┌──────────────┐     ┌─────────────────┐
│  BDO UA API │────▶│  BDO Client  │────▶│  Black Desert   │
│ bdo-ua.com  │     │   (клієнт)   │     │   Online        │
└─────────────┘     └──────────────┘     └─────────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  Резервні    │
                    │   копії      │
                    └──────────────┘
```

1. **Пошук гри** — клієнт шукає встановлену гру через Steam, реєстр або API підказки
2. **Завантаження** — отримує актуальну локалізацію через API з перевіркою SHA-256
3. **Резервне копіювання** — створює бекап поточного файлу перед заміною
4. **Встановлення** — атомарно замінює файл локалізації
5. **Перевірка** — звіряє встановлений файл з очікуваним

---

## Архітектура

<!-- SCREENSHOT: Архітектура (якщо є діаграма) -->
<!-- ![Архітектура](docs/screenshot-architecture.png) -->

```
BdoClient/
├── Api/                    # Клієнт для bdo-ua.com.ua API
├── Models/                 # Моделі даних
├── Services/               # Бізнес-логіка
│   ├── GameDetector        # Пошук гри
│   ├── LocalizationInstaller # Завантаження + SHA-256
│   ├── LocalizationInstallService # Транзакційне встановлення
│   ├── RestoreOriginalService # Відновлення оригіналу
│   └── RestoreBackupService # Відновлення з копії
├── Storage/                # Конфігурація, стан, бекапи
├── Logging/                # Логування
└── Program.cs              # Точка входу
```

### Дані застосунку

```
%LocalAppData%\BDO-UA-Client\
├── config.json             # Налаштування
├── state/
│   └── installation.json   # Стан встановлення
├── logs/                   # Логи (щоденні файли)
├── cache/                  # Тимчасові файли
└── backups/
    ├── original/           # Оригінальний snapshot
    └── restore-points/     # Точки відновлення
```

---

## Збірка з коду

Для розробників, які хочуть зібрати проєкт з вихідного коду.

### Передумови

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11

### Команди

```bash
# Клонування
git clone https://github.com/merelyigor/bdo-ua-client.git
cd bdo-ua-client

# Збірка
dotnet build BdoUaClient.sln

# Тести
dotnet test BdoUaClient.sln

# Публікація (self-contained, win-x64)
dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -o publish
```

### Структура репозиторію

```
bdo-ua-client/
├── BdoUaClient.sln          # Рішення
├── BdoClient.csproj         # Основний проєкт (WinForms)
├── BdoClient.Tests/         # Тести (xUnit)
├── .github/workflows/
│   ├── ci.yml               # CI (build + test)
│   └── release-build.yml    # Релізний artifact
└── AGENTS.md                # Правила для AI-агента
```

---

## API

Застосунок використовує публічний API:

```
GET https://bdo-ua.com.ua/api/public/v1/releases
```

API повертає актуальні релізи локалізації з метаданими, SHA-256 хешами та URL для завантаження. Авторизація не потрібна.

---

## Пов'язані проекти

| Проект | Опис |
|---|---|
| [bdo-ua.com.ua](https://bdo-ua.com.ua/) | Проект українізації Black Desert Online |
| [bdo-ua.com.ua/download](https://bdo-ua.com.ua/download) | Сторінка завантаження локалізації |

---

## Безпека

- **SHA-256 перевірка** — кожен файл перевіряється перед встановленням
- **Атомарна заміна** — файл гри замінюється атомарно (NTFS atomic)
- **Rollback** — при помилці після заміни файл відновлюється
- **Резервні копії** — автоматичні бекапи перед кожною зміною
- **Path traversal захист** — шляхи валідуються проти escape
- **Безпечне скасування** — cancellation не залишає пошкоджений стан

---

## Логи

Логи зберігаються у:
```
%LocalAppData%\BDO-UA-Client\logs\bdo-ua-client_YYYY-MM-DD.log
```

Формат:
```
2026-08-16 21:32:14.125 [INFO] Application started.
2026-08-16 21:32:14.500 [INFO] Game found from Steam: C:\...\Black Desert Online
2026-08-16 21:32:15.200 [INFO] Install transaction started: mode=full-ukrainian
```

---

## Питання та проблеми

Якщо у вас виникли питання або проблеми:

1. Перевірте [логи](#логи) — там можуть бути деталі помилки
2. Переконайтеся, що гра знайдена правильно
3. Спробуйте перезапустити застосунок

---

## Ліцензія

Цей проєкт є публічним. Код доступний на [GitHub](https://github.com/merelyigor/bdo-ua-client).

---

## Подяки

- [BDO UA](https://bdo-ua.com.ua/) — проект українізації Black Desert Online
- Перекладачі та спільнота за тестування та вдосконалення локалізації
