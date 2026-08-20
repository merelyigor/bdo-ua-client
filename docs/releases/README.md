# Процедура релізу

Production GitHub Releases завжди публікуються вручну власником репозиторію.

## Процес

### 1. Підготовка

- Переконайтеся, що `main` містить бажаний стан релізу
- Перевірте, що normal CI зелений

### 2. Release Candidate workflow

- GitHub Actions → **Release Candidate** → **Run workflow**
- Оберіть гілку `main`
- **Version**: залиште порожнім для автоматичного наступного patch (найпоширеніший випадок), або введіть версію вручну для minor/major
- Дочекайтеся завершення: Validate → Resolve → Build → Test → Publish → Prepare flat release artifact → SHA/manifest/notes → Tag

#### Автоматична версія (порожнє поле)

Якщо останній тег — `v0.1.0`, автоматично буде `0.1.1`.
Якщо тегів немає — автоматично `0.1.0`.

#### Ручна версія

Для minor/major змін введіть версію вручну (наприклад, `0.2.0`).
Версія повинна бути більшою за найновший існуючий тег.

### 3. Завантаження artifact

- Завантажте artifact `BDO-UA-Client-vX.Y.Z-win-x64` з workflow run
- GitHub-generated downloaded ZIP містить рівно flat-файли: `BDO-UA-Client.exe`, `release-manifest.json`, `SHA256SUMS.txt`, `RELEASE_NOTES-vX.Y.Z.md`
- Розпакуйте локальну копію artifact ZIP і протестуйте `BDO-UA-Client.exe`
- Перевірте SHA-256 за бажанням

### 4. Підготовка реліз-нотаток

- Відкрийте згенерований `RELEASE_NOTES-vX.Y.Z.md` з artifact
- Відредагуйте секції змін (Що нового / Виправлено / Зміни)

### 5. Публікація релізу

- GitHub → **Releases** → **Draft a new release**
- Оберіть існуючий тег `vX.Y.Z`
- Вставте відредаговані реліз-нотатки у тіло релізу
- Завантажте exact downloaded GitHub artifact ZIP як **єдиний application asset**.
- Не unpack/repackage artifact ZIP перед публікацією.
- Не завантажуйте окремо `BDO-UA-Client.exe`, `release-manifest.json` або `SHA256SUMS.txt`.
- Schema-2 manifest є внутрішнім файлом bundle; GitHub asset digest захищає зовнішній ZIP.
- Для першого публічного прев'ю: позначте як **Pre-release**
- Натисніть **Publish release**

## Workflow НЕ робить

- НЕ створює GitHub Release
- НЕ публікує реліз автоматично
- НЕ переміщує існуючі теги

## Політика невдалих кандидатів

Якщо artifact не пройшов manual E2E — НЕ публікуйте його як GitHub Release. Виправте код та використайте наступну версію/тег. Теги є незмінними ідентифікаторами реліз-кандидатів.

## Файли

| Файл | Опис |
|------|------|
| `RELEASE_TEMPLATE.md` | Шаблон реліз-нотаток |
| `vX.Y.Z.md` | Архів реліз-нотаток для конкретної версії |

## Архів реліз-нотаток

Текст кожного релізу зберігається у `docs/releases/vX.Y.Z.md` (де `X.Y.Z` — версія).
Файл створюється на основі `RELEASE_TEMPLATE.md` та заповнюється конкретними даними релізу.
Поле `{{SHA256}}` заповнюється після завершення Release Candidate workflow.
