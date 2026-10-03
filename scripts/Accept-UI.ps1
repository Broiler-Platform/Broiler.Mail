<#
.SYNOPSIS
Runs the UI-13 acceptance pass: every gallery fixture at each size and theme, on the published app.

.DESCRIPTION
Publishes the Windows app as NativeAOT (the shipped configuration) unless -Executable is given.
Each run opens one demo fixture, waits for it to settle, and then:
  - captures a screenshot (PrintWindow) for visual review of clipping, overlap, and hierarchy;
  - reads the UI Automation tree and reports unnamed interactive controls, controls cut off by the
    window edge (outside any scroll view), and interactive controls that overlap each other;
  - walks Tab through the window and reports a missing, stuck, unnamed, or off-screen focus;
  - closes the window and checks the exit code and that nothing was written to stderr.
Input is posted to the window's render child, so the run does not take keyboard focus from other
applications, although each window appears on screen briefly. Results, screenshots, and summary.md
go to -Output, with the revision, package versions, SDK, OS, and display scale they apply to.

Automated checks find candidates; screenshots still need a person's review. Display scale, monitor
moves, high contrast, IME, and other machines are outside what one run can change: the summary lists
them as checks this run did not perform.
#>
param(
    [string]$Executable,
    [string]$Output,
    [string[]]$Scenarios,
    [string[]]$Sizes = @('640x480', '1100x720', '1920x1080'),
    [ValidateSet('light', 'dark')]
    [string[]]$Themes = @('light', 'dark'),
    [ValidateRange(0, 60)]
    [int]$TabSteps = 24,
    [ValidateRange(500, 20000)]
    [int]$SettleMilliseconds = 2500,
    # The system text size in percent for every run (--text-scale); 0 uses the system's own setting.
    [ValidateScript({ $_ -eq 0 -or ($_ -ge 100 -and $_ -le 225) })]
    [int]$TextScale = 0,
    # Render with the theme's high-contrast palette (--contrast high).
    [switch]$HighContrast
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (!$Output) { $Output = Join-Path $repository ("artifacts/acceptance/" + (Get-Date -Format 'yyyy-MM-dd-HHmm')) }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

if (!$Executable) {
    # NativeAOT publishing locates the C++ toolchain through vswhere.
    $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
    if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH = "$installer;$env:PATH" }
    $publish = Join-Path $repository 'artifacts/accept-app'
    dotnet publish (Join-Path $repository 'src/Broiler.Mail.Windows/Broiler.Mail.Windows.csproj') -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true -o $publish | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Publishing failed (exit $LASTEXITCODE)." }
    $Executable = Join-Path $publish 'Broiler.Mail.Windows.exe'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path

# The fixture list comes from the app itself, so new fixtures are covered without editing this script.
if (!$Scenarios) {
    $help = & $Executable --help
    $inGallery = $false
    $Scenarios = foreach ($line in $help) {
        if ($line -like 'Plain --demo*') { $inGallery = $true; continue }
        if ($line -like '--measure*') { $inGallery = $false; continue }
        if ($inGallery -and $line -match '^\s+(\S+)\s') { $Matches[1] }
    }
}

# Fixtures whose window is expected to refuse an ordinary close.
$refusesClose = @{ 'draft-conflict' = 'The draft cannot be saved, so the window stays open (intended).' }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

public sealed class AcceptanceNode
{
    public string Type;
    public string Name;
    public string ClassName;
    public bool Offscreen;
    public bool Focusable;
    public bool InScrollView;
    public int Parent;
    public Rect Bounds;
    // The part not hidden by an enclosing scroll view; UIA reports the whole element.
    public Rect Visible;
}

public static class Acceptance
{
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    public struct RECT { public int L, T, R, B; }
    public struct POINT { public int X, Y; }

    static readonly HashSet<string> Interactive = new HashSet<string>(new[] {
        "ControlType.Button", "ControlType.Edit", "ControlType.Hyperlink", "ControlType.ListItem", "ControlType.TabItem",
        "ControlType.CheckBox", "ControlType.ComboBox", "ControlType.RadioButton", "ControlType.Spinner", "ControlType.Slider",
        "ControlType.Document", "ControlType.List", "ControlType.Tab", "ControlType.Separator" });

    // Leaf controls that must not sit on top of one another.
    static readonly HashSet<string> Solid = new HashSet<string>(new[] {
        "ControlType.Button", "ControlType.Edit", "ControlType.CheckBox", "ControlType.ComboBox", "ControlType.RadioButton",
        "ControlType.Spinner", "ControlType.Slider", "ControlType.TabItem" });

    public static Rect ClientBounds(IntPtr hwnd)
    {
        RECT r; GetClientRect(hwnd, out r);
        POINT p = new POINT(); ClientToScreen(hwnd, ref p);
        return new Rect(p.X, p.Y, r.R - r.L, r.B - r.T);
    }

    public static List<AcceptanceNode> Snapshot(IntPtr hwnd, int limit)
    {
        var nodes = new List<AcceptanceNode>();
        var root = AutomationElement.FromHandle(hwnd);
        Walk(TreeWalker.RawViewWalker, root, -1, false, new Rect(-1e7, -1e7, 2e7, 2e7), nodes, limit, 0);
        return nodes;
    }

    static void Walk(TreeWalker walker, AutomationElement element, int parent, bool inScroll, Rect clip, List<AcceptanceNode> nodes, int limit, int depth)
    {
        if (depth > 40) return;
        AutomationElement child = walker.GetFirstChild(element);
        while (child != null && nodes.Count < limit)
        {
            var node = new AcceptanceNode();
            try
            {
                var c = child.Current;
                node.Type = c.ControlType.ProgrammaticName;
                node.Name = c.Name ?? "";
                node.ClassName = c.ClassName ?? "";
                node.Offscreen = c.IsOffscreen;
                node.Focusable = c.IsKeyboardFocusable;
                node.Bounds = c.BoundingRectangle;
            }
            catch (ElementNotAvailableException) { child = walker.GetNextSibling(child); continue; }
            node.Parent = parent;
            node.InScrollView = inScroll;
            node.Visible = Rect.Intersect(node.Bounds, clip);
            nodes.Add(node);
            int index = nodes.Count - 1;
            bool container = node.ClassName.Contains("Scroll") || node.ClassName.Contains("ListView") || node.ClassName.Contains("RichEdit");
            Rect childClip = container ? Rect.Intersect(clip, node.Bounds) : clip;
            Walk(walker, child, index, inScroll || container, childClip, nodes, limit, depth + 1);
            child = walker.GetNextSibling(child);
        }
    }

    static bool IsAncestor(List<AcceptanceNode> nodes, int ancestor, int index)
    {
        for (int at = nodes[index].Parent; at >= 0; at = nodes[at].Parent)
            if (at == ancestor) return true;
        return false;
    }

    static string Describe(AcceptanceNode node)
    {
        return node.Type.Replace("ControlType.", "") + " '" + node.Name + "'";
    }

    public static List<string> Check(List<AcceptanceNode> nodes, Rect client)
    {
        var findings = new List<string>();
        if (nodes.Count == 0) { findings.Add("EMPTY_TREE: the window exposes no UI Automation elements."); return findings; }
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            bool interactive = Interactive.Contains(n.Type) || (n.Focusable && n.Type == "ControlType.Group");
            if (n.Offscreen || n.Bounds.IsEmpty) continue;
            if (interactive && n.Name.Trim().Length == 0)
                findings.Add("UNNAMED: " + Describe(n) + " (" + n.ClassName + ") has no accessible name.");
            if (interactive && !n.InScrollView &&
                (n.Bounds.Left < client.Left - 2 || n.Bounds.Top < client.Top - 2 || n.Bounds.Right > client.Right + 2 || n.Bounds.Bottom > client.Bottom + 2))
                findings.Add("CLIPPED: " + Describe(n) + " extends past the window (" + n.Bounds + " outside " + client + ").");
        }
        for (int i = 0; i < nodes.Count; i++)
        {
            var a = nodes[i];
            if (a.Offscreen || a.Visible.IsEmpty || !Solid.Contains(a.Type)) continue;
            for (int j = i + 1; j < nodes.Count; j++)
            {
                var b = nodes[j];
                if (b.Offscreen || b.Visible.IsEmpty || !Solid.Contains(b.Type) || IsAncestor(nodes, i, j) || IsAncestor(nodes, j, i)) continue;
                Rect overlap = Rect.Intersect(a.Visible, b.Visible);
                if (overlap.IsEmpty) continue;
                double smaller = Math.Min(a.Visible.Width * a.Visible.Height, b.Visible.Width * b.Visible.Height);
                if (smaller > 0 && overlap.Width * overlap.Height > 0.25 * smaller)
                    findings.Add("OVERLAP: " + Describe(a) + " and " + Describe(b) + " overlap (" + a.Bounds + ", " + b.Bounds + ").");
            }
        }
        return findings;
    }

    /// <summary>The deepest element reporting keyboard focus: the window is not in the foreground, so system focus is elsewhere.</summary>
    public static AcceptanceNode Focused(IntPtr hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var found = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
        if (found.Count == 0) return null;
        var c = found[found.Count - 1].Current;
        var node = new AcceptanceNode();
        node.Type = c.ControlType.ProgrammaticName; node.Name = c.Name ?? ""; node.ClassName = c.ClassName ?? "";
        node.Offscreen = c.IsOffscreen; node.Bounds = c.BoundingRectangle;
        return node;
    }

    public static string Label(AcceptanceNode node) { return node == null ? "(none)" : Describe(node); }
}
'@

[Acceptance]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Save-Screenshot([IntPtr]$handle, [string]$path) {
    $r = New-Object Acceptance+RECT
    [Acceptance]::GetWindowRect($handle, [ref]$r) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ([Math]::Max(1, $r.R - $r.L)), ([Math]::Max(1, $r.B - $r.T))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [Acceptance]::PrintWindow($handle, $dc, 2) | Out-Null
    $graphics.ReleaseHdc($dc); $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
}

function Invoke-Run([string]$scenario, [string]$size, [string]$theme) {
    $name = "$scenario-$size-$theme"
    $stdout = Join-Path $Output "$name.out.txt"; $stderr = Join-Path $Output "$name.err.txt"
    $result = [ordered]@{ scenario = $scenario; size = $size; theme = $theme; findings = @(); tab = @(); screenshot = "$name.png" }
    $arguments = @('--demo', $scenario, '--theme', $theme, '--size', $size)
    if ($TextScale -gt 0) { $arguments += @('--text-scale', "$TextScale") }
    if ($HighContrast) { $arguments += @('--contrast', 'high') }
    $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Normal -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
        -ArgumentList $arguments
    $null = $process.Handle
    for ($i = 0; $i -lt 150 -and $process.MainWindowHandle -eq 0 -and !$process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if ($process.MainWindowHandle -eq 0) {
        $result.findings += "START: no window appeared (exit $($process.ExitCode))."
        if (!$process.HasExited) { $process.Kill() }
        return $result
    }
    Start-Sleep -Milliseconds $SettleMilliseconds
    $window = $process.MainWindowHandle
    $render = [Acceptance]::FindWindowEx($window, [IntPtr]::Zero, 'BroilerGraphicsDirect2DRenderHost', $null)
    $result.dpiScale = [Acceptance]::GetDpiForWindow($window) / 96.0
    Save-Screenshot $window (Join-Path $Output "$name.png")

    $client = [Acceptance]::ClientBounds($render)
    # The app's own tree: the render child, not the frame (whose caption buttons are native and outside the client).
    $nodes = [Acceptance]::Snapshot($render, 4000)
    $result.elements = $nodes.Count
    $result.findings += @([Acceptance]::Check($nodes, $client))

    # Tab walk: each step must land on a named, visible element, and focus must keep moving.
    $first = $null; $previous = $null
    for ($step = 1; $step -le $TabSteps; $step++) {
        [Acceptance]::PostMessage($render, 0x100, [IntPtr]0x09, [IntPtr]0) | Out-Null
        [Acceptance]::PostMessage($render, 0x101, [IntPtr]0x09, [IntPtr]0) | Out-Null
        Start-Sleep -Milliseconds 150
        $focused = [Acceptance]::Focused($render)
        $label = [Acceptance]::Label($focused)
        $result.tab += $label
        if ($null -eq $focused) { $result.findings += "TAB_NOFOCUS: nothing has focus after Tab $step."; break }
        if ($focused.Name.Trim().Length -eq 0) { $result.findings += "TAB_UNNAMED: Tab $step reached $label ($($focused.ClassName))." }
        if ($focused.Offscreen -or $focused.Bounds.IsEmpty -or !$client.IntersectsWith($focused.Bounds)) {
            $result.findings += "TAB_OFFSCREEN: Tab $step reached $label, which is not on screen."
        }
        if ($label -eq $previous) { $result.findings += "TAB_STUCK: Tab $step stayed on $label."; break }
        if ($label -eq $first) { $result.tabCycle = $step - 1; break }
        if ($null -eq $first) { $first = $label }
        $previous = $label
    }

    [Acceptance]::PostMessage($window, 0x10, [IntPtr]0, [IntPtr]0) | Out-Null
    if (!$process.WaitForExit(5000)) {
        $process.Kill(); $process.WaitForExit()
        if ($refusesClose.ContainsKey($scenario)) { $result.note = $refusesClose[$scenario] }
        else { $result.findings += 'CLOSE: the window did not close within 5 seconds.' }
    }
    elseif ($process.ExitCode -ne 0) { $result.findings += "EXIT: exit code $($process.ExitCode)." }
    $errors = (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)
    if ($errors -and $errors.Trim().Length -gt 0) { $result.findings += "STDERR: $($errors.Trim())" }
    return $result
}

$results = @()
$total = $Scenarios.Count * $Sizes.Count * $Themes.Count
$index = 0
foreach ($scenario in $Scenarios) {
    foreach ($size in $Sizes) {
        foreach ($theme in $Themes) {
            $index++
            Write-Host "[$index/$total] $scenario $size $theme"
            $results += [pscustomobject](Invoke-Run $scenario $size $theme)
        }
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'results.json') -Encoding utf8

# What the results apply to.
$revision = (git -C $repository rev-parse --short HEAD) 2>$null
if ((git -C $repository status --porcelain) 2>$null) { $revision = "$revision plus uncommitted changes" }
$props = [xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Raw)
$packages = ($props.Project.PropertyGroup.ChildNodes | Where-Object { $_.Name -like 'Broiler*Version' } |
    ForEach-Object { "$($_.Name -replace 'Version$', '') $($_.InnerText)" }) -join ', '
$os = (Get-CimInstance Win32_OperatingSystem)
$scales = ($results | Where-Object { $_.dpiScale } | ForEach-Object { $_.dpiScale } | Sort-Object -Unique) -join ', '

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# UI acceptance run, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$lines.Add('')
$lines.Add("- Revision: $revision")
$lines.Add("- Packages: $packages")
$lines.Add("- Executable: $Executable")
$lines.Add("- SDK: $(dotnet --version); OS: $($os.Caption) $($os.Version); architecture: $env:PROCESSOR_ARCHITECTURE")
$textScaleNote = if ($TextScale -gt 0) { "$TextScale % (fixed with --text-scale)" } else { 'system setting' }
$lines.Add("- Text scale: $textScaleNote; high-contrast palette forced: $([bool]$HighContrast)")
$lines.Add("- Display scale: $scales; monitors: $([Acceptance]::GetSystemMetrics(80)); high contrast on: $([System.Windows.Forms.SystemInformation]::HighContrast)")
$lines.Add("- Method: published executable, demo fixtures (fixed clock and data), posted keyboard input, UI Automation, PrintWindow screenshots")
$lines.Add('')
$clean = @($results | Where-Object { $_.findings.Count -eq 0 }).Count
$lines.Add("$clean of $($results.Count) runs have no automated findings. Screenshots still need visual review.")
$lines.Add('')
$lines.Add('| Fixture | Size | Theme | Elements | Tab cycle | Findings | Screenshot |')
$lines.Add('| --- | --- | --- | ---: | ---: | --- | --- |')
foreach ($r in $results) {
    $cycle = if ($r.tabCycle) { $r.tabCycle } else { '-' }
    $summary = if ($r.findings.Count -eq 0) { 'none' } else { ($r.findings | ForEach-Object { ($_ -split ':')[0] } | Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ', ' }
    if ($r.note) { $summary = "$summary; $($r.note)" }
    $lines.Add("| $($r.scenario) | $($r.size) | $($r.theme) | $($r.elements) | $cycle | $summary | [$($r.screenshot)]($($r.screenshot)) |")
}
$lines.Add('')
$lines.Add('## Findings')
$lines.Add('')
foreach ($r in $results | Where-Object { $_.findings.Count -gt 0 }) {
    $lines.Add("### $($r.scenario) $($r.size) $($r.theme)")
    foreach ($f in $r.findings) { $lines.Add("- $f") }
    $lines.Add('')
}
$lines.Add('## Not covered by this run')
$lines.Add('')
$lines.Add('- Other display scales and moving between monitors (this run used the scales listed above).')
$lines.Add('- Actual high contrast and reduced motion (system settings); text scale other than this run''s.')
$lines.Add('- Mouse, precision wheel, AltGr/dead keys, and IME (posted input covers keys only).')
$lines.Add('- A screen reader reading and announcing (UI Automation structure is checked, speech is not).')
$lines.Add('- Other architectures and machines.')
$summaryPath = Join-Path $Output 'summary.md'
Set-Content -LiteralPath $summaryPath -Value $lines -Encoding utf8
Write-Host "Summary: $summaryPath"
