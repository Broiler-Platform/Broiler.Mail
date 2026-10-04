<#
.SYNOPSIS
Runs the UI-12 measurement workloads on fixed demo fixtures and summarizes them.

.DESCRIPTION
Publishes the Windows app as NativeAOT (the shipped configuration) unless -Executable is given,
runs every workload -Repeat times through `--demo <scenario> --measure <workload> --report <file>`,
and writes the JSON reports plus summary.md and summary.json into -Output. Each run opens and closes
a demo window; leave the machine otherwise idle while it runs. Frame build covers the UI side of a
frame (layout and render-list construction). -Detail adds per-phase timers and the host's render and
present call (CPU time on the UI thread, not GPU execution); they cost a little per frame, so the
summary keeps detailed runs in rows of their own.

-Scales runs each workload -Repeat times at each simulated render scale (--scale). Mail then renders
at that scale on this display, but Windows' own DPI, the window frame, and monitor changes are not
exercised: these are simulated-scale results, not real DPI evidence.

The summary compares each workload with the proposed budgets in -Budgets. Budgets are report-only:
only with -Strict does a result over budget, or a budget without data, make the script exit with 1.
-Evaluate summarizes the reports already in a directory without running the app; it writes
summary-evaluated.md and summary-evaluated.json into -Output, or into that directory, and leaves the
summary of the measurement itself as it was.
#>
param(
    [string]$Executable,
    # Where the reports and the summary go; with -Evaluate, where the evaluated summary goes (by default the evaluated directory).
    [string]$Output,
    [ValidateSet('light', 'dark')]
    [string]$Theme = 'light',
    [string]$Size = '1100x720',
    [ValidateRange(1, 20)]
    [int]$Repeat = 3,
    # Workloads to run or summarize; all of them by default.
    [string[]]$Workloads,
    # Simulated render scales in percent, 100 to 300 (for example 100,150,200); none uses the system's own.
    [string[]]$Scales,
    [switch]$Detail,
    [string]$Budgets,
    [string]$Evaluate,
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$invariant = [Globalization.CultureInfo]::InvariantCulture
if (!$Budgets) { $Budgets = Join-Path $PSScriptRoot 'ui-budgets.json' }

# Each workload runs on the fixture it needs. The first seven are the 2 October baseline's.
$fixtures = [ordered]@{
    idle = 'inbox'; scroll = 'large-inbox'; select = 'inbox'; type = 'large-draft'
    theme = 'inbox'; resize = 'inbox'; splitter = 'inbox'; 'long-html' = 'long-html'; 'preview-zoom' = 'long-html'
}
$order = @($fixtures.Keys)
$baseline = @($order | Select-Object -First 7)

# Lists arrive as one comma-separated string through powershell -File.
$selected = @($Workloads | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | ForEach-Object { $_.Trim() })
foreach ($name in $selected) { if ($order -notcontains $name) { throw "Unknown workload '$name'. Known: $($order -join ', ')." } }
if ($selected.Count -eq 0) { $selected = $order }
$scaleList = @($Scales | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | ForEach-Object {
    $percent = 0
    if (![int]::TryParse($_.Trim(), [ref]$percent) -or $percent -lt 100 -or $percent -gt 300) { throw "Scale '$_' is not a percentage from 100 to 300." }
    $percent
})

if ($Evaluate) {
    if (!(Test-Path -LiteralPath $Evaluate -PathType Container)) { throw "No directory $Evaluate." }
    # The stored reports, without any summary written beside them.
    $reportFiles = @(Get-ChildItem -LiteralPath $Evaluate -Filter '*.json' | Where-Object { $_.Name -notlike 'summary*.json' })
    if (!$Output) { $Output = $Evaluate }
    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    $summaryName = 'summary-evaluated'
} else {
    if (!$Output) { $Output = Join-Path $repository ("artifacts/measurements/" + (Get-Date -Format 'yyyy-MM-dd-HHmm')) }
    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    $summaryName = 'summary'

    if (!$Executable) {
        # NativeAOT publishing locates the C++ toolchain through vswhere.
        $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH = "$installer;$env:PATH" }
        $publish = Join-Path $repository 'artifacts/measure-app'
        dotnet publish (Join-Path $repository 'src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj') -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true -o $publish | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Publishing failed (exit $LASTEXITCODE)." }
        $Executable = Join-Path $publish 'Broiler.Mail.Windows.exe'
    }

    # Only this run's reports are summarized, even in a directory that holds earlier ones.
    $written = [System.Collections.Generic.List[string]]::new()
    # 0 stands for the system's own scale (no --scale).
    $runScales = if ($scaleList.Count -gt 0) { $scaleList } else { @(0) }
    foreach ($scale in $runScales) {
        $label = if ($scale -gt 0) { "s$scale" } else { 'system' }
        foreach ($workload in $selected) {
            for ($run = 1; $run -le $Repeat; $run++) {
                $report = Join-Path $Output "$workload-$label-$run.json"
                Write-Host "$workload ($label) run $run of $Repeat"
                $arguments = @('--demo', $fixtures[$workload], '--theme', $Theme, '--size', $Size, '--measure', $workload, '--report', "`"$report`"")
                if ($scale -gt 0) { $arguments += @('--scale', "$scale") }
                if ($Detail) { $arguments += '--detail' }
                if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
                $process = Start-Process -FilePath $Executable -PassThru -Wait -WindowStyle Normal -ArgumentList $arguments
                if ($process.ExitCode -ne 0 -or !(Test-Path $report)) { throw "$workload ($label) run $run failed (exit $($process.ExitCode))." }
                $written.Add($report)
            }
        }
    }
    $reportFiles = @($written | ForEach-Object { Get-Item -LiteralPath $_ })
}

# The middle value, or the mean of the two middle values for an even count.
function Median([object[]]$values) {
    $numbers = @($values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($numbers.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($numbers.Count / 2)
    if ($numbers.Count % 2 -eq 0) { return ($numbers[$middle - 1] + $numbers[$middle]) / 2 }
    return $numbers[$middle]
}

function Show($value) {
    if ($null -eq $value) { return '-' }
    return [Math]::Round([double]$value, 2).ToString($invariant)
}

# A simulated scale is named as such; reports without one (including version 1) ran at the system's scale.
function ScaleLabel($report) {
    if ($null -ne $report.simulatedScalePercent) { return "simulated $($report.simulatedScalePercent) %" }
    return "system $([Math]::Round([double]$report.dpiScale * 100)) %"
}

# Detailed runs time more per frame, so they are not summarized with plain ones.
function RunLabel($report) {
    if ($report.detail -eq $true) { return "$(ScaleLabel $report), detail" }
    return ScaleLabel $report
}

function Value($row, [string]$field) { return Median @($row.Reports | ForEach-Object { $_.$field }) }

$reports = @($reportFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } |
    Where-Object { $_.workload -and $selected -contains $_.workload })
if ($reports.Count -eq 0) { throw "No measurement reports for the selected workloads in $(if ($Evaluate) { $Evaluate } else { $Output })." }

$rows = @($reports | Group-Object { "$($_.workload)|$(RunLabel $_)" } | ForEach-Object {
    $set = @($_.Group)
    [pscustomobject]@{ Workload = $set[0].workload; Scale = (ScaleLabel $set[0]); Detail = ($set[0].detail -eq $true); Label = (RunLabel $set[0]); Reports = $set }
} | Sort-Object { [Array]::IndexOf($order, $_.Workload) }, Scale, Detail)

$budgetSet = Get-Content -LiteralPath $Budgets -Raw | ConvertFrom-Json
$verdicts = @(foreach ($row in $rows) {
    foreach ($budget in $budgetSet.budgets) {
        if (@($budget.workloads) -notcontains $row.Workload) { continue }
        $value = Value $row $budget.field
        $result = if ($null -eq $value) { 'no data' } elseif ($value -le [double]$budget.max) { 'pass' } else { 'over' }
        [pscustomobject]@{
            workload = $row.Workload; scale = $row.Scale; detail = $row.Detail; budget = $budget.name; field = $budget.field
            max = [double]$budget.max; unit = $budget.unit; value = $value; result = $result
        }
    }
})
$over = @($verdicts | Where-Object { $_.result -eq 'over' }).Count
# A budgeted value a run did not produce (it drew nothing, or the report lacks the field) cannot pass a strict run.
$missing = @($verdicts | Where-Object { $_.result -eq 'no data' }).Count

$first = $reports[0]
$hasDetail = @($rows | Where-Object { $_.Detail }).Count -gt 0
$hasPreview = @($reports | Where-Object { $null -ne $_.previewWindowSize }).Count -gt 0
$simulated = @($rows | Where-Object { $_.Scale -like 'simulated*' }).Count -gt 0
$lines = [System.Collections.Generic.List[string]]::new()
if ($Evaluate) {
    # Evaluated later: the date is the evaluation's, not the measurement's.
    $times = @($reportFiles | ForEach-Object { $_.LastWriteTime } | Sort-Object)
    $lines.Add("# UI measurements in $((Get-Item -LiteralPath $Evaluate).Name), evaluated $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
    $lines.Add('')
    $lines.Add("- Reports: $($reportFiles.Count) in $Evaluate, written $($times[0].ToString('yyyy-MM-dd HH:mm', $invariant)) to $($times[-1].ToString('yyyy-MM-dd HH:mm', $invariant))")
} else {
    $lines.Add("# UI measurements, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
    $lines.Add('')
}
$lines.Add("- Build: $($first.build)")
$lines.Add("- Machine: $($first.machine)")
$lines.Add("- Window: $($first.windowSize) DIPs, $($first.theme) theme; median of $(($rows | ForEach-Object { $_.Reports.Count } | Measure-Object -Maximum).Maximum) runs per row")
$lines.Add('- Frame build = layout and render-list construction on the UI thread; GPU rendering and presentation are not included.')
if ($simulated) {
    $lines.Add('- Simulated scale (--scale): Mail renders at that scale on this display. Windows'' DPI, the window frame, and monitor changes are not exercised; these are not real DPI results.')
}
$lines.Add('')
$lines.Add('| Workload | Scale | Steps | Unpainted | Frames | Build p50 ms | Build p95 ms | Build p99 ms | Input-to-frame p95 ms | Alloc KB/frame p50 | Working set MB |')
$lines.Add('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($row in $rows) {
    $lines.Add("| $($row.Workload) | $($row.Label) | $(Show (Value $row 'steps')) | $(Show (Value $row 'unpaintedSteps')) | $(Show (Value $row 'frames')) | $(Show (Value $row 'buildMsP50')) | $(Show (Value $row 'buildMsP95')) | $(Show (Value $row 'buildMsP99')) | $(Show (Value $row 'inputToFrameMsP95')) | $(Show (Value $row 'allocatedKbPerFrameP50')) | $(Show (Value $row 'workingSetMb')) |")
}

if ($hasDetail) {
    $lines.Add('')
    $lines.Add('Frame phases (--detail), p50 / p95 ms. Dispatch is per input; drain, measure, arrange, and render list add up to the frame build. Render+present is the host''s Direct2D drawing and vsynced Present on the UI thread (CPU time, which can include waiting for a buffer), not GPU execution.')
    $lines.Add('')
    $lines.Add('| Workload | Scale | Dispatch | Drain | Measure | Arrange | Render list | Render+present | Input-to-present p95 |')
    $lines.Add('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($row in ($rows | Where-Object { $_.Detail })) {
        $pair = { param($name) "$(Show (Value $row ($name + 'P50'))) / $(Show (Value $row ($name + 'P95')))" }
        $lines.Add("| $($row.Workload) | $($row.Label) | $(& $pair 'dispatchMs') | $(& $pair 'drainMs') | $(& $pair 'measureMs') | $(& $pair 'arrangeMs') | $(& $pair 'renderListMs') | $(& $pair 'renderPresentMs') | $(Show (Value $row 'inputToPresentMsP95')) |")
    }
}

if ($hasPreview) {
    $lines.Add('')
    $lines.Add('HTML preview. Frames are the preview window''s after it opened; tile counts and times include the opening. Raster = drawing a tile on the preview thread (single-threaded), upload = handing its pixels to the renderer. Re-rasters after eviction count tiles drawn again because the cache had evicted them; tiles drawn anew after a zoom, width, or scale change are counted under misses, next to the discards.')
    $lines.Add('')
    $lines.Add('| Workload | Scale | Preview | Open to first frame ms | Frames (drawing tiles) | Build p95 cached / drawing ms | Tile hits | Misses | Re-rasters after eviction | Evictions | Discards | Raster ms p50 / p95 | Upload ms p95 | Peak cache MB | Layouts (ms) |')
    $lines.Add('| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($row in ($rows | Where-Object { $null -ne $_.Reports[0].previewWindowSize })) {
        $preview = "$($row.Reports[0].previewWindowSize) DIPs, scale $(Show $row.Reports[0].previewDpiScale), opened at zoom $(Show $row.Reports[0].previewOpeningZoom)"
        $lines.Add("| $($row.Workload) | $($row.Label) | $preview | $(Show (Value $row 'openToFirstFrameMs')) | $(Show (Value $row 'frames')) ($(Show (Value $row 'framesDrawingTiles'))) | $(Show (Value $row 'buildMsCachedTilesP95')) / $(Show (Value $row 'buildMsDrawingTilesP95')) | $(Show (Value $row 'tileHits')) | $(Show (Value $row 'tileMisses')) | $(Show (Value $row 'tileRerasters')) | $(Show (Value $row 'tileEvictions')) | $(Show (Value $row 'tileDiscards')) | $(Show (Value $row 'tileRasterMsP50')) / $(Show (Value $row 'tileRasterMsP95')) | $(Show (Value $row 'tileUploadMsP95')) | $(Show (Value $row 'tilePeakCachedMb')) | $(Show (Value $row 'htmlLayouts')) ($(Show (Value $row 'htmlLayoutMsTotal'))) |")
    }
}

$lines.Add('')
$strictNote = if ($Strict) { 'With -Strict, a result over budget or without data fails the run.' } else { 'Report-only: nothing fails; -Strict would fail a result over budget or without data.' }
$lines.Add("Budgets ($($budgetSet.status); $($budgetSet.source)). Not tied to a reference machine. $strictNote")
if ($verdicts.Count -gt 0) {
    $lines.Add('')
    $lines.Add('| Workload | Scale | Budget | Limit | Median | Result |')
    $lines.Add('| --- | --- | --- | ---: | ---: | --- |')
    foreach ($verdict in $verdicts) {
        $lines.Add("| $($verdict.workload) | $(if ($verdict.detail) { "$($verdict.scale), detail" } else { $verdict.scale }) | $($verdict.budget) | $(Show $verdict.max) $($verdict.unit) | $(Show $verdict.value) | $($verdict.result) |")
    }
}
$notes = @($rows | ForEach-Object { $_.Workload } | Select-Object -Unique | Where-Object { $budgetSet.notes.$_ } |
    ForEach-Object { "- ${_}: $($budgetSet.notes.$_)" })
if ($notes.Count -gt 0) {
    $lines.Add('')
    foreach ($note in $notes) { $lines.Add($note) }
}
$lines.Add('')
$lines.Add("$over result(s) over budget, $missing without data.")

# The 2 October startup figure is the median over the baseline workloads' runs; other runs are reported apart.
$lines.Add('')
$startupGroups = @($reports | Group-Object { "$(if ($baseline -contains $_.workload) { 0 } else { 1 })|$(RunLabel $_)" } | Sort-Object Name)
foreach ($group in $startupGroups) {
    $set = @($group.Group)
    $kind = if ($baseline -contains $set[0].workload) { 'baseline workloads' } else { 'HTML preview workloads (long-html fixture)' }
    $runs = if ($set.Count -eq 1) { '1 run' } else { "$($set.Count) runs" }
    $lines.Add("Startup, $kind at $(RunLabel $set[0]), ${runs}: first frame median $(Show (Median $set.startupFirstFrameMs)) ms, prepared fixture interactive median $(Show (Median $set.startupInteractiveMs)) ms.")
}
$summary = Join-Path $Output "$summaryName.md"
Set-Content -LiteralPath $summary -Value $lines -Encoding utf8

$fields = @('steps', 'unpaintedSteps', 'frames', 'buildMsP50', 'buildMsP95', 'buildMsP99', 'inputToFrameMsP95', 'allocatedKbPerFrameP50', 'workingSetMb',
    'dispatchMsP95', 'measureMsP95', 'arrangeMsP95', 'renderListMsP95', 'renderPresentMsP95', 'inputToPresentMsP95',
    'openToFirstFrameMs', 'framesDrawingTiles', 'buildMsCachedTilesP95', 'buildMsDrawingTilesP95', 'tileHits', 'tileMisses', 'tileRerasters',
    'tileEvictions', 'tileDiscards', 'tileRasterMsP95', 'tileUploadMsP95', 'tilePeakCachedMb', 'htmlLayouts', 'htmlLayoutMsTotal')
$json = [ordered]@{
    budgets = [ordered]@{ status = $budgetSet.status; strict = [bool]$Strict; over = $over; noData = $missing }
    rows = @(foreach ($row in $rows) {
        $entry = [ordered]@{ workload = $row.Workload; scale = $row.Scale; detail = $row.Detail; runs = $row.Reports.Count }
        foreach ($field in $fields) { $value = Value $row $field; if ($null -ne $value) { $entry[$field] = $value } }
        $entry
    })
    verdicts = $verdicts
}
$json | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Output "$summaryName.json") -Encoding utf8
Get-Content -LiteralPath $summary
if ($Strict -and ($over + $missing) -gt 0) { exit 1 }
exit 0
