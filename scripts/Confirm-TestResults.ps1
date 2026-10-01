param(
    [string]$ResultsDirectory = 'artifacts/validation',
    [switch]$IncludeWindowsHost,
    [switch]$IncludeLinuxHost
)

$ErrorActionPreference = 'Stop'
$suites = @('shared')
if ($IncludeWindowsHost) { $suites += 'windows-host' }
if ($IncludeLinuxHost) { $suites += 'linux-host' }
$rows = @('| Suite | Total | Passed | Failed | Not executed |', '| --- | ---: | ---: | ---: | ---: |')
$invalid = $false
foreach ($suite in $suites) {
    $path = Join-Path $ResultsDirectory "$suite.trx"
    if (!(Test-Path -LiteralPath $path)) {
        $rows += "| $suite | Missing result | | | |"
        $invalid = $true
        continue
    }
    [xml]$result = Get-Content -LiteralPath $path -Raw
    $counts = $result.TestRun.ResultSummary.Counters
    $rows += "| $suite | $($counts.total) | $($counts.passed) | $($counts.failed) | $($counts.notExecuted) |"
    if (!$counts -or [int]$counts.total -le 0 -or [int]$counts.passed -ne [int]$counts.total -or
        $result.TestRun.ResultSummary.outcome -ne 'Completed') {
        $invalid = $true
    }
}
$summary = $rows -join "`n"
Write-Output $summary
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary }
if ($invalid) { throw 'Required test results are missing, failed, or skipped. See the TRX evidence.' }
