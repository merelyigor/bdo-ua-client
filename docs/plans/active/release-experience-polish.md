# Release experience polish

Plan ID: `release-experience-polish`
Status: ACTIVE
Focus: PRIMARY
Backlog order: —
Implementation authorization: **YES**
Current phase: R2 implemented / validated / pending architect review
Next action: Architect review R2, then R3 Release Candidate v1.2.2
Dependencies: stable v1.2.1

## Goal

Завершити два bounded post-release покращення release/update experience без broad redesign: довести application-update discovery у резидентному tray-процесі до очікуваної поведінки та перейти до детермінованих структурованих public release notes.

## Roadmap

### R1 — Background application-update discovery and notification — COMPLETED / REVIEWED / ACCEPTED

Application-update discovery належить `MainForm.ApplicationUpdate.cs` і використовує один `System.Windows.Forms.Timer` приблизно на 5 хвилин, існуючі `_updateCheckCts`, `_updateCheckTask`, `GitHubUpdateClient` та `UpdateSelectionPolicy`. Перевірки є single-flight, продовжуються у tray, restore і «Перевірити зараз» запитують свіжу перевірку, а приховане вікно отримує окреме RAM-only notification з dedup за application tag. Завантаження та встановлення залишаються explicit user action через існуючу кнопку.

`ReleaseFeedPoller` і localization notification tracker залишаються незалежними; persisted schema, self-update contract і handoff ordering не змінюються.

### R2 — Deterministic structured public release notes — IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW

Approved direction:

- git history залишається provenance, а не public release copy;
- structured release fragments стають source of truth для public notes;
- generator комбінує fragments, immutable release metadata та standard installation/download boilerplate;
- CI не має LLM/API dependency;
- commit-subject heuristic mapping не є primary source;
- internal plan/task labels не потрапляють у public notes.

Реалізація: schema-v1 `docs/releases/NEXT.json`, deterministic generator/validator без `git log`, internal-token guard, standalone script tests у normal CI та explicit structured-source input у Release Candidate workflow. Під час R2 також виправлено stale `AGENTS.md` §41.2, який ще описував startup-only application-update discovery після прийняття R1.

### R3 — Stable release handoff — NOT STARTED

Очікуваний target після R1/R2 review: `v1.2.2`.

У цьому плані v1.2.2 не готується до завершення R1/R2 review.

## Acceptance criteria

- R1 periodic/restore/check-now application discovery працює через один request path і не має overlapping requests.
- Hidden application-update notification одноразова для кожного tag; visible discovery не показує balloon.
- Немає automatic download/install, persisted schema change або змін localization notification semantics.
- R1 має focused tracker tests і повний build/test validation та прийнятий зовнішнім архітектором.
- R2 має schema-v1 source, deterministic validation/rendering, CI script tests і RC structured-source integration; очікує external architect review.

## Current progress

R1 externally accepted. R2 implementation і validation завершені; `NEXT.json` seeded user-facing copy для майбутнього v1.2.2; наступна дія — `Architect review R2, then R3 Release Candidate v1.2.2`. R3 не розпочато.
