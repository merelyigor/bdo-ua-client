# Executable workflow

## Low-risk coherent flow

```text
ANALYZE
→ IMPLEMENTATION PROMPT
→ IMPLEMENT
→ TEST / DOCS / PLAN SYNC
→ VALIDATE
→ COMMIT / PUSH
→ REPORT
→ ARCHITECT REVIEW
→ ACCEPT or CORRECT
```

## Complex/high-risk flow

```text
UNDERSTAND
→ READ-ONLY INSPECTION
→ ANALYZE
→ ARCHITECTURE
→ DECOMPOSE
→ IMPLEMENTATION PROMPT
→ IMPLEMENT
→ VALIDATE
→ REPORT
→ ARCHITECT REVIEW
→ CORRECT IF NEEDED
→ FINAL ACCEPTANCE
```

Read-only investigation потрібна, коли current flow, architecture або risk недостатньо відомі. Вона не змінює файли, описує relevant files, data/control flow, dependencies, implementation points, risks і open questions; передчасний redesign не робиться.

Complex, safety-critical або ambiguous work слід split-ити за risk. Coherent low-risk task може об'єднати implementation, tests, docs, validation, commit і push. Artificial iterations, які не зменшують risk, не створюються.

## Task types and states

Task types: read-only investigation, implementation, corrective implementation, release/validation task, documentation/process task.

Factual states:

- до external review: `IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW`;
- після успішного external review: `COMPLETED / REVIEWED / ACCEPTED`;
- при material issue: `NEEDS CORRECTION`.

Dependent next task не починається, поки unresolved BLOCKER або IMPORTANT не закриті. OPTIONAL не блокує progress, якщо Owner/Architect явно не підвищив його пріоритет.

## Session policy and changes

Новий major feature, coherent roadmap stage або substantially different subsystem зазвичай починається у fresh Implementation Agent session. Focused corrective prompt для безпосереднього попереднього task і мала continuation можуть залишатися в тій самій session. Correctness не залежить від opaque memory; нова session відновлює repository context і explicit prompt.

Якщо Owner змінює material requirements mid-task, Implementation Agent не зливає їх мовчки: Architect переглядає architecture/scope і надає оновлений explicit contract. Unrelated defect лише звітується, якщо він не потрібен для task. Unrelated failing tests не приховуються; baseline failure відокремлюється від regression, а unavailable validation звітується як така.

Нуль ACTIVE планів валідний. Bounded owner-authorized task може виконуватися без roadmap; placeholder ACTIVE/PRIMARY не створюється. Broad product development зазвичай потребує approved roadmap/PRIMARY.

## Plans and persistent context

`docs/plans/README.md` є canonical registry. Meaningful task, що просуває ACTIVE plan, синхронізує plan і registry у тому самому commit. Implementation Agent не може external-accept себе. За [AGENTS §42](../../AGENTS.md) material state зберігається в CURRENT і append-only journal.

## Release flow

```text
release preparation
→ local validation
→ normal CI
→ Release Candidate
→ artifact/hash/version/release-note validation
→ Architect GO / NO-GO
→ Owner publication where manual
→ live release verification
→ post-release archive / NEXT reset / plan closure
```

Точний release contract визначають `AGENTS.md` і `docs/releases/`. Tag сам по собі не означає complete stable release. Manual owner action не вигадується й не вважається виконаною без evidence.

## Definition of Done

Окрім [AGENTS §33](../../AGENTS.md), task має мати satisfied acceptance criteria, validation evidence, scoped diff, синхронізовані docs/plan/context, якщо потрібно, і не мати unresolved BLOCKER/IMPORTANT. External review виконується, коли workflow вимагає acceptance.
