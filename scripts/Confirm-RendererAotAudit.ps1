param([Parameter(Mandatory)][string]$AssetsPath)
$ErrorActionPreference = 'Stop'
$audit = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../eng/renderer-aot-audit.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
$substitutions = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Broiler.Mail.Windows/NativeAot.Substitutions.xml')).Replace("`r`n", "`n")
$substitutionHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($substitutions)))
if ($substitutionHash -ne $audit.normalizedSubstitutionsSha256) { throw 'Renderer substitutions changed; review and update the audit.' }
foreach ($expected in $audit.packages) {
    $entry = $assets.libraries.psobject.Properties[$expected.identity]
    if (-not $entry) { throw "Renderer package changed. Re-audit NativeAot.Substitutions.xml and update renderer-aot-audit.json ($($expected.identity))." }
    $asset = $null
    foreach ($folder in $assets.packageFolders.psobject.Properties.Name) {
        $candidate = Join-Path (Join-Path $folder $entry.Value.path) $expected.asset
        if (Test-Path -LiteralPath $candidate) { $asset = $candidate; break }
    }
    if (-not $asset -or (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash -ne $expected.sha256) {
        throw "Renderer asset changed or missing. Re-audit the compatibility substitution: $($expected.identity)."
    }
}
Write-Output "Renderer compatibility audit matches restored packages (reviewed $($audit.reviewedOn))."
