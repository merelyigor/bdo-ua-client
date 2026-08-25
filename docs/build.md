# Збірка та реліз

## Tech stack

| Компонент | Технологія |
|-----------|-----------|
| Мова | C# 12 |
| Runtime | .NET 8 |
| UI | WinForms (Windows Forms) |
| JSON | System.Text.Json |
| Тести | xUnit |

## Build команди

```bash
# Збірка рішення
dotnet build BdoUaClient.sln

# Збірка Release конфігурації
dotnet build BdoUaClient.sln -c Release
```

## Test команди

```bash
# Всі тести
dotnet test BdoUaClient.sln

# Без rebuild
dotnet test BdoUaClient.sln --no-build
```

## Release publish

```bash
dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:AssemblyName=BDO-UA-Client
```

### Параметри

| Параметр | Значення | Призначення |
|----------|----------|-------------|
| `-c Release` | Release config | Оптимізована збірка |
| `-r win-x64` | win-x64 | Цільова платформа |
| `--self-contained true` | self-contained | .NET runtime включений в EXE |
| `-p:PublishSingleFile=true` | single file | Один EXE файл |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | extract native | Native libs всередині EXE |
| `-p:PublishTrimmed=false` | no trimming | Без trimming |
| `-p:AssemblyName=BDO-UA-Client` | rename | Ім'я вихідного файлу |

## Single-file output

Результат: **`BDO-UA-Client.exe`** (self-contained, ~150+ MB).

- Один файл, без sibling DLL
- Не потребує встановлення .NET runtime на машині користувача
- Self-contained — все необхідне всередині EXE

## No trimming

Trimming **вимкнений** (`PublishTrimmed=false`).

Причина: WinForms використовує reflection для:
- Designer serialization
- Data binding
- Resource loading
- Control instantiation

Trimming може видалити типи, які використовуються через reflection, що призведе до runtime помилок.

## CI

### .github/workflows/ci.yml

**Trigger:** push до main, PR до main, workflow_dispatch.

**Етапи:**
1. Checkout
2. Setup .NET 8
3. Restore: `dotnet restore BdoUaClient.sln`
4. Build Release: `dotnet build BdoUaClient.sln --configuration Release --no-restore`
5. Test Release: `dotnet test BdoUaClient.sln --configuration Release --no-build`

Мета: швидка перевірка кожного коміту/PR на компільність та проходження тестів.

### .github/workflows/test-build.yml

**Trigger:** push до main, PR до main, `workflow_dispatch`.

**Етапи:**
1. Checkout
2. Setup .NET 8
3. Restore: `dotnet restore BdoUaClient.sln`
4. Build Release: `dotnet build BdoUaClient.sln -c Release --no-restore`
5. Test Release: `dotnet test BdoUaClient.sln -c Release --no-build`
6. Publish single-file: `dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:AssemblyName=BDO-UA-Client -o artifacts/publish/win-x64`
7. Upload artifact: тільки `BDO-UA-Client.exe` (artifact: `BDO-UA-Client-test-build`)

Призначення: тестовий білд для перевірок. Не створює тег, не генерує реліз-нотатки.

## Actions artifact vs public release asset

**GitHub Actions artifact** (CI transport wrapper created by GitHub): **`BDO-UA-Client-vX.Y.Z-win-x64`**
- Після завантаження GitHub створює ZIP-wrapper з flat-файлами: `BDO-UA-Client.exe`, `release-manifest.json`, `SHA256SUMS.txt`, `RELEASE_NOTES-vX.Y.Z.md`
- Цей exact GitHub-generated ZIP стає єдиним application asset у public GitHub Release

**Public release asset**: `BDO-UA-Client-vX.Y.Z-win-x64.zip`
- Standalone `BDO-UA-Client.exe`, manifest і sums не завантажуються окремо

Усередині public ZIP немає project-created nested ZIP. Internal schema-2 manifest перевіряє EXE SHA/version, а GitHub Release asset digest перевіряє зовнішній ZIP.

## Workflows

### A. CI (`ci.yml`)
- **Trigger:** push до main, PR до main
- Автоматична перевірка кожного коміту/PR на компільність та проходження тестів

### B. Test Build (`test-build.yml`)
- **Trigger:** push до main, PR до main, `workflow_dispatch`
- Автоматичний білд при кожному коміті/PR
- Версія: `0.0.0-dev.{short_sha}` (наприклад `0.0.0-dev.4264c1f`)
- Artifact: `BDO-UA-Client-test-build` (тільки EXE)
- Artifact доступний всім (Actions → Test Build → завантажити)
- НЕ створює GitHub Release

### C. Release Candidate (`release-candidate.yml`)
- **Trigger:** `workflow_dispatch` (ручний запуск власником), тільки з гілки **main**
- Кроки:
  1. Validate main branch (заборона запуску не з main)
  2. Resolve version через `scripts/Resolve-ReleaseVersion.ps1` — автоматичний next-patch з останнього тега, або manual version якщо вказано
  3. Check tag does not exist (локально та remote)
  4. Restore → Build → Test
  5. Publish single-file з версіюванням: `-p:Version / -p:AssemblyVersion / -p:FileVersion / -p:InformationalVersion` + `-p:IncludeSourceRevisionInInformationalVersion=false`
  6. Verify EXE version metadata — exact FileVersion/ProductVersion match
  7. Генерація schema-2 manifest (`release-manifest.json`) + `SHA256SUMS.txt`
  8. Release notes через `scripts/Generate-ReleaseNotes.ps1` (авто-генерація з git log)
  9. Upload версійованого artifact `BDO-UA-Client-vX.Y.Z-win-x64`
- НЕ створює GitHub Release автоматично

### Scripts

| Скрипт | Призначення |
|---|---|
| `scripts/Resolve-ReleaseVersion.ps1` | Визначає версію RC: auto next-patch або manual; перевіряє унікальність тега |
| `scripts/Generate-ReleaseNotes.ps1` | Генерує release notes з git log |
| `scripts/Test-Resolve-ReleaseVersion.ps1` | Pester-подібні self-тести для Resolve-ReleaseVersion |

Детальна процедура релізу — [releases/README.md](releases/README.md).

### D. GitHub Release
- Створюється та публікується **вручну власником** репозиторію
- Після успішного Release Candidate: завантажити artifact ZIP → створити release → обрати тег → завантажити exact artifact ZIP як єдиний application asset → Publish
- Release notes використовуються як тіло GitHub Release.
