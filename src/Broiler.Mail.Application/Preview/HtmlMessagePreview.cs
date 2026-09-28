using Broiler.Mail.Core.Messages;
using Broiler.UI;

namespace Broiler.Mail.Application.Preview;

public sealed class HtmlMessagePreview : IMessagePreview
{
    // TODO(v2): Implement only after the isolation/resource-policy gate in docs/roadmap.md.
    public UiElement CreateContent(MailMessageBody message) =>
        throw new NotImplementedException("Isolated HTML preview is not implemented.");
}
