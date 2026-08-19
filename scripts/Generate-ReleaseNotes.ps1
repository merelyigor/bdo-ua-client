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

    # Extract description after dash
    $description = $msg
    if ($msg -match '\u2014\s*(.+)$') {
        $description = $Matches[1]
    } elseif ($msg -match '---\s*(.+)$') {
        $description = $Matches[1]
    }

    # Categorize by keywords
    if ($lower -match '(fix|bug|patch|correct|repair|corrupt)') {
        $fixes += $description
    }
    elseif ($lower -match '(add|new|feature|implement|create|live|poll|coordinator|header)') {
        $newFeatures += $description
    }
    elseif ($lower -match '(refactor|update|change|improve|clean|remove|delete|rename|test|doc)') {
        $changes += $description
    }
    else {
        $other += $description
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
2. Extract archive
3. Run ``BDO-UA-Client.exe``
4. If Windows SmartScreen shows warning - click "More info" -> "Run" (details: [README](https://github.com/merelyigor/bdo-ua-client#windows-smartscreen))

## Links

- [bdo-ua.com.ua](https://bdo-ua.com.ua/)
- [Repository](https://github.com/merelyigor/bdo-ua-client)
- [Instructions](https://github.com/merelyigor/bdo-ua-client#readme)
- [Technical docs](https://github.com/merelyigor/bdo-ua-client/blob/main/docs/index.md)
"@

return $notes