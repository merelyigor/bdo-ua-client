param(
    [Parameter(Mandatory = $false)] [string]$Path = ""
)

$ErrorActionPreference = "Stop"
$limit = 7500

if ([string]::IsNullOrWhiteSpace($Path)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Path = Join-Path $repoRoot "docs/ai-workflow/PROJECT_CHAT_RULES.md"
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Write-Error "Portable Architect rules file was not found: $Path"
    exit 1
}

$text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).Path)
$count = $text.Length

if ($count -gt $limit) {
    Write-Error "PROJECT_CHAT_RULES.md contains $count UTF-16 code units; the repository limit is $limit. Keep the portable file pasteable within the external 8000-character ChatGPT Project Instructions limit."
    exit 1
}

Write-Output "PROJECT_CHAT_RULES.md size check passed: $count UTF-16 code units (limit $limit; external limit 8000)."
