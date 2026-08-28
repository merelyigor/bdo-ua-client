# Background tray notifications

Plan ID: `background-tray-notifications`
Status: ACTIVE
Focus: NONE (secondary feature, not PRIMARY)
Implementation authorization: **YES**
Depends on: completed Stage C — MainForm physical decomposition (COMPLETED / REVIEWED / ACCEPTED)
Lifecycle: BACKLOG → (explicit owner decision) → ACTIVE → (completed/superseded) → ARCHIVE
Next action: T1 read-only tray lifetime inspection / mapping

## Goal

BDO UA Client має можливість залишатися запущеним тихо в області сповіщень Windows (notification area) після закриття головного вікна, моніторити зміни стану локалізації з низьким споживанням ресурсів і повідомляти користувача, коли потрібна дія.

## Application lifetime

Нормальне закриття вікна (X):

- не завершувати процес застосунку;
- сховати MainForm;
- прибрати з панелі задач;
- залишити NotifyIcon активним.

Меню трея:

- Відкрити
- Перевірити зараз
- Вихід

Подвійний клік по іконці трея:

- відновити/показати MainForm.

Стандартний WinForms: `NotifyIcon` + `ContextMenuStrip`. Без зовнішніх UI-фреймворків/залежностей.

## Exit semantics

Реалізація має розрізняти:

1. нормальне закриття вікна (X) → сховати в трей;
2. явний вихід з трея (Exit) → фактичне завершення;
3. self-update handoff → фактичне завершення.

Існуюча змінна `_updateHandoffInProgress` має й надалі обходити поведінку close-to-tray. Self-update жодним чином не повинен випадково сховати старий процес замість його завершення.

## Active operations

Нормальне X під час:

- встановлення локалізації;
- оновлення локалізації;
- відновлення;

повинно:

- сховати вікно;
- дозволити операції продовжуватись.

Не скасовувати операцію автоматично лише через те, що вікно сховано. Явний Exit з трея під час активної destructive-операції має зберігати безпечну семантику скасування/очікування. Не hard-kill застосунок під час операції з файлом гри.

## API polling

Поточне опитування при видимому вікні — приблизно 15 секунд. Цільова архітектура:

- вікно видиме: ~15 секунд;
- трей/background: ~5 хвилин;
- при відновленні вікна: негайне оновлення, потім повернення до видимого каденсу.

Це лише цілі архітектури. Не реалізовувати їх.

## Local file monitoring

Критичне обмеження:

`ReleaseFeedPoller` реагує лише на семантичну зміну API-фіда. Тому гра/ланчер може перезаписати `ads\languagedata_en.loc`, поки API-фід не змінився. Tray-функція потребує окремого дешевого тригера локальної зміни.

Переважна перша реалізація:

відстежувати:

- існування;
- Length;
- LastWriteTimeUtc;

поки застосунок у background.

Якщо без змін — нічого не робити. Якщо змінилось / зникло / з'явилось — викликати існуючий `LocalizationStateService` resolution.

Не:

- постійно SHA-256 хешувати файл;
- опитувати кожні кілька секунд;
- дублювати правила локалізаційного стану;
- впроваджувати `FileSystemWatcher` у першій версії.

SHA має рахуватися лише через існуючий state resolution при виявленні значущої локальної зміни.

## State ownership

Tray/background-код має перевикористовувати:

- `LocalizationStateService`
- `LocalizationStateResult`
- `LocalizationPatchTransition`

Включно з існуючою семантикою:

- `ManagedFileChanged`
- `GameFileReplacedAfterPatch`
- `UpdateAvailable`
- `WaitingForRelease`
- `UpToDate`

Tray-код спостерігає за розв'язаним станом. Він НЕ повинен самостійно вирішувати:

- corrupted;
- replaced;
- outdated;
- patch transition.

Жодних дублюючих евристик.

## Notifications

Перша версія має використовувати існуючу підтримку Windows/WinForms через `NotifyIcon.ShowBalloonTip`. Без нових залежностей.

Можливі actionable-сповіщення:

- доступне оновлення локалізації;
- керована локалізація більше не активна;
- гра/ланчер замінили файл локалізації;
- інший справді actionable перехід стану.

Клік по сповіщенню має відновлювати/відкривати MainForm.

## Deduplication

Сповіщення мають бути transition-based. Не показувати той самий popup кожен background-poll.

Перша реалізація може тримати dedup-стан лише в RAM. Без змін схеми персистентності.

Концептуальна поведінка:

- `UpToDate` → `UpdateAvailable` = сповістити;
- `UpdateAvailable` → `UpdateAvailable` = не повторювати;
- `UpdateAvailable` → `UpToDate` = скинути;
- пізніше: `UpToDate` → `UpdateAvailable` = сповістити знову.

Можливі runtime-ключі: останній actionable стан, останній сповіщений PublicId, останній сповіщений mode. Точна реалізація — пізніше.

## Non-goals

Явно зафіксовано:

- без Windows Service;
- без Scheduled Task;
- без автоматичного Windows startup у першій версії;
- без тихого встановлення/оновлення локалізації;
- без примусового self-update застосунку;
- без процесу поза нормальним lifetime застосунку;
- без постійного SHA-хешування;
- без щільного polling-циклу;
- без нових UI-залежностей;
- без змін схеми персистентності;
- без змін API contract;
- без redesign `LocalizationStateService`;
- без `FileSystemWatcher` у першій реалізації.

## Proposed task decomposition

### T1 — Tray lifetime shell

- NotifyIcon
- ContextMenuStrip
- X ховає вікно
- Open відновлює
- double-click відновлює
- явний Exit
- збережено bypass self-update exit

### T2 — Operation / shutdown semantics

- ховати під час активної операції без скасування
- явний Exit зберігає безпечну скасування/wait семантику
- валідувати FormClosing та self-update шляхи

### T3 — Background polling cadence

- видимий каденс
- tray каденс
- негайне оновлення при відновленні
- уникати зайвого API-трафіку

### T4 — Local file-change trigger

- fingerprint існування / Length / LastWriteTimeUtc
- виклик state resolution лише після зміни
- без постійного хешування

### T5 — Notifications / dedup

- actionable transition-сповіщення
- клік по сповіщенні відкриває застосунок
- повторюваний той самий стан не спамить

### T6 — Owner E2E / resource validation

Валідувати зрештою:

- X → tray
- tray → Open
- tray → Exit
- install + X → операція продовжується
- self-update → старий процес справді завершується
- ланчер перезаписує loc-файл поки API не змінився → виявлено
- API публікує новий реліз → сповіщення
- dedup працює
- background CPU/disk/network залишається розумним

Не реалізовувати T1–T6 зараз.
