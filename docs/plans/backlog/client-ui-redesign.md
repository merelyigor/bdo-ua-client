# Client UI Redesign

Plan ID: `client-ui-redesign`
Status: **BACKLOG**
Backlog order: 1
Implementation authorization: **NO**
Next action: define and review detailed redesign requirements/roadmap before activation

---

## Goal

Future BDO UA Client UI redesign.

## Context

- A UI redesign is planned but not yet authorized for implementation.
- Detailed design/UX requirements will be developed separately.
- Must account for current application functionality and completed self-update work at the time it is activated.

## Scope

TBD — to be defined before activation.

## Contracts / decisions

- ReaLTaiizor NuGet package is available (v3.8.0.3)
- `ThemePrototype.cs` exists as visual reference (launch with `--prototype` flag)
- Design files: `docs/design/BDO_THEME_PLAN.md`, `docs/design/BDO_THEME_COLORS.md`

**Do NOT change:** business logic, event handlers, API, file operations, state management

## Roadmap

TBD — to be defined before activation.

## Acceptance criteria

TBD — to be defined before activation.

## Non-goals

- This plan must NOT interrupt current self-update implementation unless owner explicitly promotes/reprioritizes it.
- Do NOT read uncommitted owner ThemePrototype.cs/docs/design as canonical spec.

## Risks / dependencies

- Must account for completed self-update work when activated.
- Color palette reference: Background #121212/#1C1C1C/#2D2D2D, Accent #C8A415/#DCBA2B, Text #F0F0F0/#A0A0A0, Status #00B400/#FF4444.

## Manual visual validation loop

Цей цикл є обов'язковим для кожного meaningful visual implementation або visual correction stage плану.

### Automated validation

Спочатку виконати stage-specific build/tests. Якщо пізніший stage не визначає сильнішу перевірку, мінімальні команди такі:

```bash
dotnet build BdoUaClient.sln -c Release
dotnet test BdoUaClient.sln -c Release --no-build
```

### Local preview publish

Після успішної automated validation опублікувати exact поточний worktree через canonical single-file Windows publish configuration з `docs/build.md`:

```bash
dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:AssemblyName=BDO-UA-Client -o artifacts/local-preview/win-x64
```

У Windows/PowerShell цю саму команду дозволено виконувати одним рядком з ідентичними arguments.

Очікуваний preview executable:

```text
artifacts/local-preview/win-x64/BDO-UA-Client.exe
```

Preview повинен відповідати exact implementation state, який передається owner для review.

### Artifact policy

`artifacts/local-preview/` — тимчасовий local output. Він повинен залишатися untracked, ніколи не stage-итися або commit-итися, не потрапляти до persistent development journal як binary content і бути безпечно перезаписуваним на наступному UI stage. `artifacts/` уже ігнорується; `.gitignore` не змінювати.

### Separate approval gates

`automated validation passed` і `owner visual review accepted` — окремі gates. Успішні build, tests або publish не означають visual acceptance. Stage може бути технічно готовим до preview, але залишається pending visual acceptance, доки owner не перегляне результат. Якщо owner повідомляє про visual defect, це feedback поточного stage; спочатку внести найменшу corrective change і повторити цей цикл, а вже потім переходити до залежного visual stage.

### Owner review and completion report

Owner може надати screenshots з local preview для architectural/UI review. Це manual workflow; автоматичний screenshot capture, screenshot test framework, image dependency або external service у межах цього plan не додаються.

Після кожного visual implementation/correction stage звіт повинен містити:

- чи пройшов build;
- чи пройшли tests;
- чи пройшов local publish;
- exact preview EXE path;
- preview EXE file size;
- змінені visual areas;
- стани, які owner має перевірити вручну;
- стани, недоступні природним шляхом, і special reproduction steps для них.

Мінімальний формат:

```text
Local preview ready:
artifacts/local-preview/win-x64/BDO-UA-Client.exe

Manual checks:
- startup/loading state
- game detected/not detected
- localization mode cards
- installed/update-available state
- download progress
- error message
- application update notification
```

Не заявляти, що UI візуально accepted, доки owner фактично не виконав review.

### GitHub CI relationship

Local preview publish є fast iteration path для visual inspection. GitHub CI залишається independent repository validation, коли він потрібен, але routine redesign iteration не повинна чекати або завантажувати GitHub Actions artifact лише для локального UI review. Release candidate і public release workflows залишаються без змін.
