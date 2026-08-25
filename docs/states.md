# Стани — LocalizationState та OperationState

Два перелічення для різних аспектів стану застосунку.

---

## LocalizationState — постійний фактичний стан

Визначає **фактичний** стан встановленої локалізації на основі файлів на диску та API-метаданих. Це **постійний** стан — він зберігається між запусками та не зникає після закриття вікна.

Визначається `LocalizationStateService.ResolveAsync()` на основі:
- `installedModeCurrent` — `Current` об'єкта встановленого режиму (з API), а не обраного в UI
- Фактичного файлу `{game_path}\ads\languagedata_en.loc` (наявність, читабельність, hash)

### Значення

| Стан | Опис |
|---|---|
| `NotInstalled` | Локалізацію не встановлено. `installation.json` відсутній, або metadata вказує `source == "official"`. |
| `UpToDate` | Встановлений `PublicId` збігається з `current.PublicId` поточного релізу встановленого режиму. Exact string comparison. |
| `UpdateAvailable` | Встановлений `PublicId` **не** збігається з `current.PublicId`. Доступна новіша версія. |
| `WaitingForRelease` | Встановлено, hash збігається, але `current` відсутній у API (режим без актуального релізу). Або `current != null`, але `PublicId` null/empty/whitespace — теж `WaitingForRelease`, але result містить diagnostic Error. |
| `InstalledVersionUnknown` | `installation.json` існує, але `InstallationStateStore.Load()` повертає Invalid (пошкоджений або нечитабельний файл стану). |
| `Corrupted` | API metadata валідний, але фактичний файл гри відсутній або нечитабельний. Hash mismatch **не завжди** означає `Corrupted`: якщо фактичний `ads_files` патч гри новіший за `metadata.GamePatch`, це нормальний patch transition (див. нижче). |

### Patch transition

Поряд з `LocalizationState` сервіс повертає `LocalizationPatchTransition { None, ExistingLocalizationOutdated, GameFileReplacedAfterPatch }`:

- `ExistingLocalizationOutdated` — локальний патч новіший за встановлену версію локалізації (без hash mismatch).
- `GameFileReplacedAfterPatch` — hash mismatch після оновлення гри (гра замінила `.loc`; не вважається пошкодженням).

### Відображення в UI

`LocalizationStatePresentation.GetDisplayText(LocalizationStateResult)` перетворює результат на текст для `localizationStateLabel`. Тексти patch transition мають пріоритет над текстами стану:

| Стан / transition | Текст в UI |
|---|---|
| `NotInstalled` | Локалізацію не встановлено |
| `UpToDate` | ✓ Локалізація актуальна |
| `UpdateAvailable` | Доступне оновлення локалізації |
| `WaitingForRelease` | Очікується актуальна версія локалізації |
| `InstalledVersionUnknown` | Не вдалося визначити версію локалізації |
| `Corrupted` | Файл локалізації пошкоджено |
| `ExistingLocalizationOutdated` | Встановлена локалізація застаріла |
| `GameFileReplacedAfterPatch` | Після оновлення гри файл локалізації було замінено |

### Ключовий принцип

`LocalizationState` використовує **встановлений** режим (`installedModeCurrent`), а не обраний в UI. Це гарантує, що стан відображає реальність, а не наміри користувача.

---

## OperationState — тимчасовий стан операції

Відображає поточний етап виконуваної операції. Це **тимчасовий** стан — він існує лише під час активної операції та скидається в `Idle` після завершення.

### Значення

| Стан | Опис | Текст progressLabel |
|---|---|---|
| `Idle` | Очікування, жодна операція не виконується. | `0%` |
| `DetectingGame` | Автоматичний пошук гри. | `Пошук гри...` |
| `LoadingApi` | Завантаження даних з API (`GET /releases`). | `Завантаження даних...` |
| `Downloading` | Завантаження файлу локалізації. progressLabel оновлюється відсотком. | `Завантаження...` / `{N}%` |
| `Verifying` | Перевірка SHA-256 та розміру завантаженого файлу. | `Перевірка...` |
| `BackingUp` | Створення резервної копії (original snapshot або restore point). | `Створення резервної копії...` |
| `Installing` | Заміна файлу локалізації у папці гри. | `Встановлення...` |
| `Restoring` | Відновлення оригінального файлу (download з `official_source_url` або fallback на snapshot). | `Відновлення...` |
| `Completed` | Операція завершена успішно. | `Завершено` |
| `Failed` | Операція завершена з помилкою. | `Помилка` |
| `Cancelled` | Операцію скасовано користувачем. | `Скасовано` |

---

## Ключова відмінність

| | LocalizationState | OperationState |
|---|---|---|
| **Тип** | Постійний фактичний стан | Тимчасовий стан операції |
| **Що описує** | Що **встановлено** на диску | Що **відбувається** зараз |
| **Джерело** | Файли на диску + API metadata | Потік виконання операції |
| **Тривалість** | Постійний між запусками | Лише під час операції |
| **Скидається** | Ні (змінюється при install/restore) | Так (→ `Idle` після завершення) |
| **Використовує** | `LocalizationStateService` | Прямий `SetOperationState()` |
| **UI-елемент** | `localizationStateLabel` (bold) | `progressLabel` + `progressBar` |
