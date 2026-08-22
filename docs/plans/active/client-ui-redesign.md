# Client UI Redesign

Plan ID: `client-ui-redesign`
Status: **ACTIVE**
Backlog order: 1
PRIMARY: **YES**
Implementation authorization: **YES**
Current phase: **Stage 2 completed / Stage 3 next**
Next action: implement Stage 3 — UI completion

---

## Goal

Apply BDO dark theme (black + gold) to MainForm. Make the application visually appealing while preserving all existing functionality.

## Context

- Current UI uses default Windows Forms styling (light theme, system colors)
- User requested BDO-themed dark UI (black + gold accents)
- The owner-local prototype source is preserved as `ThemePrototype.cs.reference.txt` visual/reference material only; it is not compiled, not wired through a `--prototype` launch mode in `Program.cs`, and is not a committed canonical file.
- ReaLTaiizor is not a committed production dependency. It was used only by the owner-local prototype as the `MaterialForm` base type and is not part of the production project.
- Production UI remains native .NET 8 WinForms with no new UI/framework NuGet dependency.
- Design direction: **Material-inspired BDO dark design**. Use Material principles for visual hierarchy, spacing, surfaces/cards, typography, interaction states, and presentation controls while retaining the Black Desert black/gold identity. This is not adoption of a Material framework: production remains native .NET 8 WinForms with no ReaLTaiizor, MaterialSkin, Metro framework, or other new UI dependency. Adapt the principles to a compact Windows desktop client; do not blindly imitate Google's mobile Material components.

**Prototype findings (approved by user):**
- Dark background (#121212) works well
- Gold accents (#C8A415) for Install button and highlights
- Three mode radio buttons visible and readable
- Game info block compact, left-aligned
- Status bar with green/red state colors
- Progress bar with gold color

## Scope

### In scope
- Color palette application to all MainForm controls
- Styling GroupBoxes, Labels, Buttons, ProgressBar, TextBox, RadioButton
- Updating dynamic controls (BuildDynamicModes radio buttons)
- Updating status color helpers (SetGameFound, ApplyLocalizationStatePresentation, etc.)
- Presentation-only layout restructuring where required by the approved visual design
- Presentation-only headers, cards, panels, labels, and other visual containers

### Out of scope
- New functional application controls, actions, or features
- Business logic changes
- API/service layer changes
- Game detection logic changes
- ReaLTaiizor production adoption
- Custom borderless window chrome or custom dialog framework

## Contracts / decisions

**Color palette (confirmed):**
```
Background (main):      #121212  (18,18,18)
Background (panels):    #1C1C1C  (28,28,28)
Background (controls):  #2D2D2D  (45,45,45)
Gold (accent):          #C8A415  (200,164,21)
Gold (hover):           #DCBA2B  (220,186,43)
Text (primary):         #F0F0F0  (240,240,240)
Text (secondary):       #A0A0A0  (160,160,160)
Success:                #00B400  (0,180,0)
Error:                  #FF4444  (255,68,68)
Border:                 #3D3D3D  (61,61,61)
```

**Architectural decisions:**
- Keep the native Windows title bar; do not implement custom borderless chrome, manual dragging, or custom minimize/maximize controls.
- Target approximately `800×650`, resizable, with approximately `700×500` minimum size.
- Keep WinForms scaling architecture and responsive `TableLayoutPanel`/`FlowLayoutPanel`/`Dock`/`Anchor` layout; do not copy prototype fixed coordinates.
- Validate at Windows scaling 100%, 125%, 150%, and 200%.
- Keep native `FolderBrowserDialog`, `MessageBox`, and ordinary Windows dialogs.
- Prefer a small UI-only theme/palette helper when needed; do not add a theme framework, DI system, or factory hierarchy.
- ReaLTaiizor is visual/reference-only and is not authorized as a production dependency.
- Stage 2 surface presentation: native dark card/panel containers replace the visible classic GroupBox composition while preserving functional control ownership and event wiring.
- Install button: gold accent (stand out from other buttons)
- RadioButton: white text on transparent background
- ProgressBar: gold foreground, dark background

**Do NOT change:** API/contracts, GameDetector internals, localization install/restore logic, backup/storage, compatibility policy, update selection/staging/replacement/rollback, release polling semantics, cancellation, operation mutual exclusion, file operations, or state ownership.

## Behavioral contracts

The redesign is presentation/layout work only. These contracts must remain unchanged:

1. Startup local detection and API loading run concurrently; API-assisted path detection starts only after local `NotFound` and available API patterns.
2. Localization modes remain API-driven and dynamically created; no modes are hardcoded.
3. Dynamic mode `Tag` remains the exact slug; `CheckedChanged`, `LastMode` persistence, feed-refresh selection restoration, and invalid-mode filtering remain intact.
4. The installed marker requires exact `ModeSlug` plus exact current `PublicId` matching.
5. All `LocalizationState` values and their current semantics remain intact.
6. `InstallActionPolicy` and compatibility gating remain authoritative; one `Install` action covers install/change/update.
7. Restore-original keeps its official-source-first and valid-snapshot fallback rules.
8. Operation mutual exclusion, control locking, release-poller pause/blocking, and pending-feed application remain intact.
9. `CancellationToken` cancellation and `FormClosing` safety remain intact.
10. All `OperationState` values and determinate/indeterminate progress mapping remain intact.
11. Diagnostic/error messages remain visible and scrollable; visual simplification must not hide them.
12. Background application update checks, update-button eligibility/visibility, staging/handoff/current-EXE safety, and `UpdateApplyingForm` behavior remain intact.

Relevant implementation sources include `MainForm.cs`, `MainForm.Designer.cs`, `Program.cs`, `UpdateApplyingForm.cs`, `Services/StartupCoordinator.cs`, `Services/DynamicModePolicy.cs`, `Services/LocalizationStateService.cs`, `Services/InstallActionPolicy.cs`, `Services/FeedApplicationCoordinator.cs`, `Services/ReleaseFeedPoller.cs`, and `Update/`.

## Roadmap

### Stage 0 — Canonical design contract and baseline

Finalize requirements, architecture boundaries, visual direction, behavioral contracts, local preview process, and plan activation. No runtime changes.

Acceptance: the plan is executable without architectural ambiguity.

### Stage 1 — Theme foundation

Introduce the approved palette, Segoe UI typography, common style values, base form/background presentation, and basic static control styling. Keep layout and behavior unchanged; do not redesign dynamic modes or operation/progress behavior yet.

Likely scope: `MainForm` presentation code and an optional small UI-only theme/palette helper.

Delivered: centralized BDO dark palette, static dark-theme readability, semantic Primary / Accent Secondary / Neutral / Destructive button roles with runtime enabled/disabled synchronization, and local development version display as `DEV`.

Acceptance: Release build, full solution tests, local preview publish, and owner manual visual review completed. Stage 1 is **COMPLETED** and owner-approved; Stage 2 was subsequently completed and owner-approved.

### Stage 2 — Static main-window layout and cards

Apply the approved visual hierarchy to the header, game section, status section, diagnostic area, footer/actions, and version/log utility area while preserving control responsibilities and event behavior.

**Status: COMPLETED — owner visual review ACCEPTED.**

Delivered architecture:

- Material-inspired BDO header and native WinForms card/surface composition for `Гра`, `Локалізація`, and `Стан`.
- Responsive approximately `800×650` layout with approximately `700×500` minimum size, content-driven vertical sizing, working-area height cap, and root vertical scrolling fallback.
- Dynamic localization modes remain API-driven and visible without changing their Stage 3 RadioButton presentation boundary.
- Status relationship hierarchy distinguishes the exact installed target, a different selected mode, and a same-mode release update; persisted installed metadata remains authoritative for the installed block.
- Action bar keeps `Install` as the left primary action and groups `Restore Original` and `Cancel` on the right.
- Initial startup selection prioritizes the currently installed API mode when still installable, without rewriting `LastMode`.

Final initial-selection invariant on startup:

`installed API ModeSlug if still installable → Config.LastMode → first installable mode → none`

This priority applies only to initial startup. User-driven selection continues to persist `LastMode`; background feed refresh preserves the current selection and does not force installed-mode selection. The exact installed marker remains `ModeSlug + PublicId`.

Stage 2 information architecture requirements:

- Preserve the three principal user concepts: `Гра`, `Локалізація`, and `Стан`.
- Introduce a compact application header with the conceptual hierarchy `BDO UA Client` and `Українська локалізація Black Desert Online`; version and logs may remain in an unobtrusive header/footer utility area.
- Prefer the clear Ukrainian section names `Гра`, `Локалізація`, and `Стан`; avoid technical wording such as `Режим локалізації` where `Локалізація` communicates the same concept.
- Display release metadata in the form `v3 · патч 398 · 21.08.2026`, using Ukrainian `патч` and omitting a redundant `реліз` before a contextual date. API fields and semantics remain unchanged.
- Avoid status duplication and distinguish factual installation from the current selection. For an exact selected target, show `✓ Обраний режим уже встановлено` with one `ВСТАНОВЛЕНО ЗАРАЗ` block and no target block. For a different mode, show `Обрано інший режим локалізації`, `ВСТАНОВЛЕНО ЗАРАЗ`, and `ОБРАНО ДЛЯ ВСТАНОВЛЕННЯ`. For the same mode with another release, show `Доступне оновлення локалізації` and `ДОСТУПНЕ ОНОВЛЕННЯ`. Critical factual states retain priority; business state and `InstallActionPolicy` remain authoritative.
- Prefer action-oriented plain Ukrainian, including `Обрати папку` instead of `Обрати вручну` where applicable. Label changes must not alter event semantics or introduce speculative behavior changes.

Validation completed: startup, resize, game found/not-found, default install/restore/cancel states, long game path, dynamic content states, install/restore flows, restart selection, local preview, and owner visual approval.

### Stage 3 — UI completion

Combine the remaining visual responsibilities into one owner-reviewed implementation stage without changing business behavior, API contracts, storage, installation, cancellation, updater internals, or adding UI dependencies.

#### Localization mode presentation

- Replace visible classic WinForms `RadioButton` controls with Material-inspired selectable mode cards.
- Make the entire card clickable and support normal, hover, selected, selected+hover, installed, disabled/incompatible, and keyboard-focus states.
- Use restrained BDO gold for selection; do not use a bright-yellow full-card surface.
- Keep installed distinct from selected; exact installed state remains `ModeSlug + PublicId`.
- Preserve API-driven creation, `ModeSlug` identity, `Tag`/equivalent identity, mutual exclusion, `CheckedChanged`-equivalent behavior, `LastMode`, installed-mode-first startup selection, feed-refresh selection restoration, invalid-mode filtering, fallback behavior, visible focus, Tab navigation, and Space/Enter activation.

#### Graphical flags

- Do not rely on native regional-indicator emoji rendering.
- Render recognized leading country flags graphically, supporting current UA/GB combinations at minimum.
- Unknown or unmapped flags must fail gracefully to readable text.
- Do not add an external emoji framework or hardcode localization modes.

#### Operation and progress presentation

Theme the presentation of `LoadingApi`, `DetectingGame`, `Downloading`, `Verifying`, `BackingUp`, `Installing`, `Restoring`, `Completed`, `Failed`, and `Cancelled`, including determinate/indeterminate progress, Cancel presentation, and operation locking. Preserve all `OperationState` semantics and cooperative cancellation. A small focused native WinForms progress control may be considered only if the approved visual design requires it; no UI framework is authorized.

#### Application update visual consistency

Theme `updateButton`, version/log utility presentation, update availability/busy states, and `UpdateApplyingForm` if required. Do not alter self-update selection, staging, replacement, rollback, helper protocol, or other updater internals.

#### Stage 3 validation

Stage 3 requires Release build, full solution tests, local preview publish, and owner visual review. Validate all mode-card states, flags, operation/progress states, cancellation, and update UI states before owner acceptance.

### Stage 4 — Final UI validation and regression closure

Validation-driven closure stage; do not introduce another broad redesign. Validate:

- Windows scaling at 100%, 125%, 150%, and 200%.
- Default, minimum, and larger resized window sizes.
- Dynamic vertical growth and scroll fallback.
- Long Ukrainian text and long filesystem paths.
- Tab order, visible keyboard focus, and Space/Enter activation of mode cards.
- Disabled-state readability.
- All localization relationship states, operation states, cancellation states, self-update UI states, and `UpdateApplyingForm` if themed.
- No clipping, overlap, or functional regression.

Fix only concrete defects found during this matrix. Stage 4 completion closes the UI redesign plan after owner acceptance and final automated validation.

## Acceptance criteria

- [ ] Form background is dark (#121212)
- [ ] All text is readable (white on dark)
- [x] Material-inspired dark card/surface layout replaces the visible classic GroupBox composition
- [ ] Install button is gold (#C8A415)
- [ ] Other buttons have dark background with visible borders
- [ ] ProgressBar is gold
- [ ] RadioButton text is readable in all modes
- [x] Status relationship presentation distinguishes installed, selected, and update states
- [ ] All existing functionality and behavioral contracts are preserved
- [ ] Native WinForms remains the production UI technology
- [ ] No ReaLTaiizor production dependency or `--prototype` launch mode is introduced
- [ ] Release build and full solution tests pass for each implementation stage
- [ ] Local preview is published after every meaningful visual stage
- [ ] Owner visual review is recorded separately from automated validation
- [ ] No layout, DPI, accessibility, or visual regression remains in final validation

## Non-goals

- This plan must NOT interrupt current self-update implementation unless owner explicitly promotes/reprioritizes it.
- Presentation-only layout restructuring is allowed where required by the approved redesign; existing functional responsibilities and event behavior must remain preserved.
- Presentation-only headers, cards, panels, labels, and other visual containers may be added. These are visual/presentation controls, not new functional application controls/actions.
- No new functional actions or features are authorized.
- Do not introduce or change business logic, API/service/state/file-operation behavior merely for redesign.
- Do NOT add UI/framework dependencies, including ReaLTaiizor, as part of this plan.
- Do not add a Material, Metro, ReaLTaiizor, or MaterialSkin framework; use the approved Material-inspired BDO presentation direction with native WinForms only.

## Risks / dependencies

- Must account for completed self-update work when activated.
- The obsolete owner-local ReaLTaiizor project scaffolding was removed from `BdoClient.csproj` and must not be reintroduced as part of this plan.
- `ThemePrototype.cs.reference.txt` and `docs/design/` remain owner-local reference materials; they are not production entry points or canonical runtime architecture.
- Color palette reference: Background #121212/#1C1C1C/#2D2D2D, Accent #C8A415/#DCBA2B, Text #F0F0F0/#A0A0A0, Status #00B400/#FF4444.

## Current progress

- [x] Read-only UI discovery completed
- [x] Canonical architecture and visual direction defined
- [x] Local preview validation policy defined
- [x] Plan activated as PRIMARY; Stage 2 is complete and Stage 3 is next
- [x] Stage 1 theme foundation implemented, locally previewed, and owner-approved
- [x] Stage 2 — Static Material-inspired layout and information hierarchy implemented, locally previewed, and owner-approved
- [ ] Stage 3 — UI completion
- [ ] Stage 4 — Final UI validation and regression closure

## Manual visual validation loop

Цей цикл є обов'язковим для кожного meaningful visual implementation або visual correction stage плану.

### Automated validation

Спочатку виконати stage-specific build/tests. Якщо пізніший stage не визначає сильнішу перевірку, мінімальні команди такі:

```bash
dotnet build BdoUaClient.sln -c Release
dotnet test BdoUaClient.sln -c Release --no-build
```

### Local preview publish

Після успішної automated validation опублікувати exact поточний worktree через canonical single-file Windows publish configuration з `docs/build.md`:

```bash
dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:AssemblyName=BDO-UA-Client -o artifacts/local-preview/win-x64
```

У Windows/PowerShell цю саму команду дозволено виконувати одним рядком з ідентичними arguments.

Очікуваний preview executable:

```text
artifacts/local-preview/win-x64/BDO-UA-Client.exe
```

Preview повинен відповідати exact implementation state, який передається owner для review.

### Artifact policy

`artifacts/local-preview/` — тимчасовий local output. Він повинен залишатися untracked, ніколи не stage-итися або commit-итися, не потрапляти до persistent development journal як binary content і бути безпечно перезаписуваним на наступному UI stage. `artifacts/` уже ігнорується; `.gitignore` не змінювати.

### Separate approval gates

`automated validation passed` і `owner visual review accepted` — окремі gates. Успішні build, tests або publish не означають visual acceptance. Stage може бути технічно готовим до preview, але залишається pending visual acceptance, доки owner не перегляне результат. Якщо owner повідомляє про visual defect, це feedback поточного stage; спочатку внести найменшу corrective change і повторити цей цикл, а вже потім переходити до залежного visual stage.

### Owner review and completion report

Owner може надати screenshots з local preview для architectural/UI review. Це manual workflow; автоматичний screenshot capture, screenshot test framework, image dependency або external service у межах цього plan не додаються.

Після кожного visual implementation/correction stage звіт повинен містити:

- чи пройшов build;
- чи пройшли tests;
- чи пройшов local publish;
- exact preview EXE path;
- preview EXE file size;
- змінені visual areas;
- стани, які owner має перевірити вручну;
- стани, недоступні природним шляхом, і special reproduction steps для них.

Мінімальний формат:

```text
Local preview ready:
artifacts/local-preview/win-x64/BDO-UA-Client.exe

Manual checks:
- startup/loading state
- game detected/not detected
- localization mode cards
- installed/update-available state
- download progress
- error message
- application update notification
```

Не заявляти, що UI візуально accepted, доки owner фактично не виконав review.

### GitHub CI relationship

Local preview publish є fast iteration path для visual inspection. GitHub CI залишається independent repository validation, коли він потрібен, але routine redesign iteration не повинна чекати або завантажувати GitHub Actions artifact лише для локального UI review. Release candidate і public release workflows залишаються без змін.
