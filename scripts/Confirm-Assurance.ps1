param(
    # The report written by `hairs assurance check --json`.
    [string]$Report = 'artifacts/assurance/check.json',
    # Rules counted in the summary without failing the run. J1, a relevant unit with no annotation, is the
    # backlog still to be assessed. Every other rule means the committed record no longer matches the code:
    # a stale header, fingerprint or manifest, a malformed block, a review claim, a missing criterion.
    [string[]]$ReportedOnly = @('J1'),
    # At most this many gating violations become annotations; the summary and the report count all of them.
    [int]$AnnotationLimit = 50
)

$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $Report)) { throw "Assurance report '$Report' is missing: hairs assurance check did not run." }
$result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
if ($result.schema -ne 1) { throw "Unsupported assurance report schema '$($result.schema)'." }

function Format-Property([string]$value) {
    $value.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A').Replace(':', '%3A').Replace(',', '%2C')
}
function Format-Message([string]$value) {
    $value.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}

$gating = @($result.violations | Where-Object { $ReportedOnly -notcontains $_.rule })
$shown = 0
foreach ($violation in $gating) {
    if ($shown -ge $AnnotationLimit) { break }
    $properties = @()
    if ($violation.file) { $properties += "file=$(Format-Property $violation.file)" }
    if ($violation.line) { $properties += "line=$($violation.line)" }
    $message = "$($violation.rule) $($violation.message)"
    if ($violation.remedy) { $message += "`nRun: $($violation.remedy)" }
    Write-Output "::error $($properties -join ',')::$(Format-Message $message)"
    $shown++
}

$rows = @(
    "## Code assurance ($($result.component))",
    '',
    '| Rule | Violations | In CI |',
    '| --- | ---: | --- |'
)
$rules = @($result.rules.PSObject.Properties | Sort-Object Name)
foreach ($rule in $rules) {
    $gate = if ($ReportedOnly -contains $rule.Name) { 'reported' } else { 'fails the run' }
    $rows += "| $($rule.Name) | $($rule.Value) | $gate |"
}
if ($rules.Count -eq 0) { $rows += '| none | 0 | |' }
$rows += ''
if ($gating.Count -gt 0) {
    $rows += "$($gating.Count) violation(s) outside $($ReportedOnly -join ', ') fail this run."
    if ($gating.Count -gt $shown) { $rows += "$shown are annotated; the uploaded report lists all of them." }
    $rows += 'Changed code needs a regenerated record: run `dotnet tool restore`, then `dotnet tool run hairs -- assurance generate --root .`, and commit what it writes.'
}
else {
    $rows += "The record matches the code. $(@($result.violations).Count) unit(s) in $($ReportedOnly -join ', ') are counted, not failed."
}
$summary = $rows -join "`n"
Write-Output $summary
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary }
if ($gating.Count -gt 0) { throw "The assurance record does not match the code: $($gating.Count) violation(s) outside $($ReportedOnly -join ', ')." }
