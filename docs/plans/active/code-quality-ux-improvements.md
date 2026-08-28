# Code quality improvements (refactor)

Plan ID: `code-quality-ux-improvements`
Status: ACTIVE
Focus: PRIMARY
Backlog order: —
Implementation authorization: **YES**
Current phase: Stage C — MainForm physical decomposition — COMPLETED / REVIEWED / ACCEPTED
Next action: Owner decision: activate background-tray-notifications or select another task
Dependencies: none (client-ui-redesign archived)

## Goal

Зменшити дублювання у safety-critical шляхах (install/restore/rollback), привести код до правил AGENTS, фізично декомпозувати MainForm.cs та централізувати спільні примітиви.

## Context

Аудит коду (2026-08) виявив дублювання та порушення AGENTS. Частина проблем вирішена у v14.2.25 (див. нижче). Архітектурний рев'ю відхилив деякі запропоновані абстракції як надмірні.

### Актуальні проблеми (ще не вирішені)

- **Дубльовані raw installation-state операції**: `ReadRawInstallationState` ×3, атомарний запис стану ×4 (разом з `InstallationStateStore.SaveAsync`), rollback-логіка розходиться (різні temp-імена, різна обробка CancellationToken). — заплановано у Stage B.
- **Магічні рядки**: `"ads"`/`"languagedata_en.loc"` у 5+ місцях, `"installation.json"` ×8 (при існуючому `AppPaths.InstallationFile`), `"api"`/`"official"` маркери ×9. — **ВИРІШЕНО у Stage A** (A.1 GamePaths, A.2 InstallationSource, A.3 AppPaths.InstallationFile reuse).
- **MainForm.cs великий** (~1800+ рядків): self-update, localization, presentation, startup — все в одному файлі. — заплановано у Stage C.
- **Дубльований factual-state резолв** у `HandleInstallAsync` та `RefreshStateAsync`.
- **Sync file IO на UI-потоку** (`_stateStore.Load()`, `AdsFilesPatchReader.TryReadPatch`, `GameDetector.ValidateGamePath`) — не підтверджено як реальний performance defect, потребує вимірювання. — заплановано у Stage D.

## Completed before backlog execution (v14.2.25)

Наступні проблеми з оригінального аудиту **вже вирішені** і не є backlog-задачами:

- Порожні catch-блоки: `LocalizationInstallService.CleanupFile`, `RestoreBackupService` rollback cleanup, `SelfUpdatePreparationService.SafeDelete` — тепер логують Warning
- `MainForm.OnReleasePollFailed` — handler та підписка видалені (poller сам логує)
- Мертвий код `RefreshStateAsync` (`installedApiMode`, `exactSelectedTarget`) — видалено
- Бейдж «Доступно» на звичайних картках — прибрано (StateText = null для ordinary installable)
- Прогрес-бар 230px — тепер responsive (Dock=Fill, MinimumSize=160)
- «Скасувати» після завершення — тепер видимий лише при cancellable states

## Architecture decisions (revised)

### 1. Немає generic GameFileTransaction

Три сервіси (`LocalizationInstallService`, `RestoreOriginalService`, `RestoreBackupService`) мають різну семантику: install з API, restore official/snapshot, restore historical backup з historical state. Generic transaction engine вимагав би callbacks/options/flags і збільшив би складність.

**Замість цього:** централізувати лише raw installation-state операції (див. Stage B нижче).

### 2. Немає generic RunExclusiveOperationAsync

Application self-update має materially different successful handoff path (Process.Start + Application.Exit). Generic runner з callbacks/options не виправданий.

**Замість цього:** якщо дублювання буде переглянуто пізніше, дозволені лише маленькі хелпери (common begin-operation setup, CTS disposal, feed/poller resume cleanup).

### 3. MainForm decomposition — фізична, не архітектурна

Розділити на partial files без зміни runtime архітектури. Без контролерів, без DI redesign, без зміни власності сервісів.

### 4. Sync IO — measurement-first

Не конвертувати speculative в async. Спочатку виміряти, чи є реальний UI stall. Конвертувати лише конкретний call path при підтвердженій проблемі.

### 5. BackupStore split — не заплановано

Не розділяти лише через розмір файлу. Snapshot/restore-point/replace/recovery behavior safety-sensitive і добре протестований. Розділення — тільки при майбутній конкретній потребі.

## Scope

**In scope:**
- Спільні примітиви: `GamePaths`, source-маркери, reuse `AppPaths.InstallationFile`
- Централізація raw installation-state capture/restore
- Фізична декомпозиція MainForm.cs на partial files
- Вимірювання sync IO responsiveness (investigation only)
- Опціонально: показ патчу гри в UI

**Out of scope:**
- Generic GameFileTransaction / transaction engine
- Generic RunExclusiveOperationAsync
- BackupStore split
- Speculative async conversion
- Зміна API contract, behavior операцій, архітектурних шарів
- Контролерний/сервісний redesign MainForm

## Contracts / decisions

1. **Behavior-free рефакторинг:** усі етапи не змінюють зовнішню поведінку операцій (§30.3).
2. **Тести — страховка:** кожен етап закінчується `dotnet build` + `dotnet test --no-build`; усі поточні тести мають пройти (окрім тестів, що оновлюються під нові internal-сигнатури).
3. **Маленькі кроки (§32.3):** кожен підетап — окремий коміт.
4. **No persisted schema change:** жоден етап не змінює формат `installation.json`, `config.json` або backup metadata.

## Roadmap

### Stage A — Shared path/source primitives (low risk) — COMPLETED / REVIEWED / ACCEPTED

Немає жодної залишкової implementation-задачі Stage A. A.1/A.2/A.3 — усі завершені, переглянуті архітектором та прийняті.

- **A.1** `GamePaths` (static): константи `AdsDirName`, `LocalizationFileName` + `GetLocalizationFilePath(gameRoot)`. Замінити 5+ місць у MainForm/Services. `GameDetector`/`BackupStore` приватні константи → делегувати на `GamePaths`. — **COMPLETED**
- **A.2** Source-маркери `"api"`/`"official"` → `InstallationSource` static-константи в Storage; замінити 9 місць. — **COMPLETED**
- **A.3** Reuse `AppPaths.InstallationFile` замість ручного `Path.Combine(StateDir, "installation.json")` у `ReadRawInstallationState` та rollback-методах. — **COMPLETED**

### Execution order (intentional, not alphabetical)

Порядок виконання навмисно відрізняється від алфавітної нумерації етапів:

**Stage C → background-tray-notifications → Stage B → Stage D → Stage E**

- `background-tray-notifications` — окремий BACKLOG-план (див. `docs/plans/backlog/background-tray-notifications.md`), виконується після Stage C і до Stage B.
- Stage B (raw installation-state centralization) є safety-critical і **навмисно відкладено** до реалізації tray/background-функції: воно не розблоковує tray і несе ризик доцільно брати лише після вищопріоритетного tray-функціоналу.
- Stage C залишається Stage C, Stage B залишається Stage B — ідентифікатори не перейменовуються під алфавітний порядок.

### Stage C — MainForm physical decomposition (low risk) — COMPLETED / REVIEWED / ACCEPTED

Розділити `MainForm.cs` на coherent partial files без зміни runtime архітектури. Без контролерів, без DI redesign.

Архітектором затверджений точний порядок фізичної декомпозиції (execution sequence, не алфавітний):

- **C.1 Presentation extraction** — `MainForm.Presentation.cs`: theme/shell layout, game-status presentation, operation/control presentation, mode-card presentation, public presentation helpers, designer-referenced visual helper. — **COMPLETED**
- **C.2 Startup extraction** — `MainForm.Startup.cs`: `MainForm_Shown`, startup lifecycle maintenance. — **COMPLETED**
- **C.3 Localization extraction** — `MainForm.Localization.cs`: modes, game detect/browse, selection/config routing, release-feed application, `RefreshStateAsync`. — **COMPLETED**
- **C.4 Operations extraction** — `MainForm.Operations.cs`: `HandleInstallAsync`, `RestoreOriginalButton_Click`, `HandleRestoreOriginalAsync`, `CancelButton_Click`, `MapInstallError`, `MapRestoreError` (фізичний partial, НЕ абстракція/контролер/сервіс). — **COMPLETED**
- **C.5 ApplicationUpdate extraction** — `MainForm.ApplicationUpdate.cs`: update check, staging, handoff. — **COMPLETED**

Обов'язкові обмеження Stage C (затверджено архітектором):
- Усі fields та constructor залишаються у `MainForm.cs`.
- `WireEventHandlers` залишається у `MainForm.cs`.
- `MainForm_FormClosing` залишається у `MainForm.cs`.
- `LogsButton_Click` залишається у `MainForm.cs`.
- Файл `MainForm.Lifecycle.cs` у Stage C НЕ створюється.
- `MainForm.Operations.cs` — лише фізичний partial, не абстракція/контролер.
- `RefreshStateAsync` належить `MainForm.Localization.cs` (C.3), а не Presentation.
- `RefreshModeCardLayout` залишається з localization/mode підсистемою (C.3).
- Tray/background залишається BACKLOG — не реалізовувати у Stage C.

Прикладний розподіл partial-файлів:
- `MainForm.cs` — constructor, DI, fields, WireEventHandlers, MainForm_FormClosing, LogsButton_Click
- `MainForm.Presentation.cs` — theme/shell layout, game-status, operation/control presentation, mode-card presentation, public helpers, BuildLogsIcon
- `MainForm.Startup.cs` — MainForm_Shown, startup lifecycle
- `MainForm.Localization.cs` — modes, game detect/browse, selection/config routing, release-feed application, RefreshStateAsync
- `MainForm.Operations.cs` — HandleInstallAsync, RestoreOriginalButton_Click, HandleRestoreOriginalAsync, CancelButton_Click, MapInstallError, MapRestoreError (фізичний partial, без нової абстракції)
- `MainForm.ApplicationUpdate.cs` — update check, staging, handoff

Безпосередня наступна дія — **Owner decision: activate background-tray-notifications or select another task**. Stage C C.1–C.5 фізична декомпозиція повністю реалізована та фінально переглянута архітектором (COMPLETED / REVIEWED / ACCEPTED): partial-власність, `MainForm_FormClosing` та self-update handoff прийняті без змін, `MainForm.Lifecycle.cs` не створено, жодного generic operation runner не додано. Tray залишається BACKLOG — не реалізовано, активація вимагає явного owner-рішення. Stage B залишається відкладеним (після tray).

### Stage B — Raw installation-state operations (low risk, correctness) — DEFERRED (після tray)

Централізувати дубльовані byte-level операції з installation state файлом. **Не** об'єднувати install/restore orchestration. **Не** вводити GameFileTransaction. **Не розпочато.**

- **B.1** Один canonical метод захоплення raw state (`ReadRawInstallationState` → на `InstallationStateStore` або поруч).
- **B.2** Один canonical метод атомарного відновлення raw state (temp → Replace/Move → byte-verify → cleanup). Зберегти present/absent semantics, cancellation behavior, rollback safety.
- **B.3** Замінити 3+ реалізації `ReadRawInstallationState` та 4 реалізації атомарного запису на спільні виклики.

### Stage D — Responsiveness investigation (investigation only)

Виміряти, чи створює sync local file IO реальні UI stalls. Не конвертувати speculative в async.

- **D.1** Профілювання `_stateStore.Load()`, `_configStore.Load()`, `AdsFilesPatchReader.TryReadPatch`, `GameDetector.ValidateGamePath` на UI-потоку.
- **D.2** Якщо підтверджено stall — окрема implementation task для конкретного call path.

### Stage E — Remaining UX ideas (optional)

- **E.1** (OPTIONAL) Блок «Гра»: показувати патч гри (`AdsFilesPatchReader`): «✓ Гру знайдено • patch 398».
- **E.2** (OPTIONAL) Картки режимів: hover-стан для візуального фідбеку клікабельності (якщо ще не достатньо).

Не додавати: роботу над «Доступно» бейджем (завершено), responsive progress (завершено), Cancel visibility (завершено), persistent selected-card highlighting (не планується).

## Acceptance criteria

- `dotnet build BdoUaClient.sln` — без помилок (§33.6)
- `dotnet test BdoUaClient.sln --no-build` — усі поточні тести зелені після кожного етапу (§33.7)
- Behavior операцій не змінено: install/restore/rollback сценарії покриті існуючими тестами
- Немає дублікатів `ReadRawInstallationState` / атомарного запису стану після Stage B
- MainForm розділений на partial files після Stage C
- Жоден етап не вводить generic transaction engine або generic operation runner

## Non-goals

- Generic GameFileTransaction / transaction engine
- Generic RunExclusiveOperationAsync
- BackupStore split
- Speculative async conversion
- Зміни API contract, behavior операцій, архітектурних шарів
- Контролерний/сервісний redesign MainForm

## Risks / dependencies

- **Safety-critical код:** Stage B торкається §14/§15/§38 (game files, backup, rollback). Мітигація: маленькі кроки, сильне тестове покриття.
- **Конфлікт з `client-ui-redesign`:** не актуально — `client-ui-redesign` вже заархівовано (Stage 4 + v1.1.2 stable). Stage C змінює MainForm у межах поточного ACTIVE PRIMARY плану.
- **Test seams:** перенесення методів InstallationStateStore/BackupStore вимагає оновлення тестових підкласів.

## Current progress

v14.2.25 завершив targeted hygiene та launcher-polish items (порожні catch, dead code, «Доступно» бейдж, responsive progress, Cancel visibility). `client-ui-redesign` заархівовано після v1.1.2. План активовано як ACTIVE PRIMARY. **Stage A COMPLETED / REVIEWED / ACCEPTED:** A.1 GamePaths, A.2 InstallationSource, A.3 AppPaths.InstallationFile reuse — усі COMPLETED. Поточний етап: **Stage C — MainForm physical decomposition — COMPLETED / REVIEWED / ACCEPTED**; фінальний архітектурний review/lifecycle handoff завершено. Наступна дія вимагає явного owner-рішення: активувати BACKLOG `background-tray-notifications` або обрати інше завдання. Після tray виконується Stage B (raw installation-state centralization, навмисно відкладено через safety-critical ризик).

### Hotfix interruption (2026-08-27)

**Reason:** managed localization hash mismatch after game/launcher overwrite was misclassified as `Corrupted` when patch number did not advance. Production v1.1.2 reproduced: installed localization → game/launcher replaced file → same patch → `Corrupted` shown.

**Fix:** added `LocalizationPatchTransition.ManagedFileChanged`; hash mismatch for valid API installation now resolves to `UpdateAvailable` (if current release available) or `WaitingForRelease` (if not), regardless of patch advancement. Higher-patch detection (`GameFileReplacedAfterPatch`) preserved.

**Status:** COMPLETED/ACCEPTED. Owner reproduced real launcher-restored-file scenario (2026-08-27): preview showed "Доступне оновлення" / "Оновити", update completed, state returned UpToDate, restart remained UpToDate.

**Resume next:** Stage A повністю завершено (A.1/A.2/A.3 COMPLETED/ACCEPTED); поточний етап — Stage C (див. вище).
