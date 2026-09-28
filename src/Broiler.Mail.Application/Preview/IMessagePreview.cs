using Broiler.Mail.Core.Messages;
using Broiler.UI;

namespace Broiler.Mail.Application.Preview;

/// <summary>Presentation boundary; an HTML implementation must isolate untrusted content.</summary>
public interface IMessagePreview
{
    UiElement CreateContent(MailMessageBody message);
}
