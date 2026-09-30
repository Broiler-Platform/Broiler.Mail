using Broiler.Mail.Core.Messages;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Label;
using Broiler.UI.Standard;

namespace Broiler.Mail.Application.Preview;

public sealed class HtmlMessagePreview(IHtmlPreviewHost host) : IMessagePreview
{
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
