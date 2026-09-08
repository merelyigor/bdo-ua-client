# Offline / Degraded Release Feed Reliability

## Status

Status: ARCHIVED
Focus: —
Current phase: roadmap completed / reviewed / accepted — offline/degraded release-feed feature complete
Next action: none; no remaining implementation action

## Scope

Надати клієнту безпечний read-only degraded mode, коли /releases недоступний, без зміни API contract, installer safety workflow або self-update architecture.

## Decisions

- Канонічне джерело в live-сесії — успішна відповідь API.
- Нормалізований last-known feed зберігається окремо в %LocalAppData%\BDO-UA-Client\cache\release-feed.json.
- Кеш має явну schema version і UTC saved_at_utc; TTL не використовується.
- Cached feed дозволяє лише presentation і локальне read-only state resolution.
- Install, update, mode-switch mutation та Відновити оригінал доступні лише для Live feed.
- Corrupt, unsupported або incomplete cache відхиляється fail-closed; live session не стає невдалою через помилку запису кешу.
- install_path_patterns, history і transient diagnostics не кешуються.

## Milestones

- Cache schema/store, normalized mapper і atomic best-effort persistence — COMPLETED / REVIEWED / ACCEPTED
- Startup live-first / cached fallback / unavailable state — COMPLETED / REVIEWED / ACCEPTED
- Background reconnect and source transition — COMPLETED / REVIEWED / ACCEPTED
- Cached write-action gate and degraded UX — COMPLETED / REVIEWED / ACCEPTED
- Storage, policy, poller and MainForm lifecycle coverage — COMPLETED / REVIEWED / ACCEPTED

## Compatibility invariants

- Online behavior remains authoritative and uses the existing canonical feed application path.
- Cached URLs and hashes never reach localization mutation services.
- Empty live mode lists replace stale cached cards.
- Poll failure preserves the current accepted feed and does not demote a live source.
- Existing operation serialization, rollback, tray lifecycle, GameDetector threading and self-update behavior remain unchanged.

## Validation and final disposition

Release build, повний test suite, focused cache/policy/poller/MainForm integration tests і git diff --check пройдені; CI #189 успішний. External Architect review завершено: v15.46 — REVIEWED / ACCEPTED. Offline/degraded release-feed mode завершено без змін API contract, installer safety workflow, GameDetector threading або self-update architecture. План заархівовано; remaining implementation action немає.
