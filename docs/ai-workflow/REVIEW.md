# External review contract

## Evidence hierarchy

Порядок джерел: diff → validation output із exit code → tests → runtime/CI evidence, artifacts, hashes → **і лише потім implementation report**. Report є supporting evidence, але не заміною inspectable repository evidence; звіт читається останнім саме тому, що він найзручніший і тому найлегше підміняє собою перевірку самої роботи.

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

Default для bounded low/medium-risk задач — один external Architect review після implementation prompt, validation, commit/push і CI. Corrective iteration створюється лише для `BLOCKER`, `IMPORTANT`, failed required validation або material baseline mismatch; `OPTIONAL` сам по собі не є підставою для нового prompt.

Після acceptance, якщо немає unresolved BLOCKER/IMPORTANT, approved dependent next step і активного незавершеного roadmap, Architect завершує цикл станом `WORK CYCLE COMPLETE / OWNER DECISION REQUIRED` та не створює автоматично новий audit, roadmap, refactoring, feature, release cycle або task. Наступний development cycle починає Owner.

Evidence policy: actual repository/diff/CI/test/artifact evidence має пріоритет. Structured agent evidence з exact SHA, run IDs і hashes допустимий fallback, коли external connector тимчасово недоступний; простого prose «все працює» недостатньо.

Task lifecycle: `IMPLEMENTED → VALIDATED → PENDING EXTERNAL REVIEW → REVIEWED / ACCEPTED`.

Release lifecycle: `RC READY → OWNER SMOKE ACCEPTED → RELEASED → PUBLIC VERIFIED → RELEASE REVIEWED / ACCEPTED`.

## Release review

Для release, де застосовно, перевіряються exact commit/tag target, CI, tests, version metadata, artifact composition/hash, manifest, generated public notes, draft/prerelease flags, public asset і live release після publication. Architect дає GO/NO-GO; Owner виконує manual publication/visual gates.
