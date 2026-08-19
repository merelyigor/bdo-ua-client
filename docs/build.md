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

Результат: **`BDO-UA-Client.exe`** (~155 MB).

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

**Trigger:** `workflow_dispatch` (ручний запуск).

**Етапи:**
1. Checkout
2. Setup .NET 8
3. Restore: `dotnet restore BdoUaClient.sln`
4. Build Release: `dotnet build BdoUaClient.sln -c Release --no-restore`
5. Test Release: `dotnet test BdoUaClient.sln -c Release --no-build`
6. Publish single-file: `dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:AssemblyName=BDO-UA-Client -o artifacts/publish/win-x64`
7. Upload artifact: тільки `BDO-UA-Client.exe` (artifact: `BDO-UA-Client-test-build`)

Призначення: тестовий білд для перевірок. Не створює тег, не генерує реліз-нотатки.

## Actions artifact vs public package

**GitHub Actions artifact** (CI transport container): **`BDO-UA-Client-vX.Y.Z-release-assets.zip`**
- Містить: versioned user ZIP + SHA256SUMS.txt + release-manifest.json + RELEASE_NOTES
- Це внутрішній артефакт workflow, НЕ для кінцевого користувача

**Public user package** (what users download from GitHub Release): **`BDO-UA-Client-vX.Y.Z-win-x64.zip`**
- Містить рівно один файл: `BDO-UA-Client.exe`
- Це єдиний файл, який завантажує користувач

Не плутати: Actions artifact ZIP ≠ public user ZIP.

## Workflows

### A. CI (`ci.yml`)
- **Trigger:** push до main, PR до main
- Автоматична перевірка кожного коміту/PR на компільність та проходження тестів

### B. Test Build (`test-build.yml`)
- **Trigger:** push до main, PR до main, `workflow_dispatch`
- Автоматичний білд при кожному коміті/PR
- Artifact: `BDO-UA-Client-test-build` (тільки EXE)
- Artifact доступний всім (Actions → Test Build → завантажити)
- НЕ створює GitHub Release

### C. Release Candidate (`release-candidate.yml`)
- **Trigger:** `workflow_dispatch` (ручний запуск власником)
- Введення версії (наприклад, `0.1.0`)
- Збірка + тести + publish + versioned ZIP + SHA-256 + tag
- Actions artifact з ZIP, SHA256SUMS.txt, release-manifest.json, RELEASE_NOTES
- НЕ створює GitHub Release автоматично

### D. GitHub Release
- Створюється та публікується **вручну власником** репозиторію
- Після успішного Release Candidate: завантажити artifact → створити release → обрати тег → завантажити ZIP → Publish
