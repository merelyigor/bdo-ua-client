# Процедура релізу

Production GitHub Releases завжди публікуються вручну власником репозиторію.

## Процес

### 1. Підготовка

- Переконайтеся, що `main` містить бажаний стан релізу
- Перевірте, що normal CI зелений

### 2. Release Candidate workflow

- GitHub Actions → **Release Candidate** → **Run workflow**
- Оберіть гілку `main`
- Введіть версію (наприклад, `0.1.0`)
- Дочекайтеся завершення: Validate → Restore → Build → Test → Publish → Package → Tag

### 3. Завантаження artifact

- Завантажте artifact `BDO-UA-Client-vX.Y.Z-win-x64` з workflow run
- Локально протестуйте ZIP/EXE

### 4. Підготовка реліз-нотаток

- Відкрийте згенерований `RELEASE_NOTES-vX.Y.Z.md` з artifact
- Відредагуйте секції змін (Що нового / Виправлено / Зміни)
- Скопіюйте відредагований Markdown у тіло GitHub Release

### 5. Публікація релізу

- GitHub → **Releases** → **Draft a new release**
- Оберіть існуючий тег `vX.Y.Z`
- Вставте відредаговані реліз-нотатки
- Завантажте точно той ZIP, який створив workflow (`BDO-UA-Client-vX.Y.Z-win-x64.zip`)
- Опціонально: завантажте `SHA256SUMS.txt`
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
