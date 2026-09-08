# Offline / Degraded Release Feed Reliability

## Status

Status: ACTIVE
Focus: PRIMARY
Current phase: implementation complete / validation pending external review
Next action: External Architect review of offline/degraded release-feed mode

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

- Cache schema/store, normalized mapper і atomic best-effort persistence — IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW
- Startup live-first / cached fallback / unavailable state — IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW
- Background reconnect and source transition — IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW
- Cached write-action gate and degraded UX — IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW
- Storage, policy, poller and MainForm lifecycle coverage — IMPLEMENTED / VALIDATED / PENDING EXTERNAL REVIEW

## Compatibility invariants

- Online behavior remains authoritative and uses the existing canonical feed application path.
- Cached URLs and hashes never reach localization mutation services.
- Empty live mode lists replace stale cached cards.
- Poll failure preserves the current accepted feed and does not demote a live source.
- Existing operation serialization, rollback, tray lifecycle, GameDetector threading and self-update behavior remain unchanged.

## Validation handoff

Перед external review перевіряються Release build, повний test suite, focused cache/policy/poller/MainForm integration tests і git diff --check. Plan remains active until external review; acceptance is not self-claimed.
