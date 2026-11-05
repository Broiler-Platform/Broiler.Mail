$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "Broiler.Mail.DependencyPolicy-$([Guid]::NewGuid().ToString('N')).json"
function Assert-Policy([hashtable]$Libraries, [bool]$ShouldPass, [string]$Runtime = 'win-x64') {
    @{ targets = @{ 'net10.0-windows7.0/win-x64' = $Libraries } } |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary
    $passed = $true
    try { & "$PSScriptRoot/Confirm-NativeDependencies.ps1" -AssetsPath $temporary -Runtime $Runtime | Out-Null }
    catch { $passed = $false }
    if ($passed -ne $ShouldPass) { throw "Unexpected policy result (expected pass=$ShouldPass): $($Libraries.Keys -join ', ')" }
}
try {
    $safe = @{ 'Broiler.HTML.Core/1.0' = @{ runtime = @{ 'lib/net10.0/Broiler.HTML.Core.dll' = @{} } } }
    Assert-Policy $safe $true
    Assert-Policy $safe $false 'win-arm64'
    Assert-Policy @{} $false
    foreach ($package in @('Jint', 'Broiler.JavaScript.Runtime', 'Broiler.VM.Profile.JavaScript', 'Microsoft.ClearScript.V8', 'Microsoft.CodeAnalysis.CSharp', 'System.Linq.Dynamic.Core')) {
        $libraries = $safe.Clone()
        $libraries["$package/1.0"] = @{ runtime = @{ "lib/net10.0/$package.dll" = @{} } }
        Assert-Policy $libraries $false
    }
    $renamed = $safe.Clone()
    $renamed['Unrelated/1.0'] = @{ runtimeTargets = @{ 'runtimes/win-x64/lib/net10.0/Jint.dll' = @{ assetType = 'runtime'; rid = 'win-x64' } } }
    Assert-Policy $renamed $false
    $tools = $safe.Clone()
    $tools['Microsoft.CodeAnalysis.CSharp/1.0'] = @{ compile = @{ 'lib/net10.0/Microsoft.CodeAnalysis.CSharp.dll' = @{} } }
    Assert-Policy $tools $true
    Write-Output 'Native dependency policy fixtures passed.'
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
