// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   10
// Annotated:        2/10
// Exempt:           3
// Human-reviewed:   0/10
// IP risk:          Low
// Security risk:    High
// Criteria:         2/2
// Resource impact:  8/10 max
// Unverified:       10
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Label;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Preview;

/// <summary>
/// The reader's HTML preview action. It follows the preview window of its own message: Open while
/// none is shown, Close while it is open, and the window's outcome as status. Changes for any other
/// message are ignored, so a late event cannot relabel the reader after the selection changed.
/// </summary>
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=AFC62E
// Broiler-Falsified-If: an exception thrown by host.ShowAsync escapes the async click handler instead of becoming the HTML preview unavailable status
// Broiler-Human:        PENDING
public sealed class HtmlMessagePreview(IHtmlPreviewHost host) : IMessagePreview
{
    public const string OpenText = "Open HTML preview";
    public const string CloseText = "Close HTML preview";
    public const string OpeningText = "Opening HTML preview…";

    // Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=8; Fingerprint=F6DEBF
    // Broiler-Falsified-If: an exception thrown by host.ShowAsync escapes the async click handler instead of becoming the HTML preview unavailable status
    // Broiler-Human:        PENDING
    public UiElement CreateContent(MailMessageBody message)
    {
        bool available = message.HtmlText is not null;
        var panel = new StandardPanel { Spacing = 4 };
        var button = new StandardButton { Text = OpenText, IsEnabled = available };
        var status = new StandardLabel
        {
            Text = message.HtmlUnavailableReason ?? "HTML preview blocks images and active content.",
            UseMnemonic = false, Wrapping = UiTextWrapping.Wrap,
        };
        panel.AddChild(button); panel.AddChild(status);
        var root = new PreviewActions(host, panel);
        bool open = false, opening = false;

        void Apply(HtmlPreviewPhase phase, string text)
        {
            if (root.IsDisposed) return;
            opening = false;
            open = phase == HtmlPreviewPhase.Open;
            button.Text = open ? CloseText : OpenText;
            button.IsEnabled = open || available;
            status.Text = text;
        }

        // The reader is rebuilt for each message; a preview of this message may already be open.
        if (host.Current == message.Key)
            Apply(HtmlPreviewPhase.Open, "The HTML preview is open in its own window.");

        root.Subscribe((_, change) =>
        {
            if (change.Message != message.Key) return;
            Post(() => Apply(change.Phase, change.Text));
        });

        button.Clicked += async (_, _) =>
        {
            if (open)
            {
                // The host reports Closed, which relabels the button.
                host.Close();
                return;
            }

            opening = true;
            button.IsEnabled = false;
            status.Text = OpeningText;
            string result;
            try { result = await host.ShowAsync(message); }
            catch (Exception) { result = "HTML preview unavailable. The text preview remains available."; }
            // A phase change for this message may already have arrived; it wins over the opening result.
            Post(() =>
            {
                if (root.IsDisposed || !opening) return;
                opening = false;
                status.Text = result;
                button.IsEnabled = available;
            });
        };
        return root;

        void Post(Action action)
        {
            var dispatcher = root.Session?.Dispatcher;
            try { if (dispatcher is null) action(); else dispatcher.Post(action); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Hosts the action row and stops listening to the preview host when the reader replaces it.</summary>
    private sealed class PreviewActions : UiElement
    {
        private readonly IHtmlPreviewHost _host;
        private readonly UiElement _content;
        private EventHandler<HtmlPreviewChange>? _handler;

        public PreviewActions(IHtmlPreviewHost host, UiElement content)
        {
            _host = host;
            _content = content;
            AddChild(content);
        }

        public void Subscribe(EventHandler<HtmlPreviewChange> handler)
        {
            _handler = handler;
            _host.Changed += handler;
        }

        protected override BSize MeasureCore(BSize availableSize) => _content.Measure(availableSize);

        protected override void Dispose(bool disposing)
        {
            if (disposing && _handler is not null)
            {
                _host.Changed -= _handler;
                _handler = null;
            }

            base.Dispose(disposing);
        }
    }
}
