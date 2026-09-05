# Plans Registry

## Current focus

**Primary:** `code-quality-ux-improvements`
**Current phase:** code-quality-ux-improvements — Stage B / B.2 implemented, pending architect review
**Next:** Architect review B.2, then B.3 raw-state call-site migration

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| 1 | `code-quality-ux-improvements` | [active/code-quality-ux-improvements.md](active/code-quality-ux-improvements.md) | PRIMARY; Stage B / B.2 — IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW | Architect review B.2, then B.3 raw-state call-site migration |

## Backlog

| Order | ID | Plan | Depends on |
|---|---|---|---|

## Archive

| ID | Plan | Coverage |
|---|---|---|
| `initial-implementation` | [archive/initial-implementation-plan.md](archive/initial-implementation-plan.md) | Stage 1–12.2 |
| `client-self-update` | [archive/client-self-update.md](archive/client-self-update.md) | v13.1.2–v14.0.1; ZIP self-update E2E completed |
| `client-ui-redesign` | [archive/client-ui-redesign.md](archive/client-ui-redesign.md) | Completed native WinForms launcher redesign through Stage 4; stable v1.1.2 validation |
| `background-tray-notifications` | [archive/background-tray-notifications.md](archive/background-tray-notifications.md) | Tray/background T1–T6 completed, reviewed, accepted and released in stable v1.2.0 |

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
