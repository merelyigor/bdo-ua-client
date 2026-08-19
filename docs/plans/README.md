# Plans Registry

## Current focus

**Primary:** `client-self-update`
**Current phase:** v13.1.2 accepted
**Next:** v13.2 — add verified update package staging

## Active plans

| Focus | ID | Plan | Current phase | Next |
|---|---|---|---|---|
| **PRIMARY** | `client-self-update` | [active/client-self-update.md](active/client-self-update.md) | v13.1.2 accepted | v13.2 — add verified update package staging |

## Backlog

| Order | ID | Plan | State / Next action |
|---|---|---|---|
| 1 | `client-ui-redesign` | [backlog/client-ui-redesign.md](backlog/client-ui-redesign.md) | Requirements/roadmap to be defined before activation |

## Archive

| ID | Plan | Coverage |
|---|---|---|
| `initial-implementation` | [archive/initial-implementation-plan.md](archive/initial-implementation-plan.md) | Stage 1–12.2 |

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
- Detailed plan rules live in the plan file, not duplicated into AGENTS.md
