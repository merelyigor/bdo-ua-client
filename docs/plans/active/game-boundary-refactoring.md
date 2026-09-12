# Game Boundary Refactoring

Plan ID: `game-boundary-refactoring`
Status: ACTIVE
Focus: PRIMARY
Implementation authorization: **YES**
Current phase: Stage 1 — Explicit Game Boundary (`IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW`)
Next action: External Architect review Stage 1; after acceptance, explicit Stage 2 authorization
Dependencies: none

## Goal

Підготувати поточний BDO-UA Client до майбутньої підтримки інших ігор через
мінімальний concrete game boundary без plugin framework, DI container або
зміни користувацької поведінки.

## Scope and invariants

- Black Desert залишається єдиною реалізованою грою.
- API contract, release feed schema, persistence schema та фізична структура
  `%LocalAppData%\BDO-UA-Client` не змінюються.
- Install, update, restore, detection, tray/background, offline/degraded і
  self-update semantics залишаються функціонально еквівалентними.
- Нова абстракція додається лише там, де вона прибирає фактичне розпорошення
  game-specific facts.

## Roadmap

### Stage 1 — Explicit Game Boundary

Status: **IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW**

- Concrete `BdoGameDefinition` володіє BDO identity, target/validation,
  detection identifiers/conventions та patch-reader composition.
- MainForm, GameDetector і localization/install/restore/backup services
  отримують потрібні game facts через цей boundary; persisted paths і schemas
  не мігруються.
- Focused regression coverage захищає target path, validation, patch reading,
  detection та існуючі install/restore/lifecycle suites.

### Stage 2 — Per-Game Persistence Isolation

Status: **NOT STARTED**

- Виділити game-scoped config/state/backups лише після concrete second-game
  contract.
- Зберегти backward compatibility існуючих BDO даних через explicit
  migration/isolation/rollback tests.

Dependency: Stage 1 external acceptance and an approved second-game contract.

## Explicit non-goals

Не створювати Game B, generic plugin/module loading, feed/API adapter,
configuration-driven game registry, database, storage migration або MainForm
controller/presenter redesign до появи окремого approved contract.

## Validation / review state

Stage 1 Release build, focused game-boundary/detection/install/restore/lifecycle
tests та повний Release suite мають бути green перед commit. Implementation Agent
не може самостійно позначити Stage 1 як `REVIEWED / ACCEPTED`.
