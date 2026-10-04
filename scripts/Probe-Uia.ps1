<#
.SYNOPSIS
Probes Broiler.Mail's UI Automation semantics from outside the process, on the published app.

.DESCRIPTION
Publishes the Windows app as NativeAOT (the shipped configuration) unless -Executable is given, then runs
demo fixtures and reads their UI Automation tree as a screen reader would, through the UIA client in this
process. Each check prints PASS or FAIL with what it read:
  - disclosure: in the composer (draft-invalid), the "Cc and Bcc" toggle exposes ExpandCollapse with the
    state the section is in, names the section it opens through ControllerFor (a part with a name of its
    own, holding the Cc and Bcc fields), and Expand and Collapse change the state and show or hide the
    Cc and Bcc fields.
  - draft-error: after Check draft rejected the To address (draft-invalid), the To field reports
    IsDataValidForForm = false, and its DescribedBy and FullDescription carry the error.
  - account-error: after Save account rejected the email address (invalid-setup), the Email address field
    reports IsDataValidForForm = false, and its DescribedBy and FullDescription carry the error.
  - row-names: every message row of the inbox fixture is named with its received date.
  - runtime-ids: in new-mail, each row keeps its runtime ID after F5 brings new mail above it, and the new
    rows get IDs no other row had.
Properties the managed UIA client does not know (IsDataValidForForm, DescribedBy, FullDescription) are read
through the UIA COM client. Input is posted to the window's render child or given through UI Automation
patterns, so the run does not take keyboard focus from other applications, although each window appears
briefly. Every demo process this script starts is closed, or killed if it does not close. Results go to
-Output as results.json and summary.md.
#>
param(
    [string]$Executable,
    [string]$Output,
    [ValidateSet('disclosure', 'draft-error', 'account-error', 'row-names', 'runtime-ids')]
    [string[]]$Checks = @('disclosure', 'draft-error', 'account-error', 'row-names', 'runtime-ids'),
    [string]$Size = '1100x720',
    [ValidateRange(500, 20000)]
    [int]$SettleMilliseconds = 2500
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (!$Output) { $Output = Join-Path $repository ("artifacts/uia-probe/" + (Get-Date -Format 'yyyy-MM-dd-HHmm')) }
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

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, WindowsBase -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;

// The UIA COM client, declared up to the members this probe calls. Slots it does not call are placeholders that
// keep the vtable order of UIAutomationClient.h.
[ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
public class CUIAutomationClass { }

[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeCondition { }

[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeAutomation
{
    void CompareElements(); void CompareRuntimeIds(); void GetRootElement();
    IProbeElement ElementFromHandle(IntPtr hwnd);
    void ElementFromPoint(); void GetFocusedElement(); void GetRootElementBuildCache(); void ElementFromHandleBuildCache();
    void ElementFromPointBuildCache(); void GetFocusedElementBuildCache(); void CreateTreeWalker(); void ControlViewWalker();
    void ContentViewWalker(); void RawViewWalker(); void RawViewCondition(); void ControlViewCondition(); void ContentViewCondition();
    void CreateCacheRequest();
    IProbeCondition CreateTrueCondition();
}

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeElement
{
    void SetFocus(); void GetRuntimeId(); void FindFirst();
    IProbeElementArray FindAll(int scope, IProbeCondition condition);
    void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProbeElementArray
{
    int Length { get; }
    IProbeElement GetElement(int index);
}

/// <summary>What the COM client reads for a field: the form properties UIA2's managed client has no identifiers for.</summary>
public sealed class FormState
{
    public bool Found;
    public object IsDataValidForForm;
    public object IsRequiredForForm;
    public string FullDescription;
    public string[] DescribedBy = new string[0];
}

/// <summary>An element another one names through a relation property, such as ControllerFor, with the fields inside it.</summary>
public sealed class Related
{
    public string Name;
    public int ControlType;
    public string[] Edits = new string[0];
}

public static class UiaProbe
{
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

    const int NameId = 30005, ControlTypeId = 30003, IsRequiredForFormId = 30025, IsDataValidForFormId = 30103,
        DescribedById = 30105, FullDescriptionId = 30159;

    public static List<AutomationElement> All(IntPtr render)
    {
        var found = new List<AutomationElement>();
        Walk(TreeWalker.RawViewWalker, AutomationElement.FromHandle(render), found, 0);
        return found;
    }

    static void Walk(TreeWalker walker, AutomationElement element, List<AutomationElement> found, int depth)
    {
        if (depth > 40 || found.Count > 6000) return;
        for (var child = walker.GetFirstChild(element); child != null; child = walker.GetNextSibling(child))
        {
            found.Add(child);
            Walk(walker, child, found, depth + 1);
        }
    }

    public static AutomationElement Find(List<AutomationElement> all, ControlType type, string name)
    {
        foreach (var element in all)
        {
            try { if (element.Current.ControlType == type && element.Current.Name == name) return element; }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }

    public static List<AutomationElement> Rows(AutomationElement list)
    {
        var rows = new List<AutomationElement>();
        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(list); child != null; child = walker.GetNextSibling(child))
            if (child.Current.ControlType == ControlType.ListItem) rows.Add(child);
        return rows;
    }

    public static string RuntimeId(AutomationElement element) { return string.Join(".", element.GetRuntimeId()); }

    /// <summary>Reads the form properties of the element named <paramref name="name"/> with the UIA control type id <paramref name="controlType"/>.</summary>
    public static FormState Form(IntPtr render, int controlType, string name)
    {
        var state = new FormState();
        var automation = (IProbeAutomation)new CUIAutomationClass();
        IProbeElement root = automation.ElementFromHandle(render);
        IProbeElementArray all = root.FindAll(4 /* TreeScope_Descendants */, automation.CreateTrueCondition());
        for (int i = 0; i < all.Length; i++)
        {
            IProbeElement element = all.GetElement(i);
            object type = element.GetCurrentPropertyValue(ControlTypeId);
            if (!(type is int) || (int)type != controlType) continue;
            if ((element.GetCurrentPropertyValue(NameId) as string) != name) continue;
            state.Found = true;
            state.IsDataValidForForm = element.GetCurrentPropertyValue(IsDataValidForFormId);
            state.IsRequiredForForm = element.GetCurrentPropertyValue(IsRequiredForFormId);
            state.FullDescription = element.GetCurrentPropertyValue(FullDescriptionId) as string;
            var names = new List<string>();
            foreach (IProbeElement target in Elements(element.GetCurrentPropertyValue(DescribedById)))
                names.Add(target.GetCurrentPropertyValue(NameId) as string ?? "");
            state.DescribedBy = names.ToArray();
            return state;
        }
        return state;
    }

    /// <summary>The elements a relation property names. The COM client hands them over as an element array.</summary>
    static List<IProbeElement> Elements(object value)
    {
        var elements = new List<IProbeElement>();
        var array = value as IProbeElementArray;
        if (array != null)
            for (int i = 0; i < array.Length; i++) elements.Add(array.GetElement(i));
        var items = value as Array;
        if (items != null)
            foreach (var item in items) { var element = item as IProbeElement; if (element != null) elements.Add(element); }
        return elements;
    }

    static IProbeElement FindNative(IProbeAutomation automation, IntPtr render, int controlType, string name)
    {
        IProbeElementArray all = automation.ElementFromHandle(render).FindAll(4 /* TreeScope_Descendants */, automation.CreateTrueCondition());
        for (int i = 0; i < all.Length; i++)
        {
            IProbeElement element = all.GetElement(i);
            object type = element.GetCurrentPropertyValue(ControlTypeId);
            if (type is int && (int)type == controlType && (element.GetCurrentPropertyValue(NameId) as string) == name) return element;
        }
        return null;
    }

    /// <summary>The elements the named element relates to through <paramref name="propertyId"/>, with the edits inside each.</summary>
    public static Related[] Relation(IntPtr render, int controlType, string name, int propertyId)
    {
        var automation = (IProbeAutomation)new CUIAutomationClass();
        IProbeElement source = FindNative(automation, render, controlType, name);
        var related = new List<Related>();
        if (source == null) return null;
        foreach (IProbeElement target in Elements(source.GetCurrentPropertyValue(propertyId)))
        {
            var item = new Related { Name = target.GetCurrentPropertyValue(NameId) as string ?? "" };
            object type = target.GetCurrentPropertyValue(ControlTypeId);
            item.ControlType = type is int ? (int)type : 0;
            var edits = new List<string>();
            IProbeElementArray inside = target.FindAll(4, automation.CreateTrueCondition());
            for (int i = 0; i < inside.Length; i++)
            {
                IProbeElement element = inside.GetElement(i);
                object insideType = element.GetCurrentPropertyValue(ControlTypeId);
                if (insideType is int && (int)insideType == 50004) edits.Add(element.GetCurrentPropertyValue(NameId) as string ?? "");
            }
            item.Edits = edits.ToArray();
            related.Add(item);
        }
        return related.ToArray();
    }
}
'@

$EditTypeId = 50004
$ButtonTypeId = 50000
$ControllerForId = 30104

function Start-Fixture([string]$fixture, [string]$name) {
    $stdout = Join-Path $Output "$name.out.txt"; $stderr = Join-Path $Output "$name.err.txt"
    $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Normal -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
        -ArgumentList @('--demo', $fixture, '--theme', 'light', '--size', $Size)
    $null = $process.Handle
    for ($i = 0; $i -lt 150 -and $process.MainWindowHandle -eq 0 -and !$process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    Start-Sleep -Milliseconds $SettleMilliseconds
    # The main window handle can briefly be the console window of the published exe; wait for the one with the render child.
    $render = [IntPtr]::Zero; $window = [IntPtr]::Zero
    for ($i = 0; $i -lt 50 -and $render -eq [IntPtr]::Zero -and !$process.HasExited; $i++) {
        $process.Refresh()
        $window = $process.MainWindowHandle
        $render = [UiaProbe]::FindWindowEx($window, [IntPtr]::Zero, 'BroilerGraphicsDirect2DRenderHost', $null)
        if ($render -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    }
    if ($render -eq [IntPtr]::Zero) { throw "$fixture opened no window with a render child." }
    [pscustomobject]@{ Process = $process; Window = $window; Render = $render; Stderr = $stderr }
}

function Stop-Fixture($run, $result) {
    if (!$run) { return }
    if (!$run.Process.HasExited) {
        [UiaProbe]::PostMessage($run.Window, 0x10, [IntPtr]0, [IntPtr]0) | Out-Null
        if (!$run.Process.WaitForExit(5000)) { $run.Process.Kill(); $run.Process.WaitForExit(); $result.findings += 'CLOSE: the window did not close within 5 seconds.' }
    }
    $errors = (Get-Content -LiteralPath $run.Stderr -Raw -ErrorAction SilentlyContinue)
    if ($errors -and $errors.Trim().Length -gt 0) { $result.findings += "STDERR: $($errors.Trim())" }
}

function Wait-For([scriptblock]$condition, [int]$milliseconds = 5000) {
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return [bool](& $condition)
}

function Test-Check([string]$check) {
    $result = [ordered]@{ check = $check; findings = @(); read = [ordered]@{} }
    $run = $null
    try {
        switch ($check) {
            'disclosure' {
                $run = Start-Fixture 'draft-invalid' $check
                $all = [UiaProbe]::All($run.Render)
                $toggle = [UiaProbe]::Find($all, [System.Windows.Automation.ControlType]::Button, 'Show Cc and Bcc')
                if (!$toggle) { $toggle = [UiaProbe]::Find($all, [System.Windows.Automation.ControlType]::Button, 'Hide Cc and Bcc') }
                if (!$toggle) { $result.findings += 'TOGGLE: no Cc and Bcc toggle in the composer.'; break }
                $pattern = $null
                if (!$toggle.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) { $result.findings += 'EXPAND_COLLAPSE: the toggle has no ExpandCollapse pattern.'; break }
                $shown = $toggle.Current.Name -like 'Hide*'
                $state = $pattern.Current.ExpandCollapseState
                $result.read.name = $toggle.Current.Name
                $result.read.state = "$state"
                $expected = if ($shown) { 'Expanded' } else { 'Collapsed' }
                if ("$state" -ne $expected) { $result.findings += "STATE: '$($toggle.Current.Name)' reports $state, expected $expected." }
                $controls = @([UiaProbe]::Relation($run.Render, $ButtonTypeId, $toggle.Current.Name, $ControllerForId))
                $result.read.controllerFor = @($controls | ForEach-Object { "$($_.ControlType) '$($_.Name)' [$($_.Edits -join ', ')]" })
                $pattern.Expand(); Start-Sleep -Milliseconds 500
                $result.read.afterExpand = "$($pattern.Current.ExpandCollapseState)"
                if ($result.read.afterExpand -ne 'Expanded') { $result.findings += "EXPAND: after Expand the toggle reports $($result.read.afterExpand)." }
                $targets = @([UiaProbe]::Relation($run.Render, $ButtonTypeId, $toggle.Current.Name, $ControllerForId))
                $result.read.controllerForExpanded = @($targets | ForEach-Object { "$($_.ControlType) '$($_.Name)' [$($_.Edits -join ', ')]" })
                if ($targets.Count -ne 1) { $result.findings += "CONTROLLER_FOR: the expanded toggle names $($targets.Count) elements, expected the section's content." }
                else {
                    if (@($targets[0].Edits) -notcontains 'Cc' -or @($targets[0].Edits) -notcontains 'Bcc') {
                        $result.findings += 'CONTROLLER_FOR: the element the toggle controls does not hold the Cc and Bcc fields.'
                    }
                    # A screen reader that follows the relation reads this name.
                    if (!$targets[0].Name) { $result.findings += 'CONTROLLER_FOR: the element the toggle controls has no name.' }
                }
                $pattern.Collapse(); Start-Sleep -Milliseconds 500
                $result.read.afterCollapse = "$($pattern.Current.ExpandCollapseState)"
                if ($result.read.afterCollapse -ne 'Collapsed') { $result.findings += "COLLAPSE: after Collapse the toggle reports $($result.read.afterCollapse)." }
                $all = [UiaProbe]::All($run.Render)
                if ([UiaProbe]::Find($all, [System.Windows.Automation.ControlType]::Edit, 'Cc')) { $result.findings += 'COLLAPSE: the Cc field is still exposed after Collapse.' }
            }
            { $_ -in 'draft-error', 'account-error' } {
                $fixture = if ($check -eq 'draft-error') { 'draft-invalid' } else { 'invalid-setup' }
                $field = if ($check -eq 'draft-error') { 'To' } else { 'Email address' }
                $run = Start-Fixture $fixture $check
                $form = [UiaProbe]::Form($run.Render, $EditTypeId, $field)
                if (!$form.Found) { $result.findings += "FIELD: no edit named '$field'."; break }
                $result.read.field = $field
                $result.read.isDataValidForForm = "$($form.IsDataValidForForm)"
                $result.read.isRequiredForForm = "$($form.IsRequiredForForm)"
                $result.read.fullDescription = $form.FullDescription
                $result.read.describedBy = @($form.DescribedBy)
                if ($form.IsDataValidForForm -ne $false) { $result.findings += "VALID: '$field' reports IsDataValidForForm = $($form.IsDataValidForForm), expected False." }
                if (!$form.FullDescription -or $form.FullDescription -notlike 'Error:*') { $result.findings += "FULL_DESCRIPTION: '$field' reports '$($form.FullDescription)', expected the error." }
                $message = if ($form.FullDescription) { ($form.FullDescription -replace '^Error:\s*', '') } else { '' }
                $described = @($form.DescribedBy | Where-Object { $_ })
                # The error label comes first; it names the error the description starts with.
                if ($described.Count -eq 0) { $result.findings += "DESCRIBED_BY: '$field' names no description." }
                elseif (!$message -or !($message.Contains($described[0]) -or $described[0].Contains($message))) {
                    $result.findings += "DESCRIBED_BY: '$field' is described first by '$($described[0])', which is not the error '$message'."
                }
            }
            'row-names' {
                $run = Start-Fixture 'inbox' $check
                $all = [UiaProbe]::All($run.Render)
                $list = [UiaProbe]::Find($all, [System.Windows.Automation.ControlType]::List, 'Messages')
                if (!$list) { $result.findings += 'LIST: no Messages list.'; break }
                $rows = @([UiaProbe]::Rows($list))
                $result.read.rows = $rows.Count
                $result.read.first = if ($rows.Count -gt 0) { $rows[0].Current.Name } else { $null }
                if ($rows.Count -eq 0) { $result.findings += 'ROWS: the list exposes no rows.' }
                $unnamed = @($rows | Where-Object { $_.Current.Name -notmatch ', Received: \S' })
                if ($unnamed.Count -gt 0) { $result.findings += "ROW_NAME: $($unnamed.Count) of $($rows.Count) rows have no received date in their name, such as '$($unnamed[0].Current.Name)'." }
            }
            'runtime-ids' {
                $run = Start-Fixture 'new-mail' $check
                $all = [UiaProbe]::All($run.Render)
                $list = [UiaProbe]::Find($all, [System.Windows.Automation.ControlType]::List, 'Messages')
                if (!$list) { $result.findings += 'LIST: no Messages list.'; break }
                $before = @{}
                foreach ($row in [UiaProbe]::Rows($list)) { $before[$row.Current.AutomationId] = [UiaProbe]::RuntimeId($row) }
                $result.read.rowsBefore = $before.Count
                [UiaProbe]::PostMessage($run.Render, 0x100, [IntPtr]0x74, [IntPtr]0) | Out-Null # F5 receives.
                [UiaProbe]::PostMessage($run.Render, 0x101, [IntPtr]0x74, [IntPtr]0) | Out-Null
                $grown = Wait-For { @([UiaProbe]::Rows($list) | Where-Object { !$before.ContainsKey($_.Current.AutomationId) }).Count -gt 0 }
                Start-Sleep -Milliseconds 500
                if (!$grown) { $result.findings += 'RECEIVE: no new rows within 5 seconds of F5.'; break }
                $after = @{}
                foreach ($row in [UiaProbe]::Rows($list)) { $after[$row.Current.AutomationId] = [UiaProbe]::RuntimeId($row) }
                $result.read.rowsAfter = $after.Count
                $kept = 0; $changed = @()
                foreach ($id in $before.Keys) {
                    if (!$after.ContainsKey($id)) { continue }
                    if ($after[$id] -eq $before[$id]) { $kept++ } else { $changed += $id }
                }
                $result.read.kept = $kept
                $result.read.sample = if ($before.Count -gt 0) { $first = @($before.Keys)[0]; "$first $($before[$first]) -> $($after[$first])" } else { $null }
                if ($changed.Count -gt 0) { $result.findings += "RUNTIME_ID: $($changed.Count) rows changed their runtime ID after the refresh, such as $($changed[0])." }
                if ($kept -eq 0) { $result.findings += 'RUNTIME_ID: no row of the first page was still listed to compare.' }
                $old = @($before.Values)
                $reused = @($after.Keys | Where-Object { !$before.ContainsKey($_) -and $old -contains $after[$_] })
                if ($reused.Count -gt 0) { $result.findings += "RUNTIME_ID: new row $($reused[0]) took a runtime ID another row had." }
            }
        }
    }
    catch { $result.findings += "ERROR: $($_.Exception.Message)" }
    finally { Stop-Fixture $run $result }
    return $result
}

$results = @()
foreach ($check in $Checks) {
    Write-Host "UIA probe: $check"
    $result = Test-Check $check
    $status = if ($result.findings.Count -eq 0) { 'PASS' } else { 'FAIL' }
    Write-Host "  $status $(($result.read | ConvertTo-Json -Compress -Depth 4))"
    foreach ($finding in $result.findings) { Write-Host "  - $finding" }
    $results += [pscustomobject]$result
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'results.json') -Encoding utf8

$revision = (git -C $repository rev-parse --short HEAD) 2>$null
if ((git -C $repository status --porcelain) 2>$null) { $revision = "$revision plus uncommitted changes" }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# UI Automation probe, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$lines.Add('')
$lines.Add("- Revision: $revision")
$lines.Add("- Executable: $Executable")
$lines.Add("- Method: published executable, demo fixtures, the UIA client of Windows PowerShell (managed UIA2, and the COM client for IsDataValidForForm, DescribedBy and FullDescription)")
$lines.Add('')
$clean = @($results | Where-Object { $_.findings.Count -eq 0 }).Count
$lines.Add("$clean of $($results.Count) checks pass.")
$lines.Add('')
foreach ($r in $results) {
    $lines.Add("## $($r.check): $(if ($r.findings.Count -eq 0) { 'pass' } else { 'fail' })")
    $lines.Add('')
    foreach ($key in $r.read.Keys) { $lines.Add("- $($key): $(($r.read[$key] | ConvertTo-Json -Compress -Depth 3))") }
    foreach ($f in $r.findings) { $lines.Add("- FINDING $f") }
    $lines.Add('')
}
$lines.Add('A screen reader''s speech is not checked: this reads what one would be given.')
Set-Content -LiteralPath (Join-Path $Output 'summary.md') -Value $lines -Encoding utf8
Write-Host "Summary: $(Join-Path $Output 'summary.md')"
if ($clean -ne $results.Count) { exit 1 }
