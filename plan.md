# BDO UA Client — Active Plan

## Поточний baseline

- **Accepted through:** v12.2
- **Baseline SHA:** `5d79f567f8b1937a53ae68173021d89f0c813c5d`
- **Automated tests:** 306
- **Platform:** win-x64, self-contained, single-file
- **Executable:** `BDO-UA-Client.exe`
- **Release Build #2:** manually verified successful
- **Actions artifact:** verified as one EXE
- **Real Windows E2E:** in progress

## Поточна фаза

**Stage 12 — Real Windows E2E stabilization**

### v12.3 — fix mode selection and game detection UX

**Status:** IN PROGRESS

**E2E findings (confirmed in real Windows test):**
- Multiple localization RadioButtons could be selected simultaneously (each had separate parent FlowLayoutPanel)
- Ambiguous selection could install the wrong mode
- Cross-mode Install could remain disabled
- Game detection lacked persistent success confirmation
- Manual folder chooser accepted only exact game root
- Exact installed release had disabled Install without clear explanation
- Installed/selected concepts needed clearer UI labels

**Fixes:**
- RadioButtons now share same immediate parent (modesFlowPanel) — native WinForms mutual exclusion
- GetSelectedModeSlug hardened: returns null if ambiguous (multiple checked)
- Installed marker: "✓ Встановлено" on matching RadioButton
- Exact-installed message: "Цей реліз уже встановлено."
- Game status UI: persistent "✓ Гру знайдено" with green color
- Manual path resolution: accepts parent folder, resolves unique child
- Ambiguous multiple roots → rejected with message
- Invalid manual pick preserves existing valid game root
- Labels: "Встановлено:" / "Обрано:" for clarity
- Progress reset on mode change

**Acceptance:**
- [ ] Exactly one localization mode selected at all times
- [ ] Ambiguous selection cannot trigger install
- [ ] Installed A + selected B → Install enabled
- [ ] Exact installed target → Install disabled + explicit explanation
- [ ] Installed release marker visible
- [ ] Persistent game-found status visible
- [ ] Manual exact root works
- [ ] Manual unique immediate child root works
- [ ] Ambiguous multiple roots rejected
- [ ] Invalid manual pick preserves current valid game root
- [ ] Restore Original independent from selected mode
- [ ] Automated tests green
- [ ] Normal CI green
- [ ] Next Windows E2E pending

## Дорожня карта

### v12.3.x — E2E bugfix iterations
Only if next real Windows test finds concrete defects.

### v12.4 — Full manual E2E pass
Target scenarios:
- Startup detection
- Dynamic API modes
- Install first localization
- Restart persistence
- Switch mode A → B
- Same-mode newer release if naturally available
- Restore Original
- Reinstall after Restore Original
- Cancellation safety where practically testable
- Logs
- Single-file startup

### v12.5 — Release readiness
Target:
- Remaining runtime edge cases
- Clean Windows/self-contained validation
- Final user-facing messages/UX
- No known critical/major E2E defects
- Release candidate artifact

### v12.6 — Public release preparation
Target:
- Real GitHub Release workflow
- Version/tag strategy
- Release asset ZIP containing one EXE
- Final README
- Download/use instructions
- Release notes
- Traceability/version metadata as needed

### v1.0.0
First public release only after v12.4-v12.6 acceptance.

---

## Історичні плани

Детальний план Stage 1–12.2: [docs/plans/archive/initial-implementation-plan.md](docs/plans/archive/initial-implementation-plan.md)

Історія змін: Git commits.
