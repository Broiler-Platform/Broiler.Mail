<#
.SYNOPSIS
Checks refresh continuity (UI-02) and the reply focus round trip natively, on the published app.

.DESCRIPTION
Publishes the Windows app as NativeAOT (the shipped configuration) unless -Executable is given, then
runs the new-mail fixture once per variant. Each run scrolls the message list away from the top (part
of a row, so the anchor has an offset inside its row), selects a row through UI Automation, scrolls the
reader, selects reader text, and presses F5. The next receive then does what the variant asks of the
demo server:
  - kept:     newer messages arrive and the open message is marked read on the server. The selected
              row keeps its identity and its place on screen (within 1 px), the first visible row keeps
              its place, the reader keeps its subject, its selected text and its pixels (so its scroll
              offset), and the new rows are listed.
  - vanish:   the open message is deleted on the server. The reader closes and the status explains that
              the message is no longer in the inbox.
  - outside:  a page of new mail pushes the open message below the newest page. It stays open (same
              subject heading) with its text selection, and the status line explains that it is older
              than the newest page and suggests Load older.
  - renumber: the server changes UIDVALIDITY. The reader closes and the status says the inbox was renumbered.
The kept run then clicks Reply with posted mouse input at the button's UI Automation bounds and checks
that the composer's message body has focus. It returns to the inbox twice, once with a posted click on the
Inbox tab and once by selecting the tab through UI Automation, and checks each time that focus returns to
the reader control that had it when composing started.

Before F5 the run also checks that the posted wheel really scrolled the reader, so the scroll check
cannot pass on an unscrolled reader. Every demo body has the same text, so the reader's subject heading
identifies the open message.

Input is posted to the window's render child, so the run does not take keyboard focus from other
applications, although each window appears on screen briefly. Results, before and after screenshots, and
summary.md go to -Output. Server changes come from the demo's acceptance-only --server-change option;
a live IMAP server, real monitor moves, and screen-reader speech are not covered.
#>
param(
    [string]$Executable,
    [string]$Output,
    [ValidateSet('kept', 'vanish', 'outside', 'renumber')]
    [string[]]$Variants = @('kept', 'vanish', 'outside', 'renumber'),
    [string]$Size = '1100x720',
    [ValidateSet('light', 'dark')]
    [string]$Theme = 'light',
    [ValidateRange(500, 20000)]
    [int]$SettleMilliseconds = 2500
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (!$Output) { $Output = Join-Path $repository ("artifacts/acceptance-refresh/" + (Get-Date -Format 'yyyy-MM-dd-HHmm')) }
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

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

public sealed class RefreshRow
{
    public string Id;
    public string Name;
    public double Top;
    public bool Selected;
    public bool Offscreen;
}

public static class RefreshCheck
{
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    public struct RECT { public int L, T, R, B; }
    public struct POINT { public int X, Y; }

    const uint WmMouseWheel = 0x020A, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmKeyDown = 0x0100, WmKeyUp = 0x0101;

    /// <summary>Depth-first over the raw view, so every list row is reached, not only the visible ones.</summary>
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

    public static AutomationElement WithClass(List<AutomationElement> all, string className)
    {
        foreach (var element in all)
        {
            try { if (element.Current.ClassName == className) return element; }
            catch (ElementNotAvailableException) { }
        }
        return null;
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

    /// <summary>The first text element whose name contains the fragment, such as the status line; buttons never match.</summary>
    public static string TextContaining(List<AutomationElement> all, string fragment)
    {
        foreach (var element in all)
        {
            try
            {
                var c = element.Current;
                if (c.ControlType == ControlType.Text && c.Name != null && c.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0) return c.Name;
            }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }

    /// <summary>The first text element after the element named <paramref name="container"/>, such as the reader's subject heading.</summary>
    public static string TextAfter(List<AutomationElement> all, string container)
    {
        bool inside = false;
        foreach (var element in all)
        {
            try
            {
                var c = element.Current;
                if (!inside) { inside = c.Name == container; continue; }
                if (c.ControlType == ControlType.Text) return c.Name;
            }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }

    public static List<RefreshRow> Rows(AutomationElement list)
    {
        var rows = new List<RefreshRow>();
        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(list); child != null; child = walker.GetNextSibling(child))
        {
            var c = child.Current;
            if (c.ControlType != ControlType.ListItem) continue;
            object pattern;
            bool selected = child.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern) && ((SelectionItemPattern)pattern).Current.IsSelected;
            rows.Add(new RefreshRow { Id = c.AutomationId, Name = c.Name ?? "", Top = c.BoundingRectangle.Top, Selected = selected, Offscreen = c.IsOffscreen });
        }
        return rows;
    }

    public static bool SelectRow(AutomationElement list, string id)
    {
        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(list); child != null; child = walker.GetNextSibling(child))
        {
            if (child.Current.AutomationId != id) continue;
            ((SelectionItemPattern)child.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            return true;
        }
        return false;
    }

    public static void SelectTab(AutomationElement tab)
    {
        ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }

    public static string Value(AutomationElement element)
    {
        object pattern;
        return element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) ? ((ValuePattern)pattern).Current.Value : null;
    }

    /// <summary>Selects the first match of <paramref name="text"/> through TextPattern and returns the selection read back.</summary>
    public static string SelectText(AutomationElement element, string text)
    {
        var pattern = (TextPattern)element.GetCurrentPattern(TextPattern.Pattern);
        TextPatternRange range = pattern.DocumentRange.FindText(text, false, false);
        if (range == null) return null;
        range.Select();
        return SelectedText(element);
    }

    public static string SelectedText(AutomationElement element)
    {
        object pattern;
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
        TextPatternRange[] ranges = ((TextPattern)pattern).GetSelection();
        return ranges.Length == 0 ? "" : ranges[0].GetText(-1);
    }

    /// <summary>The deepest element reporting keyboard focus: the window is not in the foreground, so system focus is elsewhere.</summary>
    public static string Focused(IntPtr render)
    {
        var found = AutomationElement.FromHandle(render).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
        if (found.Count == 0) return "(none)";
        var c = found[found.Count - 1].Current;
        return c.ControlType.ProgrammaticName.Replace("ControlType.", "") + " '" + c.Name + "'";
    }

    static IntPtr Point(IntPtr render, double screenX, double screenY, bool client)
    {
        var p = new POINT { X = (int)Math.Round(screenX), Y = (int)Math.Round(screenY) };
        if (client) ScreenToClient(render, ref p);
        return (IntPtr)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
    }

    /// <summary>WM_MOUSEWHEEL carries screen coordinates; a delta of 120 is one notch, and smaller deltas are parts of one.</summary>
    public static void Wheel(IntPtr render, Rect over, int delta)
    {
        PostMessage(render, WmMouseWheel, (IntPtr)((delta & 0xFFFF) << 16), Point(render, over.Left + over.Width / 2, over.Top + over.Height / 2, false));
    }

    /// <summary>A left click at the centre of screen bounds, posted in the render child's client pixels.</summary>
    public static void Press(IntPtr render, Rect bounds) { PostMessage(render, WmLButtonDown, (IntPtr)1, Point(render, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, true)); }
    public static void Release(IntPtr render, Rect bounds) { PostMessage(render, WmLButtonUp, IntPtr.Zero, Point(render, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, true)); }

    public static void Key(IntPtr render, int key)
    {
        PostMessage(render, WmKeyDown, (IntPtr)key, IntPtr.Zero);
        PostMessage(render, WmKeyUp, (IntPtr)key, IntPtr.Zero);
    }

    public static Bitmap Capture(IntPtr window, out RECT bounds)
    {
        GetWindowRect(window, out bounds);
        var bitmap = new Bitmap(Math.Max(1, bounds.R - bounds.L), Math.Max(1, bounds.B - bounds.T));
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr dc = graphics.GetHdc();
            PrintWindow(window, dc, 2);
            graphics.ReleaseHdc(dc);
        }
        return bitmap;
    }

    /// <summary>The share of differing pixels inside a screen rectangle of two captures of the same window; a diff image is saved.</summary>
    public static double Difference(Bitmap before, Bitmap after, RECT window, Rect area, string diffPath)
    {
        // Two pixels in from each edge: the reader can reach the window frame, whose colour follows activation.
        int left = Math.Max(0, (int)Math.Ceiling(area.Left) - window.L + 2), top = Math.Max(0, (int)Math.Ceiling(area.Top) - window.T + 2);
        int right = Math.Min(Math.Min(before.Width, after.Width) - 2, (int)Math.Floor(area.Right) - window.L - 2);
        int bottom = Math.Min(Math.Min(before.Height, after.Height) - 2, (int)Math.Floor(area.Bottom) - window.T - 2);
        if (right <= left || bottom <= top) return 1;
        long changed = 0;
        using (var diff = new Bitmap(right - left, bottom - top))
        {
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    bool same = before.GetPixel(x, y).ToArgb() == after.GetPixel(x, y).ToArgb();
                    if (!same) changed++;
                    diff.SetPixel(x - left, y - top, same ? Color.White : Color.Red);
                }
            diff.Save(diffPath, ImageFormat.Png);
        }
        return changed / (double)((right - left) * (bottom - top));
    }
}
'@

[RefreshCheck]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$readerPrompt = 'Choose a message from the inbox list.'

function Get-State([IntPtr]$render) {
    $all = [RefreshCheck]::All($render)
    $list = [RefreshCheck]::Find($all, [System.Windows.Automation.ControlType]::List, 'Messages')
    $reader = $all | Where-Object { try { $_.Current.Name -eq 'Message text' } catch { $false } } | Select-Object -First 1
    # The message text editor spans its whole document; this is the part of it on screen.
    $readerView = [RefreshCheck]::WithClass($all, 'ScrollableMessageText')
    $rows = if ($list) { @([RefreshCheck]::Rows($list)) } else { @() }
    [pscustomobject]@{
        All = $all; List = $list; Reader = $reader; Rows = $rows
        ListTop = if ($list) { $list.Current.BoundingRectangle.Top } else { 0 }
        ReaderBounds = if ($readerView) { $readerView.Current.BoundingRectangle } else { [System.Windows.Rect]::Empty }
        Value = if ($reader) { [RefreshCheck]::Value($reader) } else { $null }
        Selection = if ($reader) { [RefreshCheck]::SelectedText($reader) } else { $null }
        # The open message's subject heading; every demo body has the same text, so this tells messages apart.
        Heading = [RefreshCheck]::TextAfter($all, 'Message header')
    }
}

# Row names are "Read|Unread <middle dot> subject <em dash> sender"; a refresh may change the read state.
# Windows PowerShell reads this file as ANSI, so the non-ASCII separator is built from its code point.
$dot = [char]0x00B7
$dash = [char]0x2014
function Get-Identity([string]$name) { return ($name -replace "^(Read|Unread) $dot ", '') }
function Get-Subject([string]$name) { return ((Get-Identity $name) -split " $dash ")[0] }

function Wait-For([scriptblock]$condition, [int]$milliseconds = 5000) {
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return [bool](& $condition)
}

function Invoke-Variant([string]$variant) {
    $name = "new-mail-$variant-$Size-$Theme"
    $stdout = Join-Path $Output "$name.out.txt"; $stderr = Join-Path $Output "$name.err.txt"
    $result = [ordered]@{ variant = $variant; size = $Size; theme = $Theme; findings = @(); notes = @(); before = "$name-before.png"; after = "$name-after.png" }
    $arguments = @('--demo', 'new-mail', '--theme', $Theme, '--size', $Size)
    if ($variant -ne 'kept') { $arguments += @('--server-change', $variant) }
    $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Normal -RedirectStandardOutput $stdout -RedirectStandardError $stderr -ArgumentList $arguments
    $null = $process.Handle
    try {
        for ($i = 0; $i -lt 150 -and $process.MainWindowHandle -eq 0 -and !$process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
        if ($process.MainWindowHandle -eq 0) { $result.findings += "START: no window appeared."; return $result }
        Start-Sleep -Milliseconds $SettleMilliseconds
        # The main window handle can briefly be another top-level window; wait for the one with the render child.
        $render = [IntPtr]::Zero
        for ($i = 0; $i -lt 50 -and $render -eq [IntPtr]::Zero; $i++) {
            $process.Refresh()
            $window = $process.MainWindowHandle
            $render = [RefreshCheck]::FindWindowEx($window, [IntPtr]::Zero, 'BroilerGraphicsDirect2DRenderHost', $null)
            if ($render -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
        }
        if ($render -eq [IntPtr]::Zero) { $result.findings += 'START: the window has no render child.'; return $result }
        $result.dpiScale = [RefreshCheck]::GetDpiForWindow($window) / 96.0

        # A loaded machine can still be building the tree; give it up to ten more seconds.
        for ($i = 0; $i -lt 50; $i++) {
            $state = Get-State $render
            if ($state.List -and $state.Reader -and $state.Rows.Count -gt 0) { break }
            Start-Sleep -Milliseconds 200
        }
        if (!$state.List -or !$state.Reader -or $state.Rows.Count -eq 0) { $result.findings += 'TREE: the message list or the reader is missing from UI Automation.'; return $result }
        # Scroll the list away from the top by three and a half rows: one notch, then half of one.
        $listBounds = $state.List.Current.BoundingRectangle
        [RefreshCheck]::Wheel($render, $listBounds, -120); Start-Sleep -Milliseconds 300
        [RefreshCheck]::Wheel($render, $listBounds, -20); Start-Sleep -Milliseconds 300
        $state = Get-State $render
        $visible = @($state.Rows | Where-Object { !$_.Offscreen })
        if ($state.Rows[0].Offscreen -ne $true) { $result.findings += 'SCROLL: the posted wheel did not scroll the list away from the top.'; return $result }
        # A row below the first visible one, so the anchor and the selection are different rows.
        $chosen = $visible[[Math]::Min(3, $visible.Count - 1)]
        if (![RefreshCheck]::SelectRow($state.List, $chosen.Id)) { $result.findings += "SELECT: row $($chosen.Id) could not be selected."; return $result }
        $opened = Wait-For { $s = Get-State $render; $s.Value -and $s.Value -like 'Welcome to Broiler.Mail*' -and $s.Heading -eq (Get-Subject $chosen.Name) -and (@($s.Rows | Where-Object { $_.Selected })[0].Id -eq $chosen.Id) }
        if (!$opened) { $result.findings += "SELECT: row $($chosen.Id) did not open in the reader within 5 seconds."; return $result }
        Start-Sleep -Milliseconds 500
        # Scroll the reader and select text in it, as someone reading would. A reader that did not scroll
        # would make the scroll check after F5 prove nothing.
        $state = Get-State $render
        $unscrolledImage = [RefreshCheck+RECT]::new(); $unscrolled = [RefreshCheck]::Capture($window, [ref]$unscrolledImage)
        [RefreshCheck]::Wheel($render, $state.ReaderBounds, -360); Start-Sleep -Milliseconds 300
        $scrolledImage = [RefreshCheck+RECT]::new(); $scrolled = [RefreshCheck]::Capture($window, [ref]$scrolledImage)
        $wheelDiff = [RefreshCheck]::Difference($unscrolled, $scrolled, $unscrolledImage, $state.ReaderBounds, (Join-Path $Output "$name-reader-wheel-diff.png"))
        $unscrolled.Dispose(); $scrolled.Dispose()
        $result.readerPixelsScrolled = [Math]::Round($wheelDiff * 100, 3)
        if ($wheelDiff -lt 0.01) { $result.findings += "READER_WHEEL: the posted wheel changed only $($result.readerPixelsScrolled) % of the reader, so it did not scroll and the scroll check cannot hold." }
        $selected = [RefreshCheck]::SelectText($state.Reader, 'Paragraph 12:')
        if ($selected -ne 'Paragraph 12:') { $result.findings += "TEXT: selecting reader text through TextPattern returned '$selected'." }
        Start-Sleep -Milliseconds 500

        $before = Get-State $render
        $beforeSelected = @($before.Rows | Where-Object { $_.Selected })[0]
        $beforeAnchor = @($before.Rows | Where-Object { !$_.Offscreen })[0]
        $beforeImage = [RefreshCheck+RECT]::new(); $beforeBitmap = [RefreshCheck]::Capture($window, [ref]$beforeImage)
        $beforeBitmap.Save((Join-Path $Output $result.before), [System.Drawing.Imaging.ImageFormat]::Png)
        $result.selected = [ordered]@{ id = $beforeSelected.Id; name = $beforeSelected.Name; top = $beforeSelected.Top; heading = $before.Heading }
        $result.anchor = [ordered]@{ id = $beforeAnchor.Id; top = $beforeAnchor.Top - $before.ListTop }
        $beforeIds = @($before.Rows | ForEach-Object { $_.Id })

        [RefreshCheck]::Key($render, 0x74) # F5 receives.
        $changed = Wait-For { $ids = @((Get-State $render).Rows | ForEach-Object { $_.Id }); ($ids -join ',') -ne ($beforeIds -join ',') }
        if (!$changed) { $result.findings += 'RECEIVE: the list did not change within 5 seconds of F5.' }
        Start-Sleep -Milliseconds 700

        $after = Get-State $render
        $afterImage = [RefreshCheck+RECT]::new(); $afterBitmap = [RefreshCheck]::Capture($window, [ref]$afterImage)
        $afterBitmap.Save((Join-Path $Output $result.after), [System.Drawing.Imaging.ImageFormat]::Png)
        $afterSelected = @($after.Rows | Where-Object { $_.Id -eq $beforeSelected.Id })
        $afterAnchor = @($after.Rows | Where-Object { $_.Id -eq $beforeAnchor.Id })
        $added = @($after.Rows | Where-Object { $beforeIds -notcontains $_.Id })
        $result.added = $added.Count
        switch ($variant) {
            'kept' {
                if ($afterSelected.Count -ne 1 -or !$afterSelected[0].Selected) { $result.findings += "SELECTION: row $($beforeSelected.Id) is no longer the selected row." }
                else {
                    $row = $afterSelected[0]
                    $result.selected.after = [ordered]@{ name = $row.Name; top = $row.Top }
                    if ((Get-Identity $row.Name) -ne (Get-Identity $beforeSelected.Name)) { $result.findings += "IDENTITY: the selected row became '$($row.Name)'." }
                    if ([Math]::Abs($row.Top - $beforeSelected.Top) -gt 1) { $result.findings += "ANCHOR: the selected row moved from $($beforeSelected.Top) to $($row.Top) px." }
                    if ($row.Name -notlike "Read $dot *") { $result.findings += "READ_STATE: the server read the open message, but its row says '$($row.Name)'." }
                }
                if ($afterAnchor.Count -ne 1 -or [Math]::Abs(($afterAnchor[0].Top - $after.ListTop) - $result.anchor.top) -gt 1) { $result.findings += "ANCHOR: the first visible row $($beforeAnchor.Id) did not keep its place." }
                if ($added.Count -lt 1) { $result.findings += 'NEW_ROWS: no new rows were listed.' }
                elseif (@($added | Where-Object { !$_.Offscreen }).Count -gt 0) { $result.findings += 'NEW_ROWS: new rows appeared inside the scrolled view instead of above it.' }
                if ($after.Value -ne $before.Value -or $after.Heading -ne $before.Heading) { $result.findings += "READER: the reader changed from '$($before.Heading)' to '$($after.Heading)', or its text changed." }
                if ($after.Selection -ne $before.Selection) { $result.findings += "READER_SELECTION: the selected reader text changed from '$($before.Selection)' to '$($after.Selection)'." }
                $diff = [RefreshCheck]::Difference($beforeBitmap, $afterBitmap, $beforeImage, $before.ReaderBounds, (Join-Path $Output "$name-reader-diff.png"))
                $result.readerPixelsChanged = [Math]::Round($diff * 100, 3)
                if ($diff -gt 0.001) { $result.findings += "READER_SCROLL: $($result.readerPixelsChanged) % of the reader text area changed, so its scroll position did not hold." }
            }
            'vanish' {
                if ($afterSelected.Count -ne 0) { $result.findings += "VANISH: the deleted message $($beforeSelected.Id) is still listed." }
                if ($after.Value -ne $readerPrompt) { $result.findings += "VANISH: the reader still shows text instead of '$readerPrompt'." }
                $result.status = [RefreshCheck]::TextContaining($after.All, 'no longer in the inbox')
                if (!$result.status) { $result.findings += 'VANISH: no status explains that the message is no longer in the inbox.' }
            }
            'outside' {
                # The subject heading tells the open message apart; every demo body has the same text.
                if ($after.Heading -ne $before.Heading -or $after.Heading -ne (Get-Subject $beforeSelected.Name) -or $after.Value -ne $before.Value) {
                    $result.findings += "OUTSIDE: the open message '$($before.Heading)' did not stay open; the reader shows '$($after.Heading)'."
                }
                if ($after.Selection -ne $before.Selection) { $result.findings += 'OUTSIDE: the selected reader text changed.' }
                # The status sentence itself: the toolbar's Load older button is always in the tree.
                $result.status = [RefreshCheck]::TextContaining($after.All, 'older than the newest page')
                if (!$result.status -or $result.status -notlike '*Load older*') { $result.findings += 'OUTSIDE: no status explains that the message is older than the newest page and suggests Load older.' }
            }
            'renumber' {
                if ($after.Value -ne $readerPrompt) { $result.findings += "RENUMBER: the reader still shows text instead of '$readerPrompt'." }
                $result.status = [RefreshCheck]::TextContaining($after.All, 'renumbered')
                if (!$result.status) { $result.findings += 'RENUMBER: no status says the inbox was renumbered.' }
            }
        }
        $beforeBitmap.Dispose(); $afterBitmap.Dispose()

        if ($variant -eq 'kept') { Test-Reply $render $window $name $after.All $result }
    }
    finally {
        if (!$process.HasExited) {
            [RefreshCheck]::PostMessage($process.MainWindowHandle, 0x10, [IntPtr]0, [IntPtr]0) | Out-Null
            if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit(); $result.findings += 'CLOSE: the window did not close within 5 seconds.' }
            elseif ($process.ExitCode -ne 0) { $result.findings += "EXIT: exit code $($process.ExitCode)." }
        }
        $errors = (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)
        if ($errors -and $errors.Trim().Length -gt 0) { $result.findings += "STDERR: $($errors.Trim())" }
    }
    return $result
}

# Reply with a posted click at the button's UI Automation bounds: focus must land in the composer's body.
# Back in the inbox, focus must return to the control that had it when composing started. The first round
# trip returns with a posted click on the Inbox tab; the second selects the tab through UI Automation.
function Test-Reply([IntPtr]$render, [IntPtr]$window, [string]$name, $all, $result) {
    $reply = [RefreshCheck]::Find($all, [System.Windows.Automation.ControlType]::Button, 'Reply')
    $inboxTab = [RefreshCheck]::Find($all, [System.Windows.Automation.ControlType]::TabItem, 'Inbox')
    if (!$reply -or !$inboxTab) { $result.findings += 'REPLY: the reader has no Reply button or the window no Inbox tab.'; return }
    $replyBounds = $reply.Current.BoundingRectangle
    # UI Automation splits the tab strip evenly; Inbox is the first tab, so its header starts at the strip's left edge.
    $tabBounds = $inboxTab.Current.BoundingRectangle
    $inboxHeader = New-Object System.Windows.Rect ($tabBounds.Left + 8), ($tabBounds.Top + 4), 12, ($tabBounds.Height - 8)
    # For summary.md: the reported tab item bounds are an equal share of the strip, not the visible header.
    $strip = [RefreshCheck]::Find($all, [System.Windows.Automation.ControlType]::Tab, 'Inbox')
    $tabCount = @($all | Where-Object { try { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::TabItem } catch { $false } }).Count
    $result.tabItemBounds = [ordered]@{ inbox = $tabBounds.Width; strip = if ($strip) { $strip.Current.BoundingRectangle.Width } else { $null }; tabs = $tabCount }
    $result.focusBeforeReply = [RefreshCheck]::Focused($render)
    foreach ($round in @('click', 'automation')) {
        [RefreshCheck]::Press($render, $replyBounds); Start-Sleep -Milliseconds 200
        # The control that has focus when composing starts is the one the inbox returns to.
        $atReply = [RefreshCheck]::Focused($render)
        [RefreshCheck]::Release($render, $replyBounds); Start-Sleep -Milliseconds 700
        $inComposer = [RefreshCheck]::Focused($render)
        if ($inComposer -notmatch "^(Edit|Document) 'Message'$") { $result.findings += "REPLY_FOCUS: after Reply ($round round), focus is on $inComposer, not the message body." }
        if ($round -eq 'click') {
            [RefreshCheck]::Press($render, $inboxHeader); Start-Sleep -Milliseconds 100
            [RefreshCheck]::Release($render, $inboxHeader); Start-Sleep -Milliseconds 500
        }
        else {
            $tab = [RefreshCheck]::Find([RefreshCheck]::All($render), [System.Windows.Automation.ControlType]::TabItem, 'Inbox')
            [RefreshCheck]::SelectTab($tab); Start-Sleep -Milliseconds 500
        }
        $returned = [RefreshCheck]::Focused($render)
        $result."focusAtReply_$round" = $atReply
        $result."focusInComposer_$round" = $inComposer
        $result."focusAfterReturn_$round" = $returned
        if ($returned -ne $atReply) {
            $how = if ($round -eq 'click') { 'a click on the Inbox tab' } else { 'selecting the Inbox tab through UI Automation' }
            $result.findings += "RETURN_FOCUS_$($round.ToUpperInvariant()): after $how, focus is on $returned, not $atReply."
        }
        if ($round -eq 'click') {
            Save-Png $window (Join-Path $Output "$name-returned.png")
            $result.returned = "$name-returned.png"
        }
    }
}

function Save-Png([IntPtr]$window, [string]$path) {
    $bounds = [RefreshCheck+RECT]::new()
    $bitmap = [RefreshCheck]::Capture($window, [ref]$bounds)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
}

$results = @()
foreach ($variant in $Variants) {
    Write-Host "new-mail $variant $Size $Theme"
    $results += [pscustomobject](Invoke-Variant $variant)
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
$lines.Add("# Refresh continuity run, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$lines.Add('')
$lines.Add("- Revision: $revision")
$lines.Add("- Packages: $packages")
$lines.Add("- Executable: $Executable")
$lines.Add("- SDK: $(dotnet --version); OS: $($os.Caption) $($os.Version); architecture: $env:PROCESSOR_ARCHITECTURE")
$lines.Add("- Display scale: $scales (the system's own; not simulated)")
$lines.Add("- Method: published executable, new-mail fixture with --server-change, posted wheel, F5 and click input, UI Automation selection and TextPattern, PrintWindow screenshots")
$lines.Add('')
$clean = @($results | Where-Object { $_.findings.Count -eq 0 }).Count
$lines.Add("$clean of $($results.Count) runs have no automated findings. Screenshots still need visual review.")
$lines.Add('')
$lines.Add('| Variant | Size | Theme | Rows added | Selected row | Reader pixels changed | Status | Findings | Screenshots |')
$lines.Add('| --- | --- | --- | ---: | --- | ---: | --- | --- | --- |')
foreach ($r in $results) {
    $summary = if ($r.findings.Count -eq 0) { 'none' } else { ($r.findings | ForEach-Object { ($_ -split ':')[0] } | Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ', ' }
    $selectedRow = if ($r.selected) { "$($r.selected.id)" } else { '-' }
    $pixels = if ($null -ne $r.readerPixelsChanged) { "$($r.readerPixelsChanged) % (wheel moved $($r.readerPixelsScrolled) %)" } else { '-' }
    $status = if ($r.status) { $r.status } else { '-' }
    $shots = "[before]($($r.before)), [after]($($r.after))"
    if ($r.returned) { $shots += ", [after Reply and back]($($r.returned))" }
    $lines.Add("| $($r.variant) | $($r.size) | $($r.theme) | $($r.added) | $selectedRow | $pixels | $status | $summary | $shots |")
}
foreach ($r in $results | Where-Object { $_.focusInComposer_click }) {
    $lines.Add('')
    $lines.Add("Reply round trips ($($r.variant)): focus before the first click was $($r.focusBeforeReply).")
    foreach ($round in @('click', 'automation')) {
        $back = if ($round -eq 'click') { 'a click on the Inbox tab' } else { 'UI Automation selecting the Inbox tab' }
        $lines.Add("- Reply, then $($back): when composing started $($r."focusAtReply_$round"); in Compose $($r."focusInComposer_$round"); back in Inbox $($r."focusAfterReturn_$round").")
    }
}
foreach ($r in $results | Where-Object { $_.tabItemBounds }) {
    $lines.Add('')
    $lines.Add("Tab item bounds: UI Automation reports the Inbox tab item as $($r.tabItemBounds.inbox) px wide, an equal share of the $($r.tabItemBounds.strip) px tab strip for $($r.tabItemBounds.tabs) tabs, not the visible header. The click round therefore clicks 8 px in from the strip's left edge. Screen readers and Magnifier highlight the same wrong rectangle (Broiler.Hosting tab item peer; UI-09).")
}
$lines.Add('')
$lines.Add('## Findings')
$lines.Add('')
foreach ($r in $results | Where-Object { $_.findings.Count -gt 0 }) {
    $lines.Add("### $($r.variant) $($r.size) $($r.theme)")
    foreach ($f in $r.findings) { $lines.Add("- $f") }
    $lines.Add('')
}
if (@($results | Where-Object { $_.findings -match '^RETURN_FOCUS_AUTOMATION' }).Count -gt 0) {
    $lines.Add('### Known cause of RETURN_FOCUS_AUTOMATION')
    $lines.Add('')
    $lines.Add('Checked with a debugger on 4 October 2026: for SelectionItemPattern.Select, UI Automation first calls the tab item provider''s SetFocus, then Select. Broiler.Hosting''s tab item SetFocus selects the tab, which lets Mail restore the reader control, and then focuses the tab view. A pointer click does not take that path. The return-focus acceptance therefore holds for pointer input only; the UI Automation path stays open for UI-09/H-01.')
    $lines.Add('')
}
$lines.Add('## Not covered by this run')
$lines.Add('')
$lines.Add('- A live IMAP server (the demo server simulates the changes) and changes that arrive while a receive is running.')
$lines.Add('- Other display scales, monitor moves, and DPI changes during a refresh.')
$lines.Add('- Physical mouse, precision touchpad, and keyboard input (input is posted to the window).')
$lines.Add('- A screen reader announcing the new rows or the closed message (UI Automation structure is checked, speech is not).')
$summaryPath = Join-Path $Output 'summary.md'
Set-Content -LiteralPath $summaryPath -Value $lines -Encoding utf8
Write-Host "Summary: $summaryPath"
