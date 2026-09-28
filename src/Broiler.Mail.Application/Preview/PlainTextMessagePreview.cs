using Broiler.Mail.Core.Messages;
using Broiler.UI;

namespace Broiler.Mail.Application.Preview;

public sealed class PlainTextMessagePreview : IMessagePreview
{
    public UiElement CreateContent(MailMessageBody message) => new ScrollableMessageText
    {
        Text = message.PlainText,
    };
}
