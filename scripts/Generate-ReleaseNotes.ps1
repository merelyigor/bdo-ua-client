param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [Parameter(Mandatory=$true)]
    [string]$Tag,
    [Parameter(Mandatory=$true)]
    [string]$Sha,
    [Parameter(Mandatory=$true)]
    [string]$AssetName,
    [Parameter(Mandatory=$true)]
    [string]$Sha256
)

$ErrorActionPreference = "Stop"

# Find latest existing tag (before this release)
$latestTag = git tag --sort=-version:refname | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Select-Object -First 1

if ($latestTag) {
    Write-Output "Previous tag: $latestTag"
    $commitRange = "$latestTag..HEAD"
} else {
    Write-Output "No previous tag found - including all commits"
    $commitRange = "HEAD"
}

# Get commits (skip merge commits, get first line only)
$commits = git log $commitRange --no-merges --pretty=format:"%s" --reverse 2>$null
if (-not $commits) {
    $commits = @("(no commits found)")
} else {
    $commits = @($commits)
}

Write-Output "Found $($commits.Count) commits since $latestTag"

# Ukrainian description mapping for common patterns
$descriptionMap = @{
    # Fix patterns
    "fix compiler warnings" = "виправлено попередження компілятора"
    "fix diagnostic correctness" = "виправлено коректність діагностики"
    "fix resolver test isolation" = "виправлено ізоляцію тестів резолвера"
    "fix live feed" = "виправлено живе оновлення стрічки"
    "fix retention boundary" = "виправлено межу зберігання логів"
    "fix release candidate" = "виправлено реліз-кандидат"
    "fix stale pending" = "виправлено застарілий pending feed"
    "fix formclosing" = "виправлено закриття форми"
    "fix lifecycle" = "виправлено життєвий цикл"
    "fix network" = "виправлено мережеву діагностику"
    "fix tls" = "виправлено TLS з'єднання"
    "fix startup" = "виправлено запуск"
    "fix mode selection" = "виправлено вибір режиму"
    "fix game detection" = "виправлено пошук гри"
    "fix installed marker" = "виправлено маркер встановлення"
    "fix hash" = "виправлено перевірку хешу"
    "fix download" = "виправлено завантаження"
    "fix backup" = "виправлено резервне копіювання"
    "fix path" = "виправлено обробку шляхів"
    "fix utf" = "виправлено кодування UTF-8"
    "fix byte count" = "виправлено підрахунок байтів"

    # Add patterns
    "add network diagnostics" = "додано діагностику мережі"
    "add log retention" = "додано автоматичне видалення старих логів"
    "add application icon" = "додано іконку програми"
    "add startup version" = "додано логування версії при запуску"
    "add release notes" = "додано автогенерацію реліз-нотаток"
    "add correlation headers" = "додано correlation headers для діагностики"
    "add api timing" = "додано діагностику часу відповіді API"
    "add download timing" = "додано діагностику часу завантаження"
    "add marquee progress" = "додано анімацію прогресу"
    "add test build" = "додано тестовий білд workflow"
    "add auto release notes" = "додано автогенерацію реліз-нотаток"
    "auto-generate release notes" = "додано автогенерацію реліз-нотаток з git log"
    "auto-triggers on push" = "автозапуск при push/PR"
    "auto-triggers" = "автозапуск"

    # Feature patterns
    "live refresh" = "оновлення списку режимів без перезапуску"
    "parallel startup" = "паралельний запуск API та пошук гри"
    "auto increment" = "автоматичне визначення наступної версії"
    "poller" = "фонове оновлення стрічки релізів"
    "coordinator" = "координатор оновлення стрічки"
    "semantic change detection" = "семантичне виявлення змін у стрічці"

    # Change patterns
    "rename release build to test build" = "перейменовано Release Build на Test Build"
    "unify feed coordinator" = "об'єднано координатор стрічки"
    "serialize feed application" = "серіалізація застосування стрічки"
    "preserve newer pending" = "збереження новішого pending feed при помилці"
    "close finalization races" = "усунення гонок фіналізації"
    "harden lifecycle" = "зміцнення життєвого циклу"
    "extract startup coordinator" = "виділення координатора запуску"
    "status readability" = "покращення читабельності стану"
    "release candidate workflow" = "workflow реліз-кандидата"
    "immutable release contract" = "незмінний контракт релізу"
    "log retention" = "автовидалення логів старше 15 днів"
    "network diagnostics" = "діагностика мережі"
    "correlation headers" = "кореляційні заголовки"
}

# Categorize commits
$newFeatures = @()
$fixes = @()
$changes = @()
$other = @()

foreach ($msg in $commits) {
    $lower = $msg.ToLower()

    # Skip internal version bump commits
    if ($lower -match '^(v\d+\.\d+\.\d+\.\d+|merge|bump|chore)') {
        continue
    }

    # Extract description after dash (handle various dash encodings)
    $description = $msg
    if ($msg -match '[\u2014\u2013\u2012\u2011]\s*(.+)$') {
        $description = $Matches[1].Trim()
    } elseif ($msg -match '---\s*(.+)$') {
        $description = $Matches[1].Trim()
    } elseif ($msg -match ' - \s*(.+)$') {
        $description = $Matches[1].Trim()
    } elseif ($msg -match '^v\d+\.\d+\.\d+\.\d+\s*[\u2014\u2013\u2012\u2011-]+\s*(.+)$') {
        $description = $Matches[1].Trim()
    }

    # Try to find Ukrainian translation
    $uaDescription = $null
    foreach ($pattern in $descriptionMap.Keys) {
        if ($description.ToLower().Contains($pattern)) {
            $uaDescription = $descriptionMap[$pattern]
            break
        }
    }

    # Use Ukrainian if found, otherwise keep original
    if ($uaDescription) {
        $finalDescription = $uaDescription
    } else {
        $finalDescription = $description
    }

    # Categorize by keywords (check both original and mapped description)
    $checkLower = "$lower $finalDescription".ToLower()

    if ($checkLower -match '(fix|виправлен|bug|patch|correct|repair|corrupt|violation)') {
        $fixes += $finalDescription
    }
    elseif ($checkLower -match '(add|додан|new|feature|implement|create|автогенерац|автозапуск|icon|live refresh)') {
        $newFeatures += $finalDescription
    }
    elseif ($checkLower -match '(refactor|онов|update|change|змін|improve|покращ|clean|remove|delete|rename|переймен|test|doc|harden|extract|unify|serialize|зміцнен|виділен|серіаліз|координатор|poller|live|poll|coordinator|header)') {
        $changes += $finalDescription
    }
    else {
        $other += $finalDescription
    }
}

# Build sections
$sections = @()

if ($newFeatures.Count -gt 0) {
    $items = ($newFeatures | ForEach-Object { "- $_" }) -join "`n"
    $sections += "## New Features`n`n$items"
}

if ($fixes.Count -gt 0) {
    $items = ($fixes | ForEach-Object { "- $_" }) -join "`n"
    $sections += "## Fixed`n`n$items"
}

if ($changes.Count -gt 0) {
    $items = ($changes | ForEach-Object { "- $_" }) -join "`n"
    $sections += "## Changes`n`n$items"
}

if ($other.Count -gt 0) {
    $items = ($other | ForEach-Object { "- $_" }) -join "`n"
    $sections += "## Other`n`n$items"
}

$body = $sections -join "`n`n"

if ($sections.Count -eq 0) {
    $allItems = ($commits | ForEach-Object { "- $_" }) -join "`n"
    $body = "## Changes`n`n$allItems"
}

# Generate final notes
$notes = @"
# BDO UA Client $Version

Version: $Version
Tag: $Tag
Commit: $Sha

$body

## Download

``$AssetName``

SHA-256:

``$Sha256``

## How to install

1. Download ``$AssetName`` from this page
2. Run ``BDO-UA-Client.exe``
3. If Windows SmartScreen shows warning - click "More info" -> "Run" (details: [README](https://github.com/merelyigor/bdo-ua-client#windows-smartscreen))

## Links

- [bdo-ua.com.ua](https://bdo-ua.com.ua/)
- [Repository](https://github.com/merelyigor/bdo-ua-client)
- [Instructions](https://github.com/merelyigor/bdo-ua-client#readme)
- [Technical docs](https://github.com/merelyigor/bdo-ua-client/blob/main/docs/index.md)
"@

return $notes
