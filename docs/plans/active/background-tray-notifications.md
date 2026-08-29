# Background tray notifications

Plan ID: `background-tray-notifications`
Status: ACTIVE
Focus: NONE (secondary feature, not PRIMARY)
Implementation authorization: **YES**
Depends on: completed Stage C — MainForm physical decomposition (COMPLETED / REVIEWED / ACCEPTED)
Lifecycle: BACKLOG → (explicit owner decision) → ACTIVE → (completed/superseded) → ARCHIVE
Next action: T3 — Background polling cadence

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

Status: **COMPLETED / REVIEWED / OWNER ACCEPTED**

Реалізовано та прийнято власником (результат E2E: `T1 PASS, layout defect fixed`).

Прийнята поведінка:

- нативний WinForms `NotifyIcon` + `ContextMenuStrip`, створені програмно у `MainForm.Tray.cs` (partial `MainForm`);
- звичайне закриття вікна (X / Alt+F4) ховає `MainForm` у трей, прибирає кнопку з панелі задач;
- іконка трея видима лише доки `MainForm` приховано;
- меню трея: `Відкрити`, `Вихід`;
- `Відкрити` та подвійний клік по іконці відновлюють `MainForm`;
- відновлення з `Minimized` нормалізує лише `Minimized`, зберігаючи `Maximized`;
- явний Exit у треї виконує реальне завершення, коли процес простоює;
- звичайне X під час активної Install/Update/Restore ховає вікно без скасування операції;
- явний Exit трея під час активної операції зберігає поточну безпечну семантику скасування/очікування (без hard-kill);
- `_explicitExitRequested` скасування спроби Exit під час активної операції (T1/T1.1); уточнено у T2: реальний вихід тепер відкладається і завершується автоматично після безпечного очищення операції через `_exitAfterOperation`;
- `_updateHandoffInProgress` лишається першою гілкою безпеки у `MainForm_FormClosing`;
- self-update `Application.Exit()` лишається реальним виходом (не перетворюється на hide-to-tray);
- Windows shutdown не перетворюється на hide-to-tray;
- каденція/життєвий цикл poller не змінюються через hide/restore;
- іконка трея видобувається з асоційованого exe (`Icon.ExtractAssociatedIcon`) із безпечним fallback `SystemIcons.Application`;
- власна видобута іконка має явне володіння та disposed лише при реальному завершенні;
- спільна системна іконка `SystemIcons.Application` ніколи не dispose;
- виправлено дефект геометрії після відновлення: завершення операції, доки вікно приховано, більше не кліпає картки/кнопки — відновлення виконує відкладений post-show relayout (`RefreshModeCardLayout()` + `ScheduleContentFit()`).

Валідація:

- Release build: 0 errors / 0 warnings;
- tests: 807 passed / 0 failed;
- owner manual E2E: прийнято;
- власник відтворив і підтвердив виправлення регресії кліпання після завершення операції у прихованому стані.

### T1.1 — Autostart / background startup

Status: **COMPLETED / REVIEWED / ACCEPTED**

Власник явно схвалив додавання автозапуску/background-старту як наступного підзавдання трея. Реалізовано, переглянуто архітектором та прийнято на основі automated runtime E2E, реальної інтеграції з реєстром HKCU, фактичної UI Automation трея, background-startup валідації та duplicate-instance runtime валідації.

Прийнята поведінка T1.1:

- per-user автозапуск Windows через `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, значення `BDO-UA-Client` = `"<current-absolute-exe-path>" --background` (без UAC/HKLM/Scheduled Task/Service/Startup-folder, без зовнішніх залежностей).
- реєстр — джерело істини для увімкнення автозапуску; конфіг зберігає лише UX-стан підказки (`autostart_prompt_dismissed`, за замовчуванням false).
- підказка показується лише після ручного idle X → трей, коли автозапуск вимкнено і підказку раніше не відхиляли; не спамить, не показується при `--background` старті та під час активної операції; `Так` увімкнює реєстр і помічає dismissal лише при успіху, `Ні` лише зберігає dismissal; явний вибір у меню трея також приховує майбутню підказку.
- меню трея: `Відкрити` → `Запускати разом із Windows` (checkbox, оновлюється з реального реєстру) → separator → `Вихід`; пункту `Перевірити зараз` немає.
- `--background` використовує той самий нормальний `MainForm`/стартовий pipeline (`MainForm_Shown`), без `ApplicationContext`, без другого процесу, без постійного видимого вікна/кнопки панелі задач; під час automated валідації видимий flash не спостерігався.
- точний командний рядок: `BdoClient.exe` (Normal visible), `BdoClient.exe --background` (Normal приховано в трей), `--apply-update <session-id>` (helper, строго ізольовано); mixed `--background`+`--apply-update` — невалідна граматика; невідомі non-updater аргументи зберігають попередню сумісність Normal без background.

Додано `Services/WindowsAutostartService` (HKCU Run enable/disable/is-enabled + canonical command build) та поле `autostart_prompt_dismissed` у `Storage/Config`.

### Безпека single-instance (нормальні клієнти)

Нормальний/background клієнт є single-instance на Windows-сесію. Іменовані об'єкти: `Local\BDO-UA-Client.SingleInstance` (Mutex) та `Local\BDO-UA-Client.Activate` (AutoReset `EventWaitHandle`).

- `BdoClient.exe --background` уже живий, потім `BdoClient.exe` → вторинний завершується, оригінальний primary залишається, його `MainForm` відновлюється/активується; рівно один нормальний процес.
- `BdoClient.exe --background` + ще один `BdoClient.exe --background` → вторинний тихо завершується, оригінал лишається прихованим; рівно один процес.
- видимий primary + ручний вторинний `BdoClient.exe` → вторинний завершується, оригінал виводиться на передній план; без дублювання стеку застосунку.
- якщо primary немає — свіжий нормальний запуск стає primary.
- гейт single-instance застосовується лише до Normal mode; `--apply-update` його не проходить (helper чекає завершення parent PID, старий процес звільняє Mutex, helper замінює target, перезапущений target стартує Normal і стає primary). `SelfUpdateApplier` не змінено.

Валідація:

- Release build: 0 errors / 0 warnings.
- tests: 835 passed / 0 failed (baseline 827 + 8 new single-instance + autostart tests).
- Live Registry `HKCU\...\Run` Enable/Disable/IsEnabled: PASS; canonical command підтверджено; original Registry стан відновлено.
- Фактичне tray-меню через UI Automation: підтверджено `Відкрити` / `Запускати разом із Windows` / `Вихід`; перемикання автозапуску з трея реально створювало/видаляло реєстрове значення.
- Background runtime `BdoClient.exe --background`: процес живий, 0 видимих віконних семплів під час старту, без підказки автозапуску, стартові логи підтверджують нормальний API/update lifecycle/poller.
- Duplicate-instance runtime сценарії (background+manual, background+background, visible+manual, ownership release/relaunch, background regression): усі PASS.
- Test-state restoration: Registry, config (byte-for-byte), preview-процеси зупинено, тимчасовий workspace видалено.

Обмеження автоматизації: фізичне натискання кнопок `Так`/`Ні` у MessageBox після X-close не вдалося надійно синтезувати в unattended середовищі (WinForms `CloseReason.UserClosing` не піднімається від ін'єктованого Win32-вводу без foreground-сесії); гілкування підказки переглянуто структурно, backend автозапуску валідовано наживо, dismissal-поведінку конфігу покрито юніт-тестами. Це обмеження автоматизації, не дефект продукту.

### T2 — Operation / shutdown semantics

Status: **COMPLETED / REVIEWED / ACCEPTED**

Реалізовано та переглянуто. Безпечна семантика завершення/очікування підтверджена через close-policy матрицю та статичний огляд меж безпечного завершення операції.

Прийнята поведінка:

- звичайне X під час активної Install/Update/Restore ховає у трей без скасування операції;
- явний Exit трея під час активної операції стає відкладеним реальним завершенням: `e.Cancel = true`, `_closing = true`, `_exitAfterOperation = true`, запит скасування існуючого CTS;
- після повного безпечного завершення операції (state refresh, feed unblock, poller-resume guard) `CompletePendingExitAfterOperation()` планує відкладений `BeginInvoke(Close)`;
- другий FormClosing обходить hide-to-tray (pending exit залишається), процес завершується автоматично;
- друге натискання `Вихід` більше не потрібне;
- `_closing = true` під час відкладеного завершення пригнічує resume poller/feed, secondary-instance restore та layout scheduling;
- Windows/system реальне закриття під час активної операції слідує тому ж патерну безпечного defer/cancel/cleanup/exit;
- клієнт пріоритизує цілісність файлів гри; автоматичне відновлення оригінального Windows shutdown не гарантується і не ініціюється (жодних Windows shutdown API);
- self-update `_updateHandoffInProgress` лишається першою гілкою FormClosing (реальний вихід, не hide-to-tray);
- `HandleInstallAsync` захищено від pre-CTS гонки: після `await _stateService.ResolveAsync(...)` перевіряється `_exitAfterOperation || _closing`, встановлення переривається до створення CTS/початку транзакції;
- Restore Original та application-update staging не мають еквівалентної pre-CTS await-прогалини.

Валідація:

- Release build: 0 errors / 0 warnings;
- tests: 845 passed / 0 failed (baseline 835 + 10 new close-policy tests);
- статичний огляд safe completion boundaries;
- корекція pre-CTS install race перевірена та виправлена;
- без деструктивного реального runtime E2E проти файлів гри.

Не реалізовано T3-T6.

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
