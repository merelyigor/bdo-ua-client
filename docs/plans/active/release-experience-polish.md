# Release experience polish

Plan ID: `release-experience-polish`
Status: ACTIVE
Focus: PRIMARY
Backlog order: —
Implementation authorization: **YES**
Current phase: R1 implemented / validated / pending architect review
Next action: Architect review R1, then R2 deterministic structured release notes
Dependencies: stable v1.2.1

## Goal

Завершити два bounded post-release покращення release/update experience без broad redesign: довести application-update discovery у резидентному tray-процесі до очікуваної поведінки та перейти до детермінованих структурованих public release notes.

## Roadmap

### R1 — Background application-update discovery and notification — IMPLEMENTED / VALIDATED / PENDING ARCHITECT REVIEW

Application-update discovery належить `MainForm.ApplicationUpdate.cs` і використовує один `System.Windows.Forms.Timer` приблизно на 5 хвилин, існуючі `_updateCheckCts`, `_updateCheckTask`, `GitHubUpdateClient` та `UpdateSelectionPolicy`. Перевірки є single-flight, продовжуються у tray, restore і «Перевірити зараз» запитують свіжу перевірку, а приховане вікно отримує окреме RAM-only notification з dedup за application tag. Завантаження та встановлення залишаються explicit user action через існуючу кнопку.

`ReleaseFeedPoller` і localization notification tracker залишаються незалежними; persisted schema, self-update contract і handoff ordering не змінюються.

### R2 — Deterministic structured public release notes — NOT IMPLEMENTED

Approved direction:

- git history залишається provenance, а не public release copy;
- structured release fragments стають source of truth для public notes;
- generator комбінує fragments, immutable release metadata та standard installation/download boilerplate;
- CI не має LLM/API dependency;
- commit-subject heuristic mapping не є primary source;
- internal plan/task labels не потрапляють у public notes.

### R3 — Stable release handoff — NOT STARTED

Очікуваний target після R1/R2 review: `v1.2.2`.

У цьому плані v1.2.2 не готується до завершення R1/R2 review.

## Acceptance criteria

- R1 periodic/restore/check-now application discovery працює через один request path і не має overlapping requests.
- Hidden application-update notification одноразова для кожного tag; visible discovery не показує balloon.
- Немає automatic download/install, persisted schema change або змін localization notification semantics.
- R1 має focused tracker tests і повний build/test validation.
- R2 залишається NOT IMPLEMENTED до окремої реалізації та review.

## Current progress

R1 implementation і validation завершені; наступна дія — architect review R1. R2 не реалізовано. R3 не розпочато.
