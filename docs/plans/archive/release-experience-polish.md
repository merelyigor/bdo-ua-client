# Release experience polish

Plan ID: `release-experience-polish`
Status: ARCHIVED
Focus: —
Backlog order: —
Implementation authorization: **YES**
Current phase: roadmap completed / reviewed / accepted — released in stable v1.2.2
Next action: none; no remaining implementation action
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

### R3 — Stable release handoff — COMPLETED / REVIEWED / ACCEPTED

Target: `v1.2.2`. Release Candidate #29 успішний, стабільний реліз опубліковано; публікація GitHub Release залишалася owner action.

Release facts: release ID `383636389`, tag `v1.2.2`, public asset `BDO-UA-Client-v1.2.2-win-x64.zip`. Після публікації власник успішно виконав built-in update до `v1.2.2`; застосунок після оновлення працює.

## Acceptance criteria

- R1 periodic/restore/check-now application discovery працює через один request path і не має overlapping requests.
- Hidden application-update notification одноразова для кожного tag; visible discovery не показує balloon.
- Немає automatic download/install, persisted schema change або змін localization notification semantics.
- R1 має focused tracker tests і повний build/test validation та прийнятий зовнішнім архітектором.
- R2 має schema-v1 source, deterministic validation/rendering, CI script tests і RC structured-source integration та прийнятий зовнішнім архітектором.
- R3 завершено: RC/package validation і owner publication виконані для stable `v1.2.2`.

## Current progress

R1, R2 і R3 externally accepted. Stable `v1.2.2` опубліковано; факти релізу заархівовано, а `NEXT.json` скинуто для наступного циклу. План завершено й заархівовано; remaining implementation action немає.
