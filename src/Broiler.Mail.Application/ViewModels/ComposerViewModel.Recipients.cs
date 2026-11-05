using Broiler.UI.Forms;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;

namespace Broiler.Mail.Application.ViewModels;

public sealed partial class ComposerViewModel
{
    public RecipientBookViewModel? RecipientBook { get; private set; }
    /// <summary>Append one explicitly selected address; preserve all draft identity/content and refuse invalid existing fields.</summary>
    public bool InsertRecipient(string field, SavedRecipient recipient)
    {
        if (!CanEdit || _seed is null || recipient.AccountId != _seed.AccountId || RecipientBook is not { IsReady: true, IsBusy: false } book || !book.Recipients.Contains(recipient)) return false;
        string current = field switch { "To" => To, "Cc" => Cc, "Bcc" => Bcc, _ => throw new ArgumentException("Choose To, Cc or Bcc.") };
        try
        {
            var parsed = MailComposition.ParseRecipients(current);
            if (parsed.Contains(recipient.Address, StringComparer.OrdinalIgnoreCase)) { Status = "That address is already in the selected field."; Notify(); return false; }
            if (parsed.Count >= 100) throw new ArgumentException("Use at most 100 recipients.");
            string updated = current.Trim().Length == 0 ? recipient.Address : current.TrimEnd() + ", " + recipient.Address;
            _ = MailComposition.ParseRecipients(updated);
            Edit(field == "To" ? updated : To, field == "Cc" ? updated : Cc, field == "Bcc" ? updated : Bcc, Subject, PlainText);
            return true;
        }
        catch (ArgumentException error) { Status = error.Message; StatusKind = FeedbackKind.Error; InvalidField = field; Notify(); return false; }
    }
}
