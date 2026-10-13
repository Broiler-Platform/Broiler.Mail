[CmdletBinding()]
param(
    [ValidateSet('apk', 'aab')][string]$Format = 'apk',
    [ValidateSet('android-arm64', 'android-x64', 'all')][string]$Runtime = 'android-arm64',
    [string]$Version = '',
    [ValidateRange(1, 2100000000)][int]$VersionCode = 1
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = $props.SelectSingleNode('/Project/PropertyGroup/BroilerMailVersion').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
$destination = Join-Path $root "artifacts/android/$Version-$Runtime-$Format"
New-Item -ItemType Directory -Force $destination | Out-Null
$arguments = @('publish', (Join-Path $root 'src/Broiler.Mail.Android/Broiler.Mail.Android.csproj'),
    '-c', 'Release', '-o', $destination, "-p:AndroidPackageFormat=$Format",
    "-p:BroilerMailVersion=$Version", "-p:ApplicationVersion=$VersionCode")
if ($Runtime -ne 'all') { $arguments += "-p:BroilerAndroidAbis=$Runtime" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Android publish failed.' }
$package = Get-ChildItem -LiteralPath $destination -Filter "*-Signed.$Format" | Select-Object -First 1
if (-not $package) { throw "No signed $Format was produced." }
$target = Join-Path $root "artifacts/Broiler.Mail-$Version-$Runtime.$Format"
Copy-Item -LiteralPath $package.FullName -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($target))" | Set-Content -LiteralPath "$target.sha256" -Encoding utf8
Write-Host "Created $target (development-key signed; not a store release)."
