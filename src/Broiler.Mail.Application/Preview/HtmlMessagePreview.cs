// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  8/10 max
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Label;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Preview;

// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=AFC62E
// Broiler-Falsified-If: an exception thrown by host.ShowAsync escapes the async click handler instead of becoming the HTML preview unavailable status
// Broiler-Human:        PENDING
public sealed class HtmlMessagePreview(IHtmlPreviewHost host) : IMessagePreview
{
    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=1190B1
    // Broiler-Falsified-If: an exception thrown by host.ShowAsync escapes the async click handler instead of becoming the HTML preview unavailable status
    // Broiler-Human:        PENDING
    public UiElement CreateContent(MailMessageBody message)
    {
        var panel = new StandardPanel { Spacing = 4 };
        var button = new StandardButton { Text = "Open HTML preview", IsEnabled = message.HtmlText is not null };
        var status = new StandardLabel { Text = message.HtmlUnavailableReason ?? "HTML preview blocks images and active content.", Wrapping = UiTextWrapping.Wrap, Foreground = StandardControlPaint.Text };
        panel.AddChild(button); panel.AddChild(status);
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            var dispatcher = panel.Session?.Dispatcher;
            string result;
            try { result = await host.ShowAsync(message); }
            catch (Exception) { result = "HTML preview unavailable. The text preview remains available."; }
            void Complete() { if (!panel.IsDisposed) { status.Text = result; button.IsEnabled = true; } }
            try { if (dispatcher is null) Complete(); else dispatcher.Post(Complete); }
            catch (ObjectDisposedException) { }
        };
        return panel;
    }
}
