# UI — MainForm

Один вікно (`MainForm`), розмір 640×560 (мінімум 620×480), стартова позиція — центр екрана.

## Структура

Вікно містить 4 блоки, розташовані вертикально через `TableLayoutPanel`:

```
┌─────────────────────────────┐
│  [Гра]                      │  AutoSize
├─────────────────────────────┤
│  [Режим локалізації]        │  AutoSize
├─────────────────────────────┤
│  [Стан]                     │  Fill (займає решту)
├─────────────────────────────┤
│  [Встановити] [Відновити]…  │  AutoSize
└─────────────────────────────┘
```

---

## 1. Блок «Гра» (`gameGroupBox`)

GroupBox з заголовком **"Гра"**.

Елементи:
- **`gameStatusLabel`** — текстовий Label, постійний індикатор стану пошуку гри. Зелений `"✓ Гру знайдено"` (або `"✓ Гру знайдено вручну"`) при успіху, сірий `"Гру не знайдено"` / `"Пошук гри..."` в інших випадках. Не зникає після успіху — залишається видимим.
- **`gamePathLabel`** — показує знайдений шлях до гри з `AutoEllipsis`.
- **`detectGameButton`** — кнопка **"Знайти гру"**, запускає автоматичний пошук через `GameDetector` (registry → Steam → API hints).
- **`browseGameButton`** — кнопка **"Обрати вручну"**, відкриває `FolderBrowserDialog`.

Layout: 2×2 `TableLayoutPanel`. Перший рядок — `gameStatusLabel` на всю ширину. Другий рядок — `gamePathLabel` зліва, кнопки праворуч (`FlowLayoutPanel`).

### Детекція гри: UX-поведінка

**Стартовий пошук (MainForm_Shown):**

API запит та локальна детекція запускаються **паралельно**:
1. `gameStatusLabel` одразу показує `"Пошук гри..."`
2. Локальна детекція (SavedConfig/Registry/Steam) виконується без очікування API
3. Якщо локальна детекція знайшла гру — `gameStatusLabel` = зелений `"✓ Гру знайдено"` навіть якщо API ще завантажується
4. Якщо локальна детекція не знайшла — чекає API; якщо API успішний з `install_path_patterns` — виконує API-assisted fallback
5. Якщо API не вдався — показується повідомлення про помилку, локальний результат зберігається
6. Блок режимів показує `"Завантаження доступних режимів..."` під час API, `"Не вдалося завантажити режими."` при помилці, `"Наразі немає доступних режимів."` при порожньому API відповіді

**Автоматичний пошук (DetectGameButton_Click):**

- **Успішний `DetectAsync`**: новий `GamePath` стає активним, `gameStatusLabel` = зелений `"✓ Гру знайдено"`.
- **`DetectAsync` повертає нормальний NotFound**: `_gameRoot` очищається, `gameStatusLabel` = сірий `"Гру не знайдено"`, `gamePathLabel` очищається.
- **`DetectAsync` викидає неочікуваний exception**: якщо попередній `_gameRoot` досі валідний (`ValidateGamePath` повертає `true`), попередній root відновлюється як активний з попереднім зеленим статусом. Повідомлення про помилку показується в `messageTextBox`. Якщо попереднього валідного root немає — `"Помилка пошуку гри"`.

**Ручний вибір (BrowseGameButton_Click):**

- **Невалідна/неоднозначна папка при наявності активного валідного `_gameRoot`**: статус гри та шлях не змінюються — показується лише тимчасове повідомлення про помилку в `messageTextBox`.
- **Невалідна/неоднозначна папка без активного валідного `_gameRoot`**: `gameStatusLabel` = сірий `"Гру не знайдено"`.

**Manual parent→child resolution**: якщо користувач обирає батьківську папку (наприклад, `C:\Games`), `GameDetector.ResolveManualGameRoot` шукає підпапку з грою:
1. Сама обрана папка (exact root)
2. Підпапки першого рівня (immediate children)
3. Якщо знайдено кілька — `"Знайдено кілька папок з грою. Оберіть точну папку гри."`
4. Глибоке рекурсивне сканування не виконується.

---

## 2. Блок «Режим локалізації» (`modeGroupBox`)

GroupBox з заголовком **"Режим локалізації"**.

Елементи:
- **`modesFlowPanel`** — `FlowLayoutPanel` з `FlowDirection.TopDown`, `WrapContents = false`. Містить динамічно створені `RadioButton`.

### Динамічне побудова режимів

Режими **не захардкоджені**. Вони будуються з `_apiResponse.Data.Modes` через `DynamicModePolicy.GetInstallableModes()`. Режим вважається придатним для встановлення, якщо `Current != null` та структурно валідний.

Для кожного режиму створюється `RadioButton`:
- **Text**: `"{PublicName}\n{releaseLine}"` (багаторядковий: назва + інформація про реліз)
- **Tag**: `mode.Slug` (використовується для ідентифікації)
- **AutoSize**: `true`

При зміні вибраного режиму (`CheckedChanged`) зберігається `LastMode` у `Config` та оновлюється стан.

### Маркер встановленого режиму

`UpdateInstalledMarkers()` проходить по всіх `RadioButton` у `modesFlowPanel`. Для кожного перевіряється: чи збігається `slug` з `installedModeSlug` **І** чи збігається `PublicId` поточного релізу цього режиму з `installedPublicId`. Якщо так — до тексту додається `"\n✓ Встановлено"`.

Це **exact match**: потрібен збіг і ModeSlug, і PublicId. Якщо встановлено режим A з PublicId X, а в API режим A має вже інший current (PublicId Y) — маркер не показується.

---

## 3. Блок «Стан» (`statusGroupBox`)

GroupBox з заголовком **"Стан"**, `Dock = Fill` — займає весь доступний простір.

Елементи (зверху вниз):
- **`localizationStateLabel`** — **жирний** текст поточного стану локалізації (наприклад, "Не встановлено", "Актуальна", "Доступна новіша версія"). Відображає `LocalizationState` у зрозумілій формі.
- **`installedInfoLabel`** — сірий текст. Формат: `"Встановлено: {назва режиму}  |  v{версія}  |  {дата}"` або `"Локалізацію не встановлено"`.
- **`detailsLabel`** — сірий текст. Формат: `"Обрано: {назва режиму} | {інформація про реліз}"`. Порожній, якщо немає обраного режиму з current.
- **spacer** — 8px порожній рядок.
- **progressBar + progressLabel** — рядок з `ProgressBar` (0–100) та Label праворуч з текстом відсотка або статусу операції.
- **`messageTextBox`** — багаторядковий `ReadOnly` TextBox з вертикальним скролом. Показує діагностичні повідомлення, помилки, підтвердження.

### Відображення OperationState у progressLabel

| OperationState | Текст progressLabel |
|---|---|
| `Idle` | `0%` |
| `DetectingGame` | `Пошук гри...` |
| `LoadingApi` | `Завантаження даних...` |
| `Downloading` | `Завантаження...` |
| `Verifying` | `Перевірка...` |
| `BackingUp` | `Створення резервної копії...` |
| `Installing` | `Встановлення...` |
| `Restoring` | `Відновлення...` |
| `Completed` | `Завершено` |
| `Failed` | `Помилка` |
| `Cancelled` | `Скасовано` |

Під час `Downloading` progressLabel також оновлюється відсотком через `OnDownloadProgress`.

---

## 4. Блок «Дії» (`actionsPanel`)

`FlowLayoutPanel` зліва направо, `Dock = Bottom`.

Кнопки:
- **`installButton`** — **"Встановити"**. Єдина кнопка для першої установки, заміни режиму та оновлення. Окрема кнопка "Оновити" відсутня.
- **`restoreOriginalButton`** — **"Відновити оригінал"**. Відновлює офіційний файл: спочатку завантаження з `official_source_url`, fallback на локальний original snapshot (якщо патч збігається).
- **`cancelButton`** — **"Скасувати"**. Активна лише під час операції. Натискання викликає `_operationCts.Cancel()`.

Кнопки **"Оновити"** та **"Відновити backup"** в UI відсутні.

Початковий стан: всі три кнопки `Enabled = false`.

---

## 5. Операційний flow

### Guard: `_operationInProgress`

Булевий прапорець, що блокує паралельне виконання операцій. `HandleInstallAsync()` та `HandleRestoreOriginalAsync()` на вході перевіряють `if (_operationInProgress) return;`.

### Блокування контролів під час операції

`SetControlsDuringOperation(false)` вимикає:
- `detectGameButton`
- `browseGameButton`
- Всі `RadioButton` у `modesFlowPanel`

Кнопка `cancelButton` вмикається окремо після створення `CancellationTokenSource`.

### Після завершення операції

1. `cancelButton.Enabled = false`
2. `_operationCts?.Dispose()` + `null`
3. `_operationInProgress = false`
4. `SetControlsDuringOperation(true)` — відновлення контролів
5. `RefreshStateAsync()` — оновлення всього UI
6. Вивід фінального повідомлення

### FormClosing safety

Обробник `MainForm_FormClosing`:
- Якщо операція **не** виконується — дозволяє закриття.
- Якщо операція виконується — `e.Cancel = true`, запуск скасування через `_operationCts.Cancel()`. Повторне закриття після завершення операції.

---

## 6. Скасування операції

`CancelButton_Click`:
1. Перевірка `_operationInProgress` та `_operationCts != null`.
2. `cancelButton.Enabled = false` — запобігає повторному натисканню.
3. `SetMessage("Скасування операції...")`.
4. `_operationCts.Cancel()`.

У `HandleInstallAsync` / `HandleRestoreOriginalAsync`:
- `OperationCanceledException` перехоплюється окремим catch-блоком.
- `SetOperationState(OperationState.Cancelled)`.
- Повідомлення: "Встановлення скасовано." / "Відновлення оригіналу скасовано."
