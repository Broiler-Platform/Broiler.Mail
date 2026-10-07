param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # self-contained: one NativeAOT executable that needs no .NET runtime.
    # framework-dependent: the IL build with its assemblies, run on an installed .NET 10 runtime.
    [ValidateSet('self-contained', 'framework-dependent')]
    [string]$Variant = 'self-contained',

    # Stamped into the build and the package name. Defaults to BroilerMailVersion from
    # Directory.Build.props; the Publish workflow passes the preview it resolved.
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if (![System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows) -or
    $Runtime -ne "win-$hostArchitecture") {
    throw "Publishing includes executing the smoke test. Run $Runtime on a matching Windows .NET process (current: $hostArchitecture)."
}
$repository = Split-Path -Parent $PSScriptRoot
if ($Version) {
    $version = $Version
}
else {
    $versionNode = ([xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw)).SelectSingleNode('/Project/PropertyGroup/BroilerMailVersion')
    if (!$versionNode) { throw 'BroilerMailVersion is missing from Directory.Build.props.' }
    $version = $versionNode.InnerText.Trim()
}
if ($version -notmatch '^\d+\.\d+\.\d+(-preview\.[1-9]\d*)?$') { throw "Unexpected version '$version'." }
$selfContained = $Variant -eq 'self-contained'
$releaseName = "Broiler.Mail-$version-$Runtime-$Variant"
$output = Join-Path $repository "artifacts/$releaseName"
$archive = Join-Path $repository "artifacts/$releaseName.zip"

Push-Location $repository
try {
    # Publish into a fresh directory, so stale files cannot enter the archive.
    $artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repository 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedOutput = [System.IO.Path]::GetFullPath($output)
    if (!$resolvedOutput.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish directory must be inside the repository artifacts directory.'
    }
    if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
    if ($selfContained) {
        dotnet publish src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj -c Release -r $Runtime --self-contained true -p:PublishAot=true -p:PublishTrimmed=true "-p:BroilerMailVersion=$version" -o $output
    }
    else {
        dotnet publish src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj -c Release -r $Runtime --self-contained false -p:PublishAot=false "-p:BroilerMailVersion=$version" -o $output
    }
    if ($LASTEXITCODE -ne 0) { throw "Publishing failed (exit $LASTEXITCODE)." }
    $runtimeConfig = Join-Path $output 'Broiler.Mail.Windows.runtimeconfig.json'
    if ($selfContained -and (Test-Path -LiteralPath $runtimeConfig)) { throw 'The self-contained build is not NativeAOT: a runtimeconfig.json was emitted.' }
    if (!$selfContained) {
        # The apphost and the runtimeconfig that names the shared framework must be there, and
        # no runtime may have leaked in: that would be a self-contained build under the wrong name.
        if (!(Test-Path -LiteralPath $runtimeConfig)) { throw 'The framework-dependent build has no runtimeconfig.json.' }
        foreach ($file in @('coreclr.dll', 'System.Private.CoreLib.dll')) {
            if (Test-Path -LiteralPath (Join-Path $output $file)) { throw "$file is in a framework-dependent build." }
        }
    }
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $output 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $repository 'docs/version-2-acceptance.md') -Destination (Join-Path $output 'START-HERE.md')
    Copy-Item -LiteralPath (Join-Path $repository 'docs/html-renderer-security.md') -Destination (Join-Path $output 'html-renderer-security.md')

    # Include package identity/license metadata and license files from the actual restored graph.
    $assets = Get-Content -LiteralPath 'src/Broiler.Mail.Windows/obj/project.assets.json' -Raw | ConvertFrom-Json
    $notices = [System.Collections.Generic.List[string]]::new()
    if ($selfContained) {
        # NativeAOT emits no .deps.json. Resolve its runtime license from the restored SDK pack.
        $runtimePackage = "Microsoft.NETCore.App.Runtime.NativeAOT.$Runtime"
        $runtimeDownload = $assets.project.frameworks.psobject.Properties.Value.downloadDependencies |
            Where-Object { $_.name -eq $runtimePackage } | Select-Object -First 1
        if (!$runtimeDownload -or $runtimeDownload.version -notmatch '^\[([^,]+),\s*\1\]$') {
            throw 'The exact NativeAOT runtime version is missing from the restore graph.'
        }
        $runtimeVersion = $Matches[1]
        $runtimeDirectory = $null
        foreach ($root in ($assets.packageFolders.psobject.Properties | ForEach-Object { $_.Name })) {
            $candidate = Join-Path $root "$($runtimePackage.ToLowerInvariant())/$runtimeVersion"
            if (Test-Path -LiteralPath $candidate) { $runtimeDirectory = $candidate; break }
        }
        if (!$runtimeDirectory) { throw 'The runtime license files could not be located.' }
        foreach ($file in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            Copy-Item -LiteralPath (Join-Path $runtimeDirectory $file) -Destination (Join-Path $output $file)
        }
        $notices.Add("# Third-party components`n`nThis self-contained build includes the .NET runtime and the packages below. See also the runtime's LICENSE.txt and THIRD-PARTY-NOTICES.txt.`n")
    }
    else {
        # The runtime is not shipped; record the shared-framework version the build runs on.
        $runtimeVersion = [string](Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json).runtimeOptions.framework.version
        if (!$runtimeVersion) { throw 'The shared-framework version is missing from the runtimeconfig.json.' }
        $notices.Add("# Third-party components`n`nThis framework-dependent build runs on an installed .NET $runtimeVersion runtime, which it does not include, and includes the packages below.`n")
    }
    foreach ($entry in ($assets.libraries.psobject.Properties | Sort-Object Name)) {
        if ($entry.Value.type -ne 'package') { continue }
        $packageDirectory = $null
        foreach ($root in ($assets.packageFolders.psobject.Properties | ForEach-Object { $_.Name })) {
            $candidate = Join-Path $root $entry.Value.path
            if (Test-Path -LiteralPath $candidate) { $packageDirectory = $candidate; break }
        }
        if (!$packageDirectory) { throw "Restored package not found: $($entry.Name)" }
        $specFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' | Select-Object -First 1
        [xml]$spec = Get-Content -LiteralPath $specFile.FullName -Raw
        $metadata = $spec.package.metadata
        $license = if ($metadata.license) { $metadata.license.InnerText } else { $metadata.licenseUrl }
        $notices.Add("## $($entry.Key)`n`nAuthors: $($metadata.authors)`n`n$($metadata.copyright)`n`nLicense: $license`n`nProject: $($metadata.projectUrl)`n")
        foreach ($file in Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING|THIRD.PARTY.NOTICES)(\..*)?$' }) {
            $notices.Add("### $($file.Name)`n`n" + (Get-Content -LiteralPath $file.FullName -Raw))
        }
    }
    $notices.Add("## MIT license terms (for packages declaring MIT)`n`nPermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the `"Software`"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:`n`nThe above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.`n`nTHE SOFTWARE IS PROVIDED `"AS IS`", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.`n")
    Set-Content -LiteralPath (Join-Path $output 'PACKAGE-NOTICES.md') -Value ($notices -join "`n") -Encoding utf8
    # The head is a WinExe: invoked with `&`, PowerShell does not wait for a GUI-subsystem
    # process, so its exit code would go unread and its files stay locked while archiving.
    $smoke = Start-Process -FilePath (Join-Path $output 'Broiler.Mail.Windows.exe') -ArgumentList '--smoke-test' -NoNewWindow -PassThru
    $null = $smoke.Handle  # Windows PowerShell only reports ExitCode once the handle is held.
    try {
        if (!$smoke.WaitForExit(180000)) { throw 'Published executable smoke test did not finish within three minutes.' }
        $smoke.WaitForExit()
        if ($smoke.ExitCode -ne 0) { throw "Published executable smoke test failed (exit $($smoke.ExitCode))." }
    }
    finally {
        if (!$smoke.HasExited) { Stop-Process -Id $smoke.Id -Force }
        $smoke.Dispose()
    }
    $sdkVersion = dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine SDK version.' }
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine source revision.' }
    $workingTreeChanges = @(git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine working-tree state.' }
    [ordered]@{
        schemaVersion = 1
        applicationVersion = [string]$version
        runtime = $Runtime
        variant = $Variant
        compilation = $(if ($selfContained) { 'NativeAOT' } else { 'IL (framework-dependent)' })
        runtimeVersion = $runtimeVersion
        processArchitecture = $hostArchitecture
        sdkVersion = [string]$sdkVersion
        sourceRevision = [string]$revision
        workingTreeDirty = $workingTreeChanges.Count -gt 0
        builtAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        signingStatus = 'unsigned'
        smokeTest = 'passed'
        smokeScope = 'Headless composition and UI rendering; no provider or native-window acceptance.'
        htmlRendererIsolation = 'not-implemented'
        releaseReady = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $releaseName.zip" -Encoding ascii
    Write-Output "Created unsigned validation artifact $archive ($Variant; HTML process isolation remains pending)."
}
finally { Pop-Location }
