using Broiler.Mail.Core.Messages;

namespace Broiler.Mail.Application.Preview;

/// <summary>Platform adapter. Must enforce renderer isolation and resource policy before displaying mail.</summary>
public interface IHtmlPreviewHost : IDisposable
{
    Task<string> ShowAsync(MailMessageBody message);
    void Close();
}
