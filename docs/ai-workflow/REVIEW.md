# External review contract

## Evidence hierarchy

Перевага надається actual commit/diff, changed source, tests, logs, build output, CI, artifacts, hashes і live release/runtime evidence, коли вони релевантні. Implementation report є supporting evidence, але не заміною inspectable repository evidence.

## Review checklist

Перевіряються requirements і acceptance criteria, correctness, scope, compatibility, architecture, error handling, edge cases, security, concurrency/data integrity, performance, tests, validation, plan/docs/CURRENT synchronization, unresolved issues і public-repository/secret hygiene.

## Severity

**BLOCKER** — incorrect behavior, crash, security defect, data corruption/loss risk, broken public contract/API, safety violation, unmet mandatory requirement або release-integrity failure.

**IMPORTANT** — substantial defect або inconsistency, яку слід виправити до dependent progress чи release.

**OPTIONAL** — non-required improvement, polish або cleanup, що не перешкоджає acceptance.

Поки BLOCKER/IMPORTANT не закриті, implementation iterations на OPTIONAL не витрачаються.

## Outcomes and correction loop

External outcomes: `ACCEPTED`, `NEEDS CORRECTION`, `BLOCKED / INSUFFICIENT EVIDENCE`.

Для correction Architect формулює focused prompt із confirmed issue та validation. Implementation Agent corrects, validates і reports; Architect повторно перевіряє correction та affected invariant і лише тоді accepts або просить наступну correction.

Малий diff із green relevant validation і без risky behavior може потребувати одного focused review. Finalization iteration без реального issue не створюється. Якщо report суперечить diff/tests/logs, repository evidence wins.

## Release review

Для release, де застосовно, перевіряються exact commit/tag target, CI, tests, version metadata, artifact composition/hash, manifest, generated public notes, draft/prerelease flags, public asset і live release після publication. Architect дає GO/NO-GO; Owner виконує manual publication/visual gates.
