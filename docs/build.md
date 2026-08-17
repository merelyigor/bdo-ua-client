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

### .github/workflows/release-build.yml

**Trigger:** `workflow_dispatch` (ручний запуск).

**Етапи:**
1. Checkout
2. Setup .NET 8
3. Restore: `dotnet restore BdoUaClient.sln`
4. Build Release: `dotnet build BdoUaClient.sln -c Release --no-restore`
5. Test Release: `dotnet test BdoUaClient.sln -c Release --no-build`
6. Publish single-file: `dotnet publish BdoClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:AssemblyName=BDO-UA-Client -o artifacts/publish/win-x64`
7. Upload artifact: тільки `BDO-UA-Client.exe`

Restore + Build + Test обов'язкові перед publish.

## Actions artifact

Артефакт: **`BDO-UA-Client-win-x64.zip`** містить один EXE файл.

Це **артефакт GitHub Actions**, а не GitHub Release. Артефакт доступний для завантаження зі сторінки workflow run.

## GitHub Release

GitHub Release — заплановано на **v12.6**, наразі не реалізовано.

Планується:
- Автоматичне створення release при тегу
- Прикріплення EXE як release asset
- Release notes з changelog
