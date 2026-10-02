<#
.SYNOPSIS
Runs the UI-12 measurement workloads on fixed demo fixtures and summarizes them.

.DESCRIPTION
Publishes the Windows app as NativeAOT (the shipped configuration) unless -Executable is given,
runs every workload -Repeat times through `--demo <scenario> --measure <workload> --report <file>`,
and writes the JSON reports plus summary.md into -Output. Each run opens and closes a demo window;
leave the machine otherwise idle while it runs. Numbers cover the UI side of a frame (layout and
render-list construction), not GPU rendering or presentation.
#>
param(
    [string]$Executable,
    [string]$Output,
    [ValidateSet('light', 'dark')]
    [string]$Theme = 'light',
    [string]$Size = '1100x720',
    [ValidateRange(1, 20)]
    [int]$Repeat = 3
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (!$Output) { $Output = Join-Path $repository ("artifacts/measurements/" + (Get-Date -Format 'yyyy-MM-dd-HHmm')) }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

if (!$Executable) {
    # NativeAOT publishing locates the C++ toolchain through vswhere.
    $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
    if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH = "$installer;$env:PATH" }
    $publish = Join-Path $repository 'artifacts/measure-app'
    dotnet publish (Join-Path $repository 'src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj') -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true -o $publish | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Publishing failed (exit $LASTEXITCODE)." }
    $Executable = Join-Path $publish 'Broiler.Mail.Windows.exe'
}

# Each workload runs on the fixture it needs.
$workloads = [ordered]@{
    idle = 'inbox'; scroll = 'large-inbox'; select = 'inbox'; type = 'large-draft'
    theme = 'inbox'; resize = 'inbox'; splitter = 'inbox'
}
$results = @{}
foreach ($workload in $workloads.Keys) {
    $results[$workload] = @()
    for ($run = 1; $run -le $Repeat; $run++) {
        $report = Join-Path $Output "$workload-$run.json"
        Write-Host "$workload run $run of $Repeat"
        $process = Start-Process -FilePath $Executable -PassThru -Wait -WindowStyle Normal -ArgumentList @(
            '--demo', $workloads[$workload], '--theme', $Theme, '--size', $Size, '--measure', $workload, '--report', "`"$report`"")
        if ($process.ExitCode -ne 0 -or !(Test-Path $report)) { throw "$workload run $run failed (exit $($process.ExitCode))." }
        $results[$workload] += Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    }
}

function Median([object[]]$values) {
    $numbers = @($values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($numbers.Count -eq 0) { return 'n/a' }
    $middle = $numbers[[int][Math]::Floor(($numbers.Count - 1) / 2)]
    return [Math]::Round([double]$middle, 2)
}

$first = $results['idle'][0]
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# UI measurements, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$lines.Add('')
$lines.Add("- Build: $($first.build)")
$lines.Add("- Machine: $($first.machine)")
$lines.Add("- Window: $($first.windowSize) at DPI scale $($first.dpiScale), $Theme theme; median of $Repeat runs per row")
$lines.Add('- Frame build = layout and render-list construction on the UI thread; GPU rendering and presentation are not included.')
$lines.Add('')
$lines.Add('| Workload | Steps | Frames | Build p50 ms | Build p95 ms | Build p99 ms | Input-to-frame p95 ms | Alloc KB/frame p50 | Working set MB |')
$lines.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($workload in $workloads.Keys) {
    $set = $results[$workload]
    $lines.Add("| $workload | $(Median $set.steps) | $(Median $set.frames) | $(Median $set.buildMsP50) | $(Median $set.buildMsP95) | $(Median $set.buildMsP99) | $(Median $set.inputToFrameMsP95) | $(Median $set.allocatedKbPerFrameP50) | $(Median $set.workingSetMb) |")
}
$startup = @($results.Values | ForEach-Object { $_ })
$lines.Add('')
$lines.Add("Startup (all runs): first frame median $(Median $startup.startupFirstFrameMs) ms, prepared fixture interactive median $(Median $startup.startupInteractiveMs) ms.")
$summary = Join-Path $Output 'summary.md'
Set-Content -LiteralPath $summary -Value $lines -Encoding utf8
Get-Content -LiteralPath $summary
