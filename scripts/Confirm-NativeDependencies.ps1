param(
    [Parameter(Mandatory)][string]$AssetsPath,
    [Parameter(Mandatory)][string]$Runtime
)

$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
$targets = @($assets.targets.psobject.Properties | Where-Object { $_.Name.EndsWith("/$Runtime", [StringComparison]::OrdinalIgnoreCase) })
if ($targets.Count -ne 1) { throw "Expected exactly one restored target for $Runtime in $AssetsPath." }

# Check runtime libraries, not compile-only analyzers/source generators/tooling.
# Match package identity AND asset basename: an engine must not slip through in a
# differently named metapackage. Known-engine guard; NativeAOT analysis is complementary.
$forbidden = '^(Jint|Jurassic|JavaScriptEngineSwitcher(?:\..*)?|Microsoft\.ClearScript(?:\..*)?|ClearScript(?:\..*)?|NiL\.JS|YantraJS(?:\..*)?|ChakraCore|V8(?:\..*)?|Microsoft\.CodeAnalysis(?:\..*)?|Microsoft\.CSharp|System\.Linq\.Dynamic\.Core|Broiler\.(?:JS|JavaScript)(?:\..*)?|Broiler\.HtmlBridge\.Scripting(?:\..*)?|Broiler\.VM(?:\..*JavaScript.*)?)$'
$violations = [System.Collections.Generic.List[string]]::new()
$checked = 0
foreach ($library in $targets[0].Value.psobject.Properties) {
    $runtimeAssets = @()
    foreach ($group in @('runtime', 'native', 'runtimeTargets')) {
        $property = $library.Value.psobject.Properties[$group]
        if ($property) { $runtimeAssets += @($property.Value.psobject.Properties.Name | Where-Object { $_ -notmatch '(^|/)_\._$' }) }
    }
    if ($runtimeAssets.Count -eq 0) { continue }
    $checked++
    $identity = ($library.Name -split '/')[0]
    if ($identity -match $forbidden) { $violations.Add($library.Name) }
    foreach ($asset in $runtimeAssets) {
        $assetName = [IO.Path]::GetFileNameWithoutExtension($asset.Replace('\', '/'))
        if ($assetName -match $forbidden) { $violations.Add("$($library.Name): $asset") }
    }
}
if ($checked -eq 0) { throw "No runtime libraries found for $Runtime; dependency policy cannot be verified." }
if ($violations.Count) { throw "Native Mail forbids scripting/dynamic compilation runtime dependencies:`n$($violations -join "`n")" }
Write-Output "Native dependency policy passed: $checked runtime libraries ($Runtime)."
