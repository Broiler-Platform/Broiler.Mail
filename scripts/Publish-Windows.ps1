param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if (![System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows) -or
    $Runtime -ne "win-$hostArchitecture") {
    throw "Publishing includes executing the smoke test. Run $Runtime on a matching Windows .NET process (current: $hostArchitecture)."
}
$repository = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$releaseName = "Broiler.Mail-$version-$Runtime"
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
    dotnet publish src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj -c Release -r $Runtime --self-contained true -p:PublishAot=true -p:PublishTrimmed=true -o $output
    if ($LASTEXITCODE -ne 0) { throw "Publishing failed (exit $LASTEXITCODE)." }
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $output 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $repository 'docs/version-2-acceptance.md') -Destination (Join-Path $output 'START-HERE.md')
    Copy-Item -LiteralPath (Join-Path $repository 'docs/html-renderer-security.md') -Destination (Join-Path $output 'html-renderer-security.md')

    # Include package identity/license metadata and license files from the actual restored graph.
    $assets = Get-Content -LiteralPath 'src/Broiler.Mail.Windows/obj/project.assets.json' -Raw | ConvertFrom-Json
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
    $notices = [System.Collections.Generic.List[string]]::new()
    $notices.Add("# Third-party components`n`nThis self-contained build includes the .NET runtime and the packages below. See also the runtime's LICENSE.txt and THIRD-PARTY-NOTICES.txt.`n")
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
    & (Join-Path $output 'Broiler.Mail.Windows.exe') --smoke-test
    if ($LASTEXITCODE -ne 0) { throw 'Published executable smoke test failed.' }
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
        compilation = 'NativeAOT'
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
    Write-Output "Created unsigned validation artifact $archive (HTML process isolation remains pending)."
}
finally { Pop-Location }
