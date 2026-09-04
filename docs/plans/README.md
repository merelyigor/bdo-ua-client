# Plans Registry

## Current focus

**Primary:** `code-quality-ux-improvements`
**Current phase:** background-tray-notifications — ACTIVE (T1 — Tray lifetime shell — COMPLETED / REVIEWED / OWNER ACCEPTED; T1.1 — Autostart / background startup — COMPLETED / REVIEWED / ACCEPTED; T2 — Operation / shutdown semantics — COMPLETED / REVIEWED / ACCEPTED; T3 — Background polling cadence — COMPLETED / REVIEWED / ACCEPTED; T4 — Local file-change trigger — COMPLETED / REVIEWED / ACCEPTED; T5 — Notifications / dedup — IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW; T6 — NOT IMPLEMENTED)
**Next:** T5 architect review, then T6 Owner E2E / resource validation

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| 1 | `code-quality-ux-improvements` | [active/code-quality-ux-improvements.md](active/code-quality-ux-improvements.md) | Stage C — MainForm physical decomposition — COMPLETED / REVIEWED / ACCEPTED | T5 — IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW; Stage B deferred until tray completion |
| 2 | `background-tray-notifications` | [active/background-tray-notifications.md](active/background-tray-notifications.md) | ACTIVE | T5 — IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW; next = architect review, then T6 |

## Backlog

| Order | ID | Plan | Depends on |
|---|---|---|---|

## Archive

| ID | Plan | Coverage |
|---|---|---|
| `initial-implementation` | [archive/initial-implementation-plan.md](archive/initial-implementation-plan.md) | Stage 1–12.2 |
| `client-self-update` | [archive/client-self-update.md](archive/client-self-update.md) | v13.1.2–v14.0.1; ZIP self-update E2E completed |
| `client-ui-redesign` | [archive/client-ui-redesign.md](archive/client-ui-redesign.md) | Completed native WinForms launcher redesign through Stage 4; stable v1.1.2 validation |

## Rules / lifecycle

**Lifecycle transitions:**
```
BACKLOG → (explicit owner decision) → ACTIVE → (completed/superseded) → ARCHIVE
```

**Key rules:**
- `docs/plans/README.md` = canonical plans registry
- Implementation plans live only in `active/`, `backlog/`, `archive/`
- No canonical `/plan.md` in repository root
- ACTIVE plans are executable roadmaps only after explicit task command
- BACKLOG plans must NOT be implemented automatically
- ARCHIVED plans are historical references only
- Exactly one plan should be marked PRIMARY
- Moving lifecycle state requires file move + registry update in same commit
- Folder status and registry status must always match
- Meaningful implementation commits must synchronize the active plan and this registry with factual validation/review status and the exact next action
- Implementation agents must not claim external review/acceptance that did not happen
- Detailed plan rules live in the plan file, not duplicated into AGENTS.md
