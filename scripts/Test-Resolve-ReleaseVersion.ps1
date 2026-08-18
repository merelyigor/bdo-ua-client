$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$passed = 0
$failed = 0

function Assert-Equals {
    param([string]$TestName, [string]$Actual, [string]$Expected)
    if ($Actual -eq $Expected) {
        Write-Output "  PASS: $TestName"
        return $true
    }
    Write-Output "  FAIL: $TestName -- expected '$Expected', got '$Actual'"
    return $false
}

function Assert-True {
    param([string]$TestName, [bool]$Value)
    if ($Value) {
        Write-Output "  PASS: $TestName"
        return $true
    }
    Write-Output "  FAIL: $TestName"
    return $false
}

Write-Output "=== Resolve-ReleaseVersion Logic Tests ==="
Write-Output ""

# Test regex patterns
$semverCore = '(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)'
$versionPattern = "^${semverCore}$"
$tagPattern = "^v${semverCore}$"

# Test 1: valid versions
Write-Output "Test 1: valid version patterns"
$r1 = Assert-True "0.1.0" ("0.1.0" -match $versionPattern)
$r2 = Assert-True "1.10.2" ("1.10.2" -match $versionPattern)
$r3 = Assert-True "0.0.1" ("0.0.1" -match $versionPattern)
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# Test 2: invalid versions
Write-Output "Test 2: invalid version patterns"
$r1 = Assert-True "v1.0.0 invalid" -not ("v1.0.0" -match $versionPattern)
$r2 = Assert-True "01.0.0 invalid" -not ("01.0.0" -match $versionPattern)
$r3 = Assert-True "1.0 invalid" -not ("1.0" -match $versionPattern)
$r4 = Assert-True "1.0.0-beta invalid" -not ("1.0.0-beta" -match $versionPattern)
$r5 = Assert-True "empty invalid" -not ("" -match $versionPattern)
if ($r1 -and $r2 -and $r3 -and $r4 -and $r5) { $passed++ } else { $failed++ }

# Test 3: valid tags
Write-Output "Test 3: valid tag patterns"
$r1 = Assert-True "v0.1.0" ("v0.1.0" -match $tagPattern)
$r2 = Assert-True "v1.10.2" ("v1.10.2" -match $tagPattern)
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# Test 4: invalid tags
Write-Output "Test 4: invalid tag patterns"
$r1 = Assert-True "test invalid" -not ("test" -match $tagPattern)
$r2 = Assert-True "vfoo invalid" -not ("vfoo" -match $tagPattern)
$r3 = Assert-True "release-1.0 invalid" -not ("release-1.0" -match $tagPattern)
$r4 = Assert-True "v1.0.0-beta.1 invalid" -not ("v1.0.0-beta.1" -match $tagPattern)
$r5 = Assert-True "v01.0.0 invalid" -not ("v01.0.0" -match $tagPattern)
if ($r1 -and $r2 -and $r3 -and $r4 -and $r5) { $passed++ } else { $failed++ }

# Test 5: numeric sorting
Write-Output "Test 5: numeric version sorting"
$versions = @("0.1.0", "0.9.9", "0.10.0", "1.0.0", "1.9.99", "1.10.0")
$sorted = $versions | ForEach-Object { [System.Version]::Parse($_) } | Sort-Object
$expected = @("0.1.0", "0.9.9", "0.10.0", "1.0.0", "1.9.99", "1.10.0")
$actual = $sorted | ForEach-Object { $_.ToString() }
$r1 = Assert-Equals "sorted order" ($actual -join ",") ($expected -join ",")
if ($r1) { $passed++ } else { $failed++ }

# Test 6: patch increment logic
Write-Output "Test 6: patch increment"
$latestParsed = [System.Version]::Parse("0.1.9")
$nextPatch = [System.Version]::new($latestParsed.Major, $latestParsed.Minor, $latestParsed.Build + 1)
$r1 = Assert-Equals "0.1.9+1" $nextPatch.ToString() "0.1.10"

$latestParsed2 = [System.Version]::Parse("0.9.9")
$nextPatch2 = [System.Version]::new($latestParsed2.Major, $latestParsed2.Minor, $latestParsed2.Build + 1)
$r2 = Assert-Equals "0.9.9+1" $nextPatch2.ToString() "0.9.10"

$latestParsed3 = [System.Version]::Parse("1.10.0")
$nextPatch3 = [System.Version]::new($latestParsed3.Major, $latestParsed3.Minor, $latestParsed3.Build + 1)
$r3 = Assert-Equals "1.10.0+1" $nextPatch3.ToString() "1.10.1"
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# Test 7: monotonic check - valid
Write-Output "Test 7: monotonic check valid"
$latest = [System.Version]::Parse("0.1.5")
$requested = [System.Version]::Parse("0.1.6")
$r1 = Assert-True "0.1.6 > 0.1.5" ($requested -gt $latest)
$requested2 = [System.Version]::Parse("0.2.0")
$r2 = Assert-True "0.2.0 > 0.1.5" ($requested2 -gt $latest)
$requested3 = [System.Version]::Parse("1.0.0")
$r3 = Assert-True "1.0.0 > 0.1.5" ($requested3 -gt $latest)
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# Test 8: monotonic check - invalid
Write-Output "Test 8: monotonic check invalid"
$latest = [System.Version]::Parse("0.1.5")
$requested = [System.Version]::Parse("0.1.5")
$r1 = Assert-True "0.1.5 <= 0.1.5" ($requested -le $latest)
$requested2 = [System.Version]::Parse("0.1.4")
$r2 = Assert-True "0.1.4 <= 0.1.5" ($requested2 -le $latest)
$requested3 = [System.Version]::Parse("0.0.9")
$r3 = Assert-True "0.0.9 <= 0.1.5" ($requested3 -le $latest)
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# Test 9: latest from multiple tags (numeric)
Write-Output "Test 9: latest from multiple tags"
$tags = @("v0.9.9", "v0.10.0")
$latestParsed = $tags | ForEach-Object {
    [System.Version]::Parse(($_ -replace '^v', ''))
} | Sort-Object | Select-Object -Last 1
$r1 = Assert-Equals "latest of v0.9.9,v0.10.0" $latestParsed.ToString() "0.10.0"

$tags2 = @("v1.9.99", "v1.10.0")
$latestParsed2 = $tags2 | ForEach-Object {
    [System.Version]::Parse(($_ -replace '^v', ''))
} | Sort-Object | Select-Object -Last 1
$r2 = Assert-Equals "latest of v1.9.99,v1.10.0" $latestParsed2.ToString() "1.10.0"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# Test 10: latest from single tag
Write-Output "Test 10: latest from single tag"
$tags = @("v0.1.0")
$latestParsed = $tags | ForEach-Object {
    [System.Version]::Parse(($_ -replace '^v', ''))
} | Sort-Object | Select-Object -Last 1
$r1 = Assert-Equals "latest of v0.1.0" $latestParsed.ToString() "0.1.0"
if ($r1) { $passed++ } else { $failed++ }

# Test 11: composition works (no double-anchor bug)
Write-Output "Test 11: tag pattern composition"
$composed = "^v${semverCore}$"
$r1 = Assert-True "v0.1.0 matches composed" ("v0.1.0" -match $composed)
$r2 = Assert-True "v1.10.25 matches composed" ("v1.10.25" -match $composed)
$r3 = Assert-True "test not matches composed" -not ("test" -match $composed)
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# Test 12: filter unrelated tags
Write-Output "Test 12: filter unrelated tags"
$allTags = @("test", "vfoo", "release-1.0", "v1.0.0-beta.1", "v01.0.0", "v0.1.0")
$validTags = $allTags | Where-Object { $_ -match $tagPattern }
$r1 = Assert-Equals "valid count" "$($validTags.Count)" "1"
$r2 = Assert-Equals "valid tag" $validTags[0] "v0.1.0"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

Write-Output ""
Write-Output "=== Results: $passed passed, $failed failed ==="
if ($failed -gt 0) {
    exit 1
}
