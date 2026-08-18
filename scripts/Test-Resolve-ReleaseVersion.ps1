$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$resolverPath = Join-Path $scriptDir "Resolve-ReleaseVersion.ps1"

$passed = 0
$failed = 0

function Invoke-Resolver {
    param([string]$ManualVersion, [string[]]$Tags)
    $env:RESOLVER_TEST_TAGS = ($Tags -join ',')
    $env:RESOLVER_TEST_ORIGIN = $null
    $exitCode = 0
    $output = try { & $resolverPath -ManualVersion $ManualVersion 2>&1 } catch { $_ }
    $exitCode = $LASTEXITCODE
    $env:RESOLVER_TEST_TAGS = $null
    return @{ Output = @($output); ExitCode = $exitCode }
}

function Get-FromOutput {
    param($Output, [string]$Key)
    $line = $Output | Where-Object { $_ -match "^${Key}=" }
    if ($line) { return ($line -split '=', 2)[1] }
    return $null
}

function Assert-Equals {
    param([string]$TestName, [string]$Actual, [string]$Expected)
    if ($Actual -eq $Expected) {
        Write-Output "  PASS: $TestName"
        return $true
    }
    Write-Output "  FAIL: $TestName -- expected '$Expected', got '$Actual'"
    return $false
}

function Assert-ExitCode {
    param([string]$TestName, [int]$Actual, [int]$Expected)
    if ($Actual -eq $Expected) {
        Write-Output "  PASS: $TestName (exit $Actual)"
        return $true
    }
    Write-Output "  FAIL: $TestName -- expected exit $Expected, got $Actual"
    return $false
}

Write-Output "=== Resolve-ReleaseVersion Integration Tests ==="
Write-Output ""

# A: no tags + blank -> 0.1.0
Write-Output "A: no tags + blank -> 0.1.0"
$r = Invoke-Resolver -ManualVersion "" -Tags @()
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$t = Get-FromOutput $r.Output "RELEASE_TAG"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.1.0"
$r3 = Assert-Equals "tag" $t "v0.1.0"
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# B: v0.1.0 + blank -> 0.1.1
Write-Output "B: v0.1.0 + blank -> 0.1.1"
$r = Invoke-Resolver -ManualVersion "" -Tags @("v0.1.0")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$t = Get-FromOutput $r.Output "RELEASE_TAG"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.1.1"
$r3 = Assert-Equals "tag" $t "v0.1.1"
if ($r1 -and $r2 -and $r3) { $passed++ } else { $failed++ }

# C: v0.1.9 + blank -> 0.1.10
Write-Output "C: v0.1.9 + blank -> 0.1.10"
$r = Invoke-Resolver -ManualVersion "" -Tags @("v0.1.9")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.1.10"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# D: v0.9.9 + v0.10.0 + blank -> 0.10.1
Write-Output "D: v0.9.9 + v0.10.0 + blank -> 0.10.1"
$r = Invoke-Resolver -ManualVersion "" -Tags @("v0.9.9", "v0.10.0")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.10.1"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# E: v1.9.99 + v1.10.0 + blank -> 1.10.1
Write-Output "E: v1.9.99 + v1.10.0 + blank -> 1.10.1"
$r = Invoke-Resolver -ManualVersion "" -Tags @("v1.9.99", "v1.10.0")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "1.10.1"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# F: latest v0.1.5 + manual 0.1.6 -> success
Write-Output "F: latest v0.1.5 + manual 0.1.6 -> success"
$r = Invoke-Resolver -ManualVersion "0.1.6" -Tags @("v0.1.5")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.1.6"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# G: latest v0.1.5 + manual 0.2.0 -> success
Write-Output "G: latest v0.1.5 + manual 0.2.0 -> success"
$r = Invoke-Resolver -ManualVersion "0.2.0" -Tags @("v0.1.5")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.2.0"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

# H: latest v0.1.5 + manual 0.1.5 -> failure
Write-Output "H: latest v0.1.5 + manual 0.1.5 -> failure"
$r = Invoke-Resolver -ManualVersion "0.1.5" -Tags @("v0.1.5")
$r1 = Assert-ExitCode "exit code" $r.ExitCode 1
if ($r1) { $passed++ } else { $failed++ }

# I: latest v0.1.5 + manual 0.1.4 -> failure
Write-Output "I: latest v0.1.5 + manual 0.1.4 -> failure"
$r = Invoke-Resolver -ManualVersion "0.1.4" -Tags @("v0.1.5")
$r1 = Assert-ExitCode "exit code" $r.ExitCode 1
if ($r1) { $passed++ } else { $failed++ }

# J: manual 01.0.0 -> failure
Write-Output "J: manual 01.0.0 -> failure"
$r = Invoke-Resolver -ManualVersion "01.0.0" -Tags @()
$r1 = Assert-ExitCode "exit code" $r.ExitCode 1
if ($r1) { $passed++ } else { $failed++ }

# K: manual v1.0.0 -> failure
Write-Output "K: manual v1.0.0 -> failure"
$r = Invoke-Resolver -ManualVersion "v1.0.0" -Tags @()
$r1 = Assert-ExitCode "exit code" $r.ExitCode 1
if ($r1) { $passed++ } else { $failed++ }

# L: unrelated tags + v0.1.0 + blank -> 0.1.1
Write-Output "L: unrelated tags + v0.1.0 + blank -> 0.1.1"
$r = Invoke-Resolver -ManualVersion "" -Tags @("test", "vfoo", "release-1.0", "v1.0.0-beta.1", "v01.0.0", "v0.1.0")
$v = Get-FromOutput $r.Output "RELEASE_VERSION"
$r1 = Assert-ExitCode "exit code" $r.ExitCode 0
$r2 = Assert-Equals "version" $v "0.1.1"
if ($r1 -and $r2) { $passed++ } else { $failed++ }

Write-Output ""
Write-Output "=== Results: $passed passed, $failed failed ==="
if ($failed -gt 0) {
    exit 1
}
