# Prompt contracts

## General principles

Architect prompt має бути self-contained, передавати approved conclusions, behavior/state/order/invariants, explicit scope, validation і expected output. Exact identifiers, files, commands і strings дозволені. Harmless local details залишаються Implementation Agent; syntax tutorial і зайве internal reasoning не потрібні.

## Canonical implementation prompt

Стандартна структура:

```text
## ROLE
## OBJECTIVE
## CONTEXT
## FILES TO INSPECT
## ALLOWED TO CHANGE
## DO NOT MODIFY
## REQUIRED IMPLEMENTATION
## PATTERNS TO PRESERVE
## EDGE CASES
## ACCEPTANCE CRITERIA
## VALIDATION
## OUTPUT
```

За потреби додаються `BASELINE`, `PLAN STATE`, `COMMIT`, `PUSH`, `RELEASE`, `SAFETY` або `MIGRATION`.

## Data/migration prompt

Для задачі, що переносить, імпортує або мігрує дані, prompt додатково фіксує: джерело даних, ідентичність звʼязку записів, поведінку для незвʼязаного залишку і три числа, які agent зобовʼязаний навести у звіті — було, стало, не звʼязано. «Import completed successfully» без чисел не є evidence: на великому наборі воно однаково звучить і при повному, і при порожньому результаті.

## Read-only inspection prompt

Prompt має прямо сказати: inspect only; do not modify files; повернути relevant files, current flow, dependencies, risks і implementation points; identify open architecture questions; do not start implementation і не redesign prematurely.

## Corrective prompt

Corrective prompt містить confirmed defect, root cause/failure point коли відомий, affected files, required corrected behavior, constraints і reproduction/validation. Формулювання лише `fix previous implementation` недостатнє. Corrective scope зазвичай менший за original scope.

## Release/validation prompt

Такий prompt окремо розмежовує allowed changes і validation-only actions, publication authority, immutable tag/artifact rules, GO/NO-GO gate та owner actions. Він не дозволяє створювати release/tag або заявляти publication без відповідної authority/evidence.

## Baseline contract

Коли точний стан repository має значення, implementation prompt визначає релевантний baseline: branch, expected HEAD/commit, expected worktree state, relevant CI state, relevant plan state та, коли застосовно, release/tag/version state. Не потрібно включати кожне поле для кожної задачі — лише факти, mismatch яких може зробити implementation assumptions недійсними.

Implementation Agent перевіряє required baseline до зміни файлів. Якщо material mismatch робить task contract недійсним, agent повинен STOP, повідомити actual state і повернути рішення Architect/Owner; не можна мовчки перебазувати task на новий стан. Minor irrelevant drift можна повідомити й обробити лише коли це дозволяють prompt і repository rules.

## Conflict handling

Implementation Agent STOP-ить, якщо prompt конфліктує з mandatory repository rule, allowed scope не задовольняє requirement, потрібне unapproved architecture/schema/dependency expansion або baseline invalidates contract. Conflict повертається Architect/Owner для рішення, а не вирішується мовчазним scope expansion.
