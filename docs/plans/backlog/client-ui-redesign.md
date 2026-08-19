# Client UI Redesign

Plan ID: `client-ui-redesign`
Status: **BACKLOG**
Backlog order: 1
Implementation authorization: **NO**
Next action: define and review detailed redesign requirements/roadmap before activation

---

## Goal

Future BDO UA Client UI redesign.

## Context

- A UI redesign is planned but not yet authorized for implementation.
- Detailed design/UX requirements will be developed separately.
- Must account for current application functionality and completed self-update work at the time it is activated.

## Scope

TBD — to be defined before activation.

## Contracts / decisions

- ReaLTaiizor NuGet package is available (v3.8.0.3)
- `ThemePrototype.cs` exists as visual reference (launch with `--prototype` flag)
- Design files: `docs/design/BDO_THEME_PLAN.md`, `docs/design/BDO_THEME_COLORS.md`

**Do NOT change:** business logic, event handlers, API, file operations, state management

## Roadmap

TBD — to be defined before activation.

## Acceptance criteria

TBD — to be defined before activation.

## Non-goals

- This plan must NOT interrupt current self-update implementation unless owner explicitly promotes/reprioritizes it.
- Do NOT read uncommitted owner ThemePrototype.cs/docs/design as canonical spec.

## Risks / dependencies

- Must account for completed self-update work when activated.
- Color palette reference: Background #121212/#1C1C1C/#2D2D2D, Accent #C8A415/#DCBA2B, Text #F0F0F0/#A0A0A0, Status #00B400/#FF4444.
