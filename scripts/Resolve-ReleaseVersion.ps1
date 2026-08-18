param(
    [string]$ManualVersion = ""
)

$ErrorActionPreference = "Stop"

# SemVer core WITHOUT anchors — for composition
$semverCore = '(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)'
$versionPattern = "^${semverCore}$"
$tagPattern = "^v${semverCore}$"

# Get tags — test override via RESOLVER_TEST_TAGS_JSON (JSON array, presence check),
# production via git ls-remote
$testTagsJson = [Environment]::GetEnvironmentVariable("RESOLVER_TEST_TAGS_JSON", "Process")

if ($null -ne $testTagsJson) {
    # Test override present (even [] = empty tag set)
    try {
        $remoteTags = @($testTagsJson | ConvertFrom-Json)
    }
    catch {
        Write-Error "Invalid RESOLVER_TEST_TAGS_JSON: $($_.Exception.Message)"
        exit 1
    }
}
else {
    # Production: query real remote
    $remoteRef = if ($env:RESOLVER_TEST_ORIGIN) { $env:RESOLVER_TEST_ORIGIN } else { "origin" }
    $savedEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $remoteOutput = git ls-remote --tags --refs $remoteRef 2>&1
    $remoteExitCode = $LASTEXITCODE
    $ErrorActionPreference = $savedEap

    if ($remoteExitCode -ne 0) {
        $msg = ($remoteOutput | Out-String).Trim()
        Write-Error "Failed to query remote '$remoteRef' (exit code ${remoteExitCode}): $msg"
        exit 1
    }

    $remoteTags = $remoteOutput |
        ForEach-Object { ($_ -split '\s+')[1] } |
        Where-Object { $_ -match '^refs/tags/v\d+\.\d+\.\d+$' } |
        ForEach-Object { $_ -replace '^refs/tags/', '' }
}

# Filter to strict SemVer tags only
$validTags = @($remoteTags | Where-Object { $_ -match $tagPattern })

if ($ManualVersion -and $ManualVersion.Trim() -ne "") {
    # Manual version provided
    $version = $ManualVersion.Trim()

    if ($version -notmatch $versionPattern) {
        Write-Error "Invalid version format: '$version'. Expected MAJOR.MINOR.PATCH without leading zeroes."
        exit 1
    }

    # Parse requested version
    $requested = [System.Version]::Parse($version)

    # Check monotonic: must be > latest existing tag
    if ($validTags.Count -gt 0) {
        $latestTag = $validTags | ForEach-Object {
            [System.Version]::Parse(($_ -replace '^v', ''))
        } | Sort-Object | Select-Object -Last 1

        if ($requested -le $latestTag) {
            Write-Error "Version $version must be greater than latest existing tag v$latestTag"
            exit 1
        }
    }

    Write-Output "Manual release version selected: $version"
    Write-Output "RELEASE_VERSION=$version"
    Write-Output "RELEASE_TAG=v$version"
}
else {
    # Automatic patch increment
    if ($validTags.Count -gt 0) {
        # Find latest by numeric comparison
        $latestParsed = $validTags | ForEach-Object {
            [System.Version]::Parse(($_ -replace '^v', ''))
        } | Sort-Object | Select-Object -Last 1

        $nextPatch = [System.Version]::new(
            $latestParsed.Major,
            $latestParsed.Minor,
            $latestParsed.Build + 1
        )
        $version = $nextPatch.ToString()

        Write-Output "Version input empty."
        Write-Output "Latest release tag: v$latestParsed"
        Write-Output "Automatically resolved next patch version: $version"
    }
    else {
        $version = "0.1.0"
        Write-Output "Version input empty."
        Write-Output "No previous release tags found."
        Write-Output "Automatically resolved initial version: $version"
    }

    Write-Output "RELEASE_VERSION=$version"
    Write-Output "RELEASE_TAG=v$version"
}
