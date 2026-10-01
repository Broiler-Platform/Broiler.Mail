param([string]$UiRoot = (Join-Path $PSScriptRoot '../../Broiler.UI'))
$ErrorActionPreference = 'Stop'
$mailRoot = Split-Path $PSScriptRoot -Parent
$uiPath = (Resolve-Path -LiteralPath $UiRoot).Path
$output = Join-Path $mailRoot 'artifacts/c04/packages'
New-Item -ItemType Directory -Force -Path $output | Out-Null
[xml]$versions = Get-Content -LiteralPath (Join-Path $mailRoot 'Directory.Packages.props')
$version = [string]$versions.Project.PropertyGroup.BroilerUiVersion
$projects = @{}
Get-ChildItem -LiteralPath (Join-Path $uiPath 'src') -Filter '*.csproj' -Recurse | ForEach-Object { $projects[$_.BaseName] = $_.FullName }
$closure = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Add-Project([string]$path) {
    if (!$closure.Add($path)) { return }
    [xml]$project = Get-Content -LiteralPath $path
    foreach ($reference in $project.SelectNodes('//ProjectReference')) {
        $dependency = [IO.Path]::GetFullPath((Join-Path (Split-Path $path -Parent) $reference.Include))
        Add-Project $dependency
    }
}
[xml]$application = Get-Content -LiteralPath (Join-Path $mailRoot 'src/Broiler.Mail.Application/Broiler.Mail.Application.csproj')
foreach ($reference in $application.SelectNodes('//PackageReference')) {
    if ($reference.Include.StartsWith('Broiler.UI.')) {
        if (!$projects.ContainsKey($reference.Include)) { throw "Missing UI source project: $($reference.Include)" }
        Add-Project $projects[$reference.Include]
    }
}
$solution = Join-Path $mailRoot 'artifacts/c04/Preview.slnx'
$lines = @('<Solution>') + @($closure | Sort-Object | ForEach-Object { '  <Project Path="' + [System.Security.SecurityElement]::Escape($_) + '" />' }) + @('</Solution>')
$lines | Set-Content -LiteralPath $solution
dotnet pack $solution -c Release "-p:Version=$version" -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'C-04 package build failed.' }
dotnet restore (Join-Path $mailRoot 'Broiler.Mail.slnx') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mail restore failed.' }
