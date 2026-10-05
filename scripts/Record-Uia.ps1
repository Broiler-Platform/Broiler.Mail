<#
.SYNOPSIS
Records the UI Automation events Broiler.Mail raises during a short scripted walk, as a transcript for the
screen-reader pass (H-01).

.DESCRIPTION
Publishes the Windows app as NativeAOT (the shipped configuration) unless -Executable is given, then runs one
or more short walks on demo fixtures and prints, step by step, the UI Automation events a screen reader
would be given:
  - FocusChanged: what gets focus, with its control type, name and help text, 'IsDataValidForForm=False'
    when it reports that, and its FullDescription (a form field's error comes first in it). List rows and
    tab items answer valid (Hosting preview.7), so only a refused field is marked;
  - ElementSelected: the row or tab that became selected;
  - Notification: the text and kind the app announces (status, progress, results);
  - property changes of ExpandCollapseState and ToggleState (managed UIA client) and of IsDataValidForForm.
Focus, selection and IsDataValidForForm are recorded through one UIA COM client, as screen readers listen,
and read from the element each event names. Every line is stamped when its callback is entered, before it
reads any property from the app, and each step's lines are printed in that order, so the transcript shows
the order of FocusChanged and ElementSelected as that client was told.
Walks:
  - inbox (inbox fixture): Tab through the window, select the second row through the managed (UIA2) client,
    select the third through the COM client, move down the list with the arrow key, and receive with F5.
    For a UIA2 Select, UIAutomationCore first calls SetFocus on the row; that focuses the list without
    selecting, so the transcript first names the row selected before. A COM client's Select reaches the app
    as Select alone: ElementSelected on the new row, then one FocusChanged on it (Broiler.Hosting README,
    Focus).
  - composer (draft-invalid fixture, where Check draft has rejected the To address): show and hide Cc and
    Bcc through ExpandCollapse, type into To so the error goes, and run Check draft again so it comes back.
Input is posted to the window's render child or given through UI Automation patterns, so the walk does not
take keyboard focus from other applications, although the window appears briefly. Each demo process this
script starts is closed, or killed if it does not close. The transcript is printed and saved to -Output as
transcript.txt. It records what the app raises, not what a particular screen reader then says.
#>
param(
    [string]$Executable,
    [string]$Output,
    [ValidateSet('inbox', 'composer')]
    [string[]]$Walks = @('inbox', 'composer'),
    [string]$Size = '1100x720',
    [ValidateRange(500, 20000)]
    [int]$SettleMilliseconds = 2500,
    # How long each step waits for its events before the next one.
    [ValidateRange(100, 5000)]
    [int]$StepMilliseconds = 600
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (!$Output) { $Output = Join-Path $repository ("artifacts/uia-record/" + (Get-Date -Format 'yyyy-MM-dd-HHmm')) }
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

// The UIA COM client, declared up to the members the recorder calls; placeholder slots keep the vtable order of
// UIAutomationClient.h. Focus and selection are recorded through it, as a screen reader hears them; IsDataValidForForm
// and FullDescription need it too: the managed client has no identifiers for them.
[ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
public class CUIAutomationRecorderClass { }

[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderCondition { }

[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderAutomation
{
    void CompareElements(); void CompareRuntimeIds(); void GetRootElement();
    IRecorderElement ElementFromHandle(IntPtr hwnd);
    void ElementFromPoint(); void GetFocusedElement(); void GetRootElementBuildCache(); void ElementFromHandleBuildCache();
    void ElementFromPointBuildCache(); void GetFocusedElementBuildCache(); void CreateTreeWalker(); void ControlViewWalker();
    void ContentViewWalker(); void RawViewWalker(); void RawViewCondition(); void ControlViewCondition(); void ContentViewCondition();
    void CreateCacheRequest();
    IRecorderCondition CreateTrueCondition();
    void CreateFalseCondition(); void CreatePropertyCondition();
    void CreatePropertyConditionEx(); void CreateAndCondition(); void CreateAndConditionFromArray(); void CreateAndConditionFromNativeArray();
    void CreateOrCondition(); void CreateOrConditionFromArray(); void CreateOrConditionFromNativeArray(); void CreateNotCondition();
    void AddAutomationEventHandler(int eventId, IRecorderElement element, int scope, IntPtr cacheRequest, IRecorderEventHandler handler);
    void RemoveAutomationEventHandler();
    void AddPropertyChangedEventHandlerNativeArray(IRecorderElement element, int scope, IntPtr cacheRequest, IRecorderPropertyHandler handler,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] int[] properties, int count);
    void AddPropertyChangedEventHandler(); void RemovePropertyChangedEventHandler(); void AddStructureChangedEventHandler();
    void RemoveStructureChangedEventHandler();
    void AddFocusChangedEventHandler(IntPtr cacheRequest, IRecorderFocusHandler handler);
    void RemoveFocusChangedEventHandler();
    void RemoveAllEventHandlers();
}

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderElement
{
    void SetFocus(); void GetRuntimeId(); void FindFirst();
    IRecorderElementArray FindAll(int scope, IRecorderCondition condition);
    void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
    void GetCurrentPropertyValueEx(); void GetCachedPropertyValue(); void GetCachedPropertyValueEx(); void GetCurrentPatternAs();
    void GetCachedPatternAs();
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int patternId);
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderElementArray
{
    int Length { get; }
    IRecorderElement GetElement(int index);
}

[ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderSelectionItem
{
    void Select();
}

[ComImport, Guid("c270f6b5-5c69-4290-9745-7a7f97169468"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderFocusHandler
{
    void HandleFocusChangedEvent(IRecorderElement sender);
}

[ComImport, Guid("146c3c17-f12e-4e22-8c27-f894b9b79c69"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderEventHandler
{
    void HandleAutomationEvent(IRecorderElement sender, int eventId);
}

[ComImport, Guid("40cd37d4-c756-4b0c-8c6f-bddfeeb13b50"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecorderPropertyHandler
{
    void HandlePropertyChangedEvent(IRecorderElement sender, int propertyId, [In, MarshalAs(UnmanagedType.Struct)] object newValue);
}

// Focus and selection come through the same COM client, so their order is the order that client is told, and the
// properties read are the named element's own, not those of whatever has focus by the time a handler would ask.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecorderFocusHandler : IRecorderFocusHandler
{
    public void HandleFocusChangedEvent(IRecorderElement sender) { UiaRecorder.OnFocus(UiaRecorder.Now(), sender); }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecorderEventHandler : IRecorderEventHandler
{
    public void HandleAutomationEvent(IRecorderElement sender, int eventId) { UiaRecorder.OnSelected(UiaRecorder.Now(), sender); }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecorderPropertyHandler : IRecorderPropertyHandler
{
    public void HandlePropertyChangedEvent(IRecorderElement sender, int propertyId, object newValue)
    {
        double at = UiaRecorder.Now();
        string name = "";
        try { name = sender.GetCurrentPropertyValue(30005) as string ?? ""; } catch (COMException) { }
        UiaRecorder.Add(at, "PropertyChanged IsDataValidForForm = " + newValue + " on '" + name + "'");
    }
}

public static class UiaRecorder
{
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

    const int ElementSelectedEventId = 20012, SelectionItemPatternId = 10010, ListItemTypeId = 50007, ListTypeId = 50008;

    sealed class Line { public double At; public long Order; public string Text; }

    static readonly ConcurrentQueue<Line> Lines = new ConcurrentQueue<Line>();
    static readonly Stopwatch Clock = new Stopwatch();
    static long _order;
    static int _processId;
    static IRecorderAutomation _com;
    static RecorderPropertyHandler _comHandler;
    static RecorderFocusHandler _comFocus;
    static RecorderEventHandler _comSelected;

    /// <summary>The time a callback is entered, taken before it reads any property: those are calls into the app.</summary>
    public static double Now() { return Clock.Elapsed.TotalSeconds; }

    public static void Add(double at, string line)
    {
        Lines.Enqueue(new Line { At = at, Order = Interlocked.Increment(ref _order), Text = line });
    }

    /// <summary>The lines recorded since the last call, in the order their callbacks were entered.</summary>
    public static string[] Drain()
    {
        var drained = new List<Line>();
        Line line;
        while (Lines.TryDequeue(out line)) drained.Add(line);
        drained.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Order.CompareTo(b.Order));
        var text = new List<string>();
        foreach (var item in drained) text.Add(string.Format("{0,8:0.0000}s  {1}", item.At, item.Text));
        return text.ToArray();
    }

    public static string Describe(AutomationElement element)
    {
        if (element == null) return "(none)";
        try
        {
            var c = element.Current;
            string text = c.ControlType.ProgrammaticName.Replace("ControlType.", "") + " '" + c.Name + "'";
            if (!string.IsNullOrEmpty(c.HelpText)) text += " help '" + c.HelpText + "'";
            return text;
        }
        catch (ElementNotAvailableException) { return "(gone)"; }
    }

    /// <summary>The control type, name and help text of an element the COM client hands over; null for another process's.</summary>
    static string Describe(IRecorderElement element)
    {
        object process = element.GetCurrentPropertyValue(30002 /* ProcessId */);
        if (!(process is int) || (int)process != _processId) return null;
        object type = element.GetCurrentPropertyValue(30003 /* ControlType */);
        ControlType controlType = type is int ? ControlType.LookupById((int)type) : null;
        string text = (controlType != null ? controlType.ProgrammaticName.Replace("ControlType.", "") : "" + type) +
            " '" + (element.GetCurrentPropertyValue(30005 /* Name */) as string) + "'";
        string help = element.GetCurrentPropertyValue(30013 /* HelpText */) as string;
        if (!string.IsNullOrEmpty(help)) text += " help '" + help + "'";
        return text;
    }

    /// <summary>Runs <paramref name="action"/> on a thread of its own in the multithreaded apartment, where the COM client lives.</summary>
    static void InMta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(failure.Message, failure);
    }

    public static void Start(IntPtr render, int processId)
    {
        _processId = processId;
        Clock.Restart();
        AutomationElement root = AutomationElement.FromHandle(render);
        Automation.AddAutomationEventHandler(AutomationElement.NotificationEvent, root, TreeScope.Subtree, OnNotification);
        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Subtree, OnProperty,
            ExpandCollapsePattern.ExpandCollapseStateProperty, TogglePattern.ToggleStateProperty);
        // The COM client registers from a thread of its own in the multithreaded apartment, so its callbacks need
        // no message loop on this thread.
        try
        {
            InMta(() =>
            {
                _com = (IRecorderAutomation)new CUIAutomationRecorderClass();
                _comFocus = new RecorderFocusHandler();
                _com.AddFocusChangedEventHandler(IntPtr.Zero, _comFocus);
                IRecorderElement window = _com.ElementFromHandle(render);
                _comSelected = new RecorderEventHandler();
                _com.AddAutomationEventHandler(ElementSelectedEventId, window, 7 /* TreeScope_Subtree */, IntPtr.Zero, _comSelected);
                _comHandler = new RecorderPropertyHandler();
                _com.AddPropertyChangedEventHandlerNativeArray(window, 7 /* TreeScope_Subtree */, IntPtr.Zero,
                    _comHandler, new[] { 30103 /* IsDataValidForForm */ }, 1);
            });
        }
        catch (Exception error) { Add(Now(), "(focus, selection and IsDataValidForForm changes are not recorded: " + error.Message + ")"); }
    }

    public static void Stop()
    {
        Automation.RemoveAllEventHandlers();
        var com = _com;
        if (com == null) return;
        try { InMta(() => com.RemoveAllEventHandlers()); } catch (Exception) { }
        _com = null;
    }

    /// <summary>
    /// A focus change, read from the element the COM client hands over: its control type, name and help text, and
    /// " IsDataValidForForm=False" and its FullDescription where it reports them.
    /// </summary>
    public static void OnFocus(double at, IRecorderElement element)
    {
        try
        {
            string text = Describe(element);
            if (text == null) return;
            object valid = element.GetCurrentPropertyValue(30103 /* IsDataValidForForm */);
            if (valid is bool && !(bool)valid) text += " IsDataValidForForm=False";
            string description = element.GetCurrentPropertyValue(30159 /* FullDescription */) as string;
            if (!string.IsNullOrEmpty(description)) text += " description '" + description + "'";
            Add(at, "FocusChanged " + text);
        }
        catch (COMException) { Add(at, "FocusChanged (gone)"); }
    }

    public static void OnSelected(double at, IRecorderElement element)
    {
        try
        {
            string text = Describe(element);
            if (text != null) Add(at, "ElementSelected " + text);
        }
        catch (COMException) { Add(at, "ElementSelected (gone)"); }
    }

    static void OnNotification(object sender, AutomationEventArgs e)
    {
        double at = Now();
        var notification = e as NotificationEventArgs;
        if (notification == null) { Add(at, "Notification from " + Describe(sender as AutomationElement)); return; }
        Add(at, "Notification " + notification.NotificationKind + "/" + notification.NotificationProcessing + " '" + notification.DisplayString +
            "' activity '" + notification.ActivityId + "' from " + Describe(sender as AutomationElement));
    }

    static void OnProperty(object sender, AutomationPropertyChangedEventArgs e)
    {
        double at = Now();
        Add(at, "PropertyChanged " + e.Property.ProgrammaticName.Replace("Pattern.", ".").Replace("Property", "") + " = " + e.NewValue + " on " + Describe(sender as AutomationElement));
    }

    public static AutomationElement Find(IntPtr render, ControlType type, string name)
    {
        return AutomationElement.FromHandle(render).FindFirst(TreeScope.Descendants,
            new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name)));
    }

    public static AutomationElement Row(IntPtr render, int index)
    {
        var list = Find(render, ControlType.List, "Messages");
        if (list == null) return null;
        var rows = list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        return index < rows.Count ? rows[index] : null;
    }

    /// <summary>
    /// Selects row <paramref name="index"/> of the Messages list through the COM client's SelectionItem pattern, the
    /// way a screen reader does: UIA passes it to the app as Select alone.
    /// </summary>
    public static void SelectRowThroughCom(IntPtr render, int index)
    {
        InMta(() =>
        {
            var automation = (IRecorderAutomation)new CUIAutomationRecorderClass();
            IRecorderElementArray all = automation.ElementFromHandle(render).FindAll(4 /* TreeScope_Descendants */, automation.CreateTrueCondition());
            bool inList = false;
            int seen = 0;
            for (int i = 0; i < all.Length; i++)
            {
                IRecorderElement element = all.GetElement(i);
                object type = element.GetCurrentPropertyValue(30003 /* ControlType */);
                if (type is int && (int)type == ListTypeId) { inList = (element.GetCurrentPropertyValue(30005) as string) == "Messages"; continue; }
                if (!inList || !(type is int) || (int)type != ListItemTypeId) continue;
                if (seen++ < index) continue;
                ((IRecorderSelectionItem)element.GetCurrentPattern(SelectionItemPatternId)).Select();
                return;
            }
            throw new InvalidOperationException("The Messages list has no row " + index + ".");
        });
    }

    public static void Key(IntPtr render, int key)
    {
        PostMessage(render, 0x100, (IntPtr)key, IntPtr.Zero);
        PostMessage(render, 0x101, (IntPtr)key, IntPtr.Zero);
    }

    public static void Type(IntPtr render, string text)
    {
        foreach (char c in text) PostMessage(render, 0x102, (IntPtr)c, (IntPtr)1);
    }
}
'@

$transcript = [System.Collections.Generic.List[string]]::new()
function Write-Line([string]$line) { Write-Host $line; $transcript.Add($line) }

function Invoke-Step([string]$description, [scriptblock]$action) {
    Write-Line "-- $description"
    try { & $action } catch { Write-Line "   (step failed: $($_.Exception.Message))" }
    Start-Sleep -Milliseconds $StepMilliseconds
    foreach ($line in [UiaRecorder]::Drain()) { Write-Line "   $line" }
}

function Invoke-Walk([string]$walk) {
    $fixture = if ($walk -eq 'inbox') { 'inbox' } else { 'draft-invalid' }
    $stderr = Join-Path $Output "$walk.err.txt"
    $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Normal -RedirectStandardOutput (Join-Path $Output "$walk.out.txt") `
        -RedirectStandardError $stderr -ArgumentList @('--demo', $fixture, '--theme', 'light', '--size', $Size)
    $null = $process.Handle
    $window = [IntPtr]::Zero
    try {
        for ($i = 0; $i -lt 150 -and $process.MainWindowHandle -eq 0 -and !$process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
        Start-Sleep -Milliseconds $SettleMilliseconds
        $render = [IntPtr]::Zero
        for ($i = 0; $i -lt 50 -and $render -eq [IntPtr]::Zero -and !$process.HasExited; $i++) {
            $process.Refresh()
            $window = $process.MainWindowHandle
            $render = [UiaRecorder]::FindWindowEx($window, [IntPtr]::Zero, 'BroilerGraphicsDirect2DRenderHost', $null)
            if ($render -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
        }
        if ($render -eq [IntPtr]::Zero) { Write-Line "== $walk ($fixture): no window with a render child."; return }
        Write-Line "== $walk walk on the $fixture fixture"
        [UiaRecorder]::Start($render, $process.Id)
        $Edit = [System.Windows.Automation.ControlType]::Edit
        $Button = [System.Windows.Automation.ControlType]::Button
        try {
            if ($walk -eq 'inbox') {
                foreach ($step in 1..5) { Invoke-Step "Tab ($step)" { [UiaRecorder]::Key($render, 0x09) } }
                Invoke-Step 'Select the second row through the managed UIA2 client (SelectionItem.Select)' {
                    $row = [UiaRecorder]::Row($render, 1)
                    ([System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
                }
                Invoke-Step 'Select the third row through the COM client (IUIAutomationSelectionItemPattern.Select)' {
                    [UiaRecorder]::SelectRowThroughCom($render, 2)
                }
                Invoke-Step 'Down arrow in the list' { [UiaRecorder]::Key($render, 0x28) }
                Invoke-Step 'F5 receives' { [UiaRecorder]::Key($render, 0x74) }
                Invoke-Step 'Wait for the receive to finish' { Start-Sleep -Milliseconds 1500 }
            }
            else {
                $toggle = [UiaRecorder]::Find($render, $Button, 'Show Cc and Bcc')
                Invoke-Step 'Show Cc and Bcc (ExpandCollapse.Expand)' {
                    ([System.Windows.Automation.ExpandCollapsePattern]$toggle.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
                }
                Invoke-Step 'Hide Cc and Bcc (ExpandCollapse.Collapse)' {
                    ([System.Windows.Automation.ExpandCollapsePattern]$toggle.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Collapse()
                }
                Invoke-Step 'Focus To (SetFocus) and type a character, which clears its error' {
                    [UiaRecorder]::Find($render, $Edit, 'To').SetFocus()
                    Start-Sleep -Milliseconds 200
                    [UiaRecorder]::Type($render, 'x')
                }
                Invoke-Step 'Check draft (Invoke)' {
                    ([System.Windows.Automation.InvokePattern][UiaRecorder]::Find($render, $Button, 'Check draft').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
                }
                Invoke-Step 'Tab to the next field' { [UiaRecorder]::Key($render, 0x09) }
            }
        }
        finally { [UiaRecorder]::Stop() }
    }
    finally {
        if (!$process.HasExited) {
            $closeTarget = if ($window -ne [IntPtr]::Zero) { $window } else { $process.MainWindowHandle }
            [UiaRecorder]::PostMessage($closeTarget, 0x10, [IntPtr]0, [IntPtr]0) | Out-Null
            if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit(); Write-Line '   (the window did not close within 5 seconds and was ended)' }
        }
        $errors = (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)
        if ($errors -and $errors.Trim().Length -gt 0) { Write-Line "   STDERR: $($errors.Trim())" }
    }
}

foreach ($walk in $Walks) { Invoke-Walk $walk }
$path = Join-Path $Output 'transcript.txt'
Set-Content -LiteralPath $path -Value $transcript -Encoding utf8
Write-Host "Transcript: $path"
