# Game Boundary Refactoring

Plan ID: `game-boundary-refactoring`
Status: ACTIVE
Focus: PRIMARY
Implementation authorization: **YES**
Current phase: Stage 2 — Per-Game Persistence Isolation (`IMPLEMENTED / VALIDATED / PENDING EXTERNAL PRE-COMMIT REVIEW`)
Next action: External Architect pre-commit review Stage 2; commit/push/CI only after explicit acceptance
Dependencies: none

## Goal

Підготувати поточний BDO-UA Client до майбутньої підтримки інших ігор через
мінімальний concrete game boundary без plugin framework, DI container або
зміни користувацької поведінки.

## Scope and invariants

- Black Desert залишається єдиною реалізованою грою.
- API contract, release feed schema, physical localization target та runtime
  behavior залишаються без змін.
- Install, update, restore, detection, tray/background, offline/degraded і
  self-update semantics залишаються функціонально еквівалентними.
- Нова абстракція додається лише там, де вона прибирає фактичне розпорошення
  game-specific facts.

## Roadmap

### Stage 1 — Explicit Game Boundary

Status: **REVIEWED / ACCEPTED**

- Concrete `BdoGameDefinition` володіє BDO identity, target/validation,
  detection identifiers/conventions та patch-reader composition.
- MainForm, GameDetector і localization/install/restore/backup services
  отримують потрібні game facts через цей boundary; persisted paths і schemas
  не мігруються.
- Focused regression coverage захищає target path, validation, patch reading,
  detection та існуючі install/restore/lifecycle suites.

### Stage 2 — Per-Game Persistence Isolation

Status: **IMPLEMENTED / VALIDATED / PENDING EXTERNAL PRE-COMMIT REVIEW**

- `GamePersistencePaths` володіє canonical game-scoped config, installation
  state та backup paths під `games/<stable-game-id>/`; application config, logs,
  cache і self-update sessions залишаються application-global.
- `LegacyBdoPersistenceMigrator` bounded-способом переносить історичний
  single-game BDO config/state/backups у canonical scope через same-volume move;
  canonical state має пріоритет, конфлікти не merge-яться, а невдала міграція
  не виконує destructive cleanup.
- JSON formats, backup contents, target file та transaction semantics не
  змінюються; додано isolation, migration, idempotency/conflict/fail-closed
  coverage.

Dependency: Stage 1 acceptance and explicit Stage 2 authorization. Game B is
not required for this persistence boundary.

## Explicit non-goals

Не створювати Game B, generic plugin/module loading, feed/API adapter,
configuration-driven game registry, database, feed/API adapter або MainForm
controller/presenter redesign.

## Validation / review state

Stage 1 Release build, focused game-boundary/detection/install/restore/lifecycle
tests та повний Release suite були green перед acceptance. Stage 2 є
persistence/data-migration change: Implementation Agent зупиняється перед
commit/push і не може самостійно позначити його `REVIEWED / ACCEPTED`.
