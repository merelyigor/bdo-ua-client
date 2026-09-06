# Handoff and persistence

## Implementation report

Кожен implementation report має містити щонайменше:

- RESULT;
- changed files;
- concise implementation summary;
- exact validation results;
- unresolved issues;
- important deviations/decisions;
- commit subject/SHA, якщо committed;
- push result, якщо pushed;
- CI status лише якщо фактично verified.

Task prompt може вимагати richer exact output.

## Reporting integrity

Не приховувати failing tests, skipped/unavailable validation або scope deviations. Не заявляти green CI без перевірки. Baseline mismatch, unavailable tooling та unresolved issues звітуються прямо.

## Commit/push handoff

Дотримуватися [AGENTS §34](../../AGENTS.md). Перед commit Implementation Agent перевіряє `git status`, `git diff`, scope і secret exposure risk, а також потрібну docs/context synchronization. Commit/push виконуються лише за explicit authorization task.

## Repository persistence

Важливий engineering state не повинен жити лише в external coordination. Persist material facts у правильному source:

- `AGENTS.md` — mandatory global rules;
- `docs/ai-workflow/` — orchestration;
- `docs/plans/` — roadmap lifecycle;
- `CURRENT.md` — living handoff;
- `history/` — chronological decisions;
- `docs/releases/` — release state/history;
- subsystem docs — technical contracts.

## New-session handoff

Нова session відновлює `AGENTS.md`, ai-workflow docs, CURRENT, relevant plan, journal, git history/diff, relevant source/tests/docs і current explicit task prompt. Повний transcript старих conversations не є prerequisite.

Під час відновлення external Architect session з нуля Owner може використати [PROJECT_CHAT_RULES.md](PROJECT_CHAT_RULES.md) як portable project instruction, а потім bootstrap поточний repository state з `AGENTS.md`, `CURRENT.md`, plans і source. Цей файл не є обов'язковим для Implementation Agent.

External coordination не зберігає personal details, irrelevant chat history, private account metadata, tokens, credentials, secrets або непотрібні private URLs. Persist лише project-relevant facts.

## Architect handoff

Architect отримує implementation report, exact commit/diff, validation/build/CI evidence і artifacts/logs/hashes, коли релевантно. Dependent next prompt зазвичай створюється після review. Якщо CI/artifact/manual validation недоступні, limitation фіксується без припущень, а Architect вирішує достатність evidence.
