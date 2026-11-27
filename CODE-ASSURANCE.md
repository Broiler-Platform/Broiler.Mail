# Broiler.Mail Code Assurance

GENERATED - DO NOT EDIT MANUALLY. Regenerate with
`dotnet tool run hairs -- assurance generate --root .`, which rewrites this file,
`HUMAN_REVIEW.md`, `hairs.manifest.json` and every generated source header from the
product tree.

**No code unit in this component carries a decision on its human line yet.** This report
records that absence precisely. It is not a claim that the code is reviewed, assured or safe,
and the figures below are the measurement of how far from that claim the per-unit record is.

## Summary

| Metric | Value |
|---|---:|
| Files scanned | 265 |
| Files not covered | 0 |
| Files carrying an annotation | 66 |
| Code units | 4696 |
| Relevant | 3045 |
| Exempt by predicate | 1651 |
| Annotated | 365 of 3045 (12%) |
| Human reviewed | 0 of 3045 (0%) |
| Unverified | 3045 |

## Review states

| State | Count |
|---|---:|
| NEW | 2680 |
| AI_ASSESSED | 0 |
| HUMAN_PENDING | 365 |
| HUMAN_APPROVED_PENDING_FINGERPRINT | 0 |
| VERIFIED | 0 |
| STALE | 0 |
| EXEMPT | 1651 |

## IP risk

| Value | Units |
|---|---:|
| None | 145 |
| Low | 222 |
| Medium | 0 |
| High | 0 |
| Unknown | 0 |
| *not annotated* | 2680 |

## Security risk

| Value | Units |
|---|---:|
| None | 8 |
| Low | 100 |
| Medium | 94 |
| High | 156 |
| Critical | 9 |
| *not annotated* | 2680 |

## Resource impact

| Metric | Value |
|---|---:|
| Maximum | 8 / 10 |
| Average over annotated units | 2.7 / 10 |
| Units scored | 365 |

## High-security review areas

- `Broiler.Mail.Application.MailApplication` in `src/Broiler.Mail.Application/MailApplication.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.MailApplication.InitializeAsync(CancellationToken)` in `src/Broiler.Mail.Application/MailApplication.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal.IsSaved` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal.Error` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal.Update(DraftSnapshot?, bool)` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal.FlushAsync()` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal.StartWorker()` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.DraftJournal.WriteAsync()` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.MemoryDraftStore` in `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.MemoryDraftStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Persistence.MemoryDraftStore.SaveAsync(long, DraftSnapshot?, CancellationToken)` in `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Preview.IMessagePreview` in `src/Broiler.Mail.Application/Preview/IMessagePreview.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Preview.IMessagePreview.CreateContent(MailMessageBody)` in `src/Broiler.Mail.Application/Preview/IMessagePreview.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountSetupStep` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.SaveAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.SavePasswordAsync(string, CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.SavePasswordAsync(string, MailProtocol, CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.ForgetPasswordAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.ForgetPasswordAsync(MailProtocol, CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.TestConnectionAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.RequireSavedProfile(MailProtocol)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.BuildProfile()` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.RequestSendConfirmation()` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.SendAnywayAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.Start(MailMessageBody?, CompositionKind?)` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.BuildDraft()` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.CheckDraft()` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.SendAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Views.AccountProfileView` in `src/Broiler.Mail.Application/Views/AccountProfileView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Views.AccountProfileView.CreateContent()` in `src/Broiler.Mail.Application/Views/AccountProfileView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Views.ComposerView` in `src/Broiler.Mail.Application/Views/ComposerView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Application.Views.ComposerView.CreateContent()` in `src/Broiler.Mail.Application/Views/ComposerView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Accounts.CredentialKey` in `src/Broiler.Mail.Core/Accounts/CredentialKey.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Accounts.CredentialKey.For(AccountProfile, MailProtocol)` in `src/Broiler.Mail.Core/Accounts/CredentialKey.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.MaximumRecipients` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.MaximumSubjectLength` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.MaximumBodyLength` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.MaximumReferences` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.ValidateDraft(AccountProfile, MailDraft)` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.Create(AccountProfile, MailMessageBody, CompositionKind, string?)` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailComposition.ParseRecipients(string)` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailEmbeddedImage` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailEmbeddedImage.MaximumImageBytes` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailEmbeddedImage.MaximumTotalBytes` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailEmbeddedImage.MaximumImageCount` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Messages.MailMessageKey` in `src/Broiler.Mail.Core/Messages/MailMessageKey.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IAccountStore` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IAccountStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IAccountStore.SaveAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IAccountStore.RemoveAsync(AccountId, CancellationToken)` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ICredentialStore` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ICredentialStore.ReadAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ICredentialStore.WriteAsync(CredentialKey, string, CancellationToken)` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ICredentialStore.DeleteAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IDraftStore` in `src/Broiler.Mail.Core/Services/IDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IDraftStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Core/Services/IDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IDraftStore.SaveAsync(long, DraftSnapshot?, CancellationToken)` in `src/Broiler.Mail.Core/Services/IDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IMailReceiver` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IMailReceiver.TestConnectionAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IMailReceiver.GetInboxAsync(AccountProfile, int, MailInboxCursor?, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IMailReceiver.GetBodyAsync(AccountProfile, MailMessageKey, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IMailSender` in `src/Broiler.Mail.Core/Services/IMailSender.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.IMailSender.SendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailSender.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ISentCopyWriter` in `src/Broiler.Mail.Core/Services/ISentCopyWriter.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ISentCopyWriter.AppendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Core/Services/ISentCopyWriter.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ISettingsStore` in `src/Broiler.Mail.Core/Services/ISettingsStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ISettingsStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Core/Services/ISettingsStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Services.ISettingsStore.SaveAsync(ApplicationSettings, CancellationToken)` in `src/Broiler.Mail.Core/Services/ISettingsStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Validation.ConfigurationValidator` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Validation.ConfigurationValidator.ValidateSenderAliases(AccountProfile)` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Validation.ConfigurationValidator.ValidateServer(MailServerSettings, string)` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, human line PENDING
- `Broiler.Mail.Core.Validation.ConfigurationValidator.RequireText(string?, string, int, string)` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.MaximumPageSize` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.MaximumMessageBytes` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.ImapMailReceiver(ICredentialStore, MutationIntentJournal?)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.ImapMailReceiver(ICredentialStore, RemoteCertificateValidationCallback?, TimeSpan, MutationIntentJournal?)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.TestConnectionAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.WithConnectionAsync<T>(AccountProfile, Func<ImapProtocolClient, CancellationToken, Task<T>>, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.SetReadStateAsync(AccountProfile, MailMessageKey, bool, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.GetBodyAsync(AccountProfile, MailMessageKey, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapSentCopyWriter` in `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapSentCopyWriter.ImapSentCopyWriter(ICredentialStore)` in `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.ImapSentCopyWriter.AppendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.MaximumTextCharacters` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.DecodeAsync(MailMessageKey, Stream, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.CompositionHeaders(ParsedMimeMessage)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.Header(string?, string)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.Clean(string, int, bool)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.ExtractHtmlText(string, CancellationToken, int)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender.SmtpMailSender(ICredentialStore)` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender.SmtpMailSender(ICredentialStore, RemoteCertificateValidationCallback?, TimeSpan)` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender.SendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.ConfigurationJsonContext` in `src/Broiler.Mail.Infrastructure/Persistence/ConfigurationJsonContext.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.SaveAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.RemoveAsync(AccountId, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.ValidateAccounts(AccountProfile[])` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile<T>` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.ReadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.UpdateAsync(Func<T, T>, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.AcquireWriteLockAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore.SaveAsync(long, DraftSnapshot?, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore.Validate(DraftStoreState)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore` in `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore.SaveAsync(ApplicationSettings, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.MaximumHtmlCharacters` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.AllowedTags` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.SuppressedTags` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.Create(string)` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.Create(string, IReadOnlyDictionary<string, MailEmbeddedImage>?, bool, bool, bool)` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.TryExternalLink(string?, out Uri?)` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.CompositionRoot` in `src/Broiler.Mail.Windows/CompositionRoot.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.CompositionRoot.DefaultDataDirectory` in `src/Broiler.Mail.Windows/CompositionRoot.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.CompositionRoot.CreateApplication(string?)` in `src/Broiler.Mail.Windows/CompositionRoot.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsMailWindow` in `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsMailWindow.RunCore()` in `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsMailWindow.OnNativeWindowMessage(nint, uint, nint, nint)` in `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsUiHost` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsUiHost.TryGetText(out string)` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsUiHost.SetText(string)` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Hosting.WindowsUiHost.PublishCaret(UiTextCaretInfo)` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.ImageHttpClient` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.HtmlPreviewWindow()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.HtmlPreviewWindow(HtmlPreviewDocument, string, Action<Uri>, string?, IReadOnlyDictionary<string, MailEmbeddedImage>?, StandardThemeTokens?, string?, PreviewMeasurement?)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.LoadRemoteImages()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.LoadRemoteImagesAsync()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.OpenLink(string, bool)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.RunCore()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.ScrollableHtmlView` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.ScrollableHtmlView.ScrollableHtmlView(string, Func<IBroilerRenderer?>, Action<string>, Func<double>?, Func<string, bool>?, bool, bool)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.ScrollableHtmlView.UpdateHtml(string, bool)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlViewElement` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlViewElement.CreateIsolatedContainer(string)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlViewElement.HtmlViewElement(string, Func<IBroilerRenderer?>, Action<string>, Func<double>?, Func<string, bool>?, bool)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlViewElement.UpdateHtml(string)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.HtmlViewElement.OnInput(UiInputEvent)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport.Deny(HttpRequestMessage)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport.SendAsync(HttpRequestMessage, RequestContext, CancellationToken)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport.Send(HttpRequestMessage, RequestContext, CancellationToken)` in `src/Broiler.Mail.Windows/Preview/ScrollableHtmlView.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Program` in `src/Broiler.Mail.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Program.Main(string[])` in `src/Broiler.Mail.Windows/Program.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.Generic` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.PersistLocalMachine` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.NotFound` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.MaximumBlobBytes` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.ReadAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.WriteAsync(CredentialKey, string, CancellationToken)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.DeleteAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.Target(CredentialKey)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsTextInput` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, human line PENDING
- `Broiler.Mail.Windows.Services.WindowsTextInput.PublishCaret(UiTextCaretInfo)` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, human line PENDING

## Falsification criteria

| Metric | Value |
|---|---:|
| Units carrying a criterion | 302 |
| Units required to carry one | 165 |
| Required and missing | 0 |

A `// Hairs-Falsified-If:` line states, at the declaration, the observation that would make
the unit wrong. `Security=High` says a unit is risky, which is a set and not a test; the
criterion is the test. It is required where `Security` is `High` or `Critical`, permitted
elsewhere, and `dotnet tool run hairs -- assurance check --root .` names every unit that owes one and carries none.

The line is a comment, so it is outside every fingerprint by construction: rewording a
criterion moves no recorded value here, in a file header or in
`hairs.manifest.json`, and invalidates nothing. That is the intended reading - a
criterion is an instruction to whoever reads the unit, not part of what a review is bound to.

## Exemption

Exemption is decided by one predicate in `CSharpAssuranceScanner`, not per unit, so
that the rule is reviewable in one place rather than in several hundred.

| Case | Units |
|---|---:|
| TrivialPropertyOrAccessor | 431 |
| ParameterAssigningConstructor | 9 |
| TrivialExpressionBodiedMember | 136 |
| CompilerSuppliedRecordOrEnumMember | 152 |
| DelegatingOverrideOrOperator | 2 |
| InsideAssemblyMarker | 0 |
| FieldDeclaringStorage | 707 |
| EnumMemberOfADeclaredVocabulary | 214 |
| DeclaredInSource | 0 |

## Per-unit exemptions

| Metric | Value |
|---|---:|
| Per-unit exemptions | 0 |

A per-unit `EXEMPT=<reason>` line exempts one unit by a reason a human wrote, for what the
predicate cannot see. Nothing mechanical checks that the reason is true, that it describes
the unit it sits on, or that it says anything at all, so every use is counted and named
here.

No unit in this component states a per-unit exemption.

## Files not covered

No file under a covered project's directory, and no file a covered project compiles in
through a `<Compile Include>` it states, is left out of the record.

## Change detection

`hairs.manifest.json` lists **every** code unit in the 4 covered assemblies -
4696 of them, exempt and relevant alike - with the fingerprint of its declaration.
This manifest is a change-detection record, not a review. A unit listed there is watched, not reviewed:
the entry records what the declaration's tokens hashed to when the generator last ran, and
nothing else. What the manifest adds is that a unit the exemption predicate treats as
trivial is no longer invisible: a semantic change to one moves a value in a generated file
the check compares byte for byte. `dotnet tool run hairs -- assurance check --root .` holds the manifest to the tree.

Beside the units it lists **every covered file** - 265 of them - with a
fingerprint over the complete token stream of its compilation unit. A unit entry exists only
for a declaration kind the scanner enumerates, and an enumeration is a whitelist: an
`[assembly: ...]` attribute is a member of nothing and can be in no unit at all.
Nothing in a covered file can change without something moving here, whatever kind of declaration it is. Comments are outside the stream, because a token's
text is its own characters, so the generated header above and the annotation lines below move
no file fingerprint - which is what lets one generation be a fixed point.

## Verification

The generator and the check are one computation: `dotnet tool run hairs -- assurance check --root .` works out what the
generator would write and compares it with the tree byte for byte, so a record edited by
hand, or left behind by code that moved, is reported rather than trusted.

| Mode | Command | Effect |
|---|---|---|
| Generate | `dotnet tool run hairs -- assurance generate --root .` | Fills every `Fingerprint=TBF`, refreshes a decision the code has outrun into `STALE; Previous=...`, rewrites the generated headers, `HUMAN_REVIEW.md`, `hairs.manifest.json` and this file. |
| Check | `dotnet tool run hairs -- assurance check --root .` | Reports every generated artefact that is not byte-identical to what the generator would produce, every relevant unit with no annotation, every annotation this system cannot read, every fingerprint out of date and every unit at the top of the security vocabulary without a criterion. |
| Release | `dotnet tool run hairs -- assurance check --root . --release` | The check, and additionally every relevant unit left in a state that blocks a release. |

The fingerprint is six hex characters - 24 bits - of SHA-256 over the declaration's token
texts, joined by single spaces. Trivia is excluded because a token's text is its own
characters and never the comments or whitespace around it, so `dotnet format` moves no
fingerprint and an annotation is never part of what it describes. The value answers whether a
unit changed since it was reviewed. It is not a collision-free identifier across units and it
is not a cryptographic commitment.
