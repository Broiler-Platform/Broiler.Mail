param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string]$Runtime = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$hostRid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
if ($Runtime -ne $hostRid) { throw "M0 probes must execute on the selected architecture ($Runtime; current $hostRid)." }
$evidence = Join-Path $repository "artifacts/m0/$Runtime"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Cannot record probe SDK version.' }
$revision = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot record probe source revision.' }
$dirty = @(& git -C $repository status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot record probe working-tree status.' }
[ordered]@{ schemaVersion = 1; runtime = $Runtime; sdkVersion = [string]$sdk; sourceRevision = [string]$revision;
    workingTreeDirty = $dirty.Count -gt 0; recordedAtUtc = [DateTimeOffset]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'validation-manifest.json')
foreach ($name in @('StorageProbe', 'ProtocolProbe', 'NetSessionProbe')) {
    $project = Join-Path $repository "eng/Broiler.Mail.$name/Broiler.Mail.$name.csproj"
    $output = [IO.Path]::GetFullPath((Join-Path $evidence $name))
    $expectedRoot = [IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar
    if (-not $output.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid probe output directory.' }
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    & dotnet publish $project -c Release -r $Runtime -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o $output 2>&1 |
        Tee-Object -FilePath (Join-Path $evidence "$name-publish.log") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$name NativeAOT publish failed." }
    $assetsPath = Join-Path (Split-Path $project) 'obj/project.assets.json'
    & "$PSScriptRoot/Confirm-NativeDependencies.ps1" -AssetsPath $assetsPath -Runtime $Runtime
    $executable = Join-Path $output "Broiler.Mail.$name$(if ($IsWindows) { '.exe' })"
    if (-not (Test-Path -LiteralPath $executable)) { throw "Missing published probe: $executable" }
    if (Test-Path -LiteralPath (Join-Path $output "Broiler.Mail.$name.runtimeconfig.json")) { throw 'Probe must be NativeAOT.' }
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw "$name timed out." }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        Set-Content -LiteralPath (Join-Path $evidence "$name-stdout.txt") -Value $stdout
        Set-Content -LiteralPath (Join-Path $evidence "$name-stderr.txt") -Value $stderr
        if ($process.ExitCode -ne 0) { throw "$name failed: $stderr" }
        if ($name -in @('StorageProbe', 'NetSessionProbe')) {
            $null = $stdout | ConvertFrom-Json
            $resultName = if ($name -eq 'StorageProbe') { 'storage-results.json' } else { 'net-session-results.json' }
            Set-Content -LiteralPath (Join-Path $evidence $resultName) -Value $stdout
        }
        Write-Host "$name passed ($Runtime)."
    }
    finally { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }

    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packages = foreach ($entry in $assets.libraries.psobject.Properties) {
        if ($entry.Value.type -ne 'package') { continue }
        $spec = $null
        foreach ($folder in $assets.packageFolders.psobject.Properties.Name) {
            $packagePath = Join-Path $folder $entry.Value.path
            if (Test-Path -LiteralPath $packagePath) {
                $nuspec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' | Select-Object -First 1
                if ($nuspec) { [xml]$spec = Get-Content -LiteralPath $nuspec.FullName -Raw; break }
            }
        }
        if (-not $spec) { throw "Missing license metadata: $($entry.Name)" }
        [ordered]@{ package = $entry.Name; license = [string]$spec.package.metadata.license.InnerText; licenseUrl = [string]$spec.package.metadata.licenseUrl }
    }
    $files = Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Extension -notin @('.pdb', '.dbg') } | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($output, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    [ordered]@{ runtime = $Runtime; probe = $name; packages = @($packages); publishedFiles = @($files) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence "$name-inventory.json")
}
