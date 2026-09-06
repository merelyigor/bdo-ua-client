# Release experience polish

Plan ID: `release-experience-polish`
Status: ACTIVE
Focus: PRIMARY
Backlog order: —
Implementation authorization: **YES**
Current phase: R3 release preparation / Release Candidate v1.2.2
Next action: Release Candidate v1.2.2, then packaged validation and owner publication
Dependencies: stable v1.2.1

## Goal

Завершити два bounded post-release покращення release/update experience без broad redesign: довести application-update discovery у резидентному tray-процесі до очікуваної поведінки та перейти до детермінованих структурованих public release notes.

## Roadmap

### R1 — Background application-update discovery and notification — COMPLETED / REVIEWED / ACCEPTED

Application-update discovery належить `MainForm.ApplicationUpdate.cs` і використовує один `System.Windows.Forms.Timer` приблизно на 5 хвилин, існуючі `_updateCheckCts`, `_updateCheckTask`, `GitHubUpdateClient` та `UpdateSelectionPolicy`. Перевірки є single-flight, продовжуються у tray, restore і «Перевірити зараз» запитують свіжу перевірку, а приховане вікно отримує окреме RAM-only notification з dedup за application tag. Завантаження та встановлення залишаються explicit user action через існуючу кнопку.

`ReleaseFeedPoller` і localization notification tracker залишаються незалежними; persisted schema, self-update contract і handoff ordering не змінюються.

### R2 — Deterministic structured public release notes — COMPLETED / REVIEWED / ACCEPTED

Approved direction:

- git history залишається provenance, а не public release copy;
- structured release fragments стають source of truth для public notes;
- generator комбінує fragments, immutable release metadata та standard installation/download boilerplate;
- CI не має LLM/API dependency;
- commit-subject heuristic mapping не є primary source;
- internal plan/task labels не потрапляють у public notes.

Реалізація: schema-v1 `docs/releases/NEXT.json`, deterministic generator/validator без `git log`, internal-token guard, standalone script tests у normal CI та explicit structured-source input у Release Candidate workflow. Під час R2 також виправлено stale `AGENTS.md` §41.2, який ще описував startup-only application-update discovery після прийняття R1. R2 externally reviewed and accepted після focused correction RC workflow summary wording.

### R3 — Stable release handoff — IN PROGRESS — RELEASE CANDIDATE v1.2.2

Target: `v1.2.2`. Release Candidate готується та запускається після успішного normal CI; публікація GitHub Release залишається owner action.

У цьому плані v1.2.2 не готується до завершення R1/R2 review.

## Acceptance criteria

- R1 periodic/restore/check-now application discovery працює через один request path і не має overlapping requests.
- Hidden application-update notification одноразова для кожного tag; visible discovery не показує balloon.
- Немає automatic download/install, persisted schema change або змін localization notification semantics.
- R1 має focused tracker tests і повний build/test validation та прийнятий зовнішнім архітектором.
- R2 має schema-v1 source, deterministic validation/rendering, CI script tests і RC structured-source integration та прийнятий зовнішнім архітектором.
- R3 у release handoff: target `v1.2.2`; після RC потрібні packaged validation і owner publication.

## Current progress

R1 і R2 externally accepted. `NEXT.json` містить user-facing copy для v1.2.2. R3 in progress — release preparation / Release Candidate v1.2.2; наступна дія — `Release Candidate v1.2.2, then packaged validation and owner publication`. План не вважається завершеним або released до owner publication.
