# Human Review: Broiler.Mail

GENERATED - DO NOT EDIT MANUALLY. Regenerate with
`dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance generate --root Broiler.Mail`, which rewrites this file,
`CODE-ASSURANCE.md`, `assurance.manifest.json` and every generated source header from the
product tree.

> **Status: PENDING.** Human-reviewed: 0 of 420 relevant units. `dotnet run --project Broiler.Code/src/Broiler.Code.Review.Cli -c Release -- assurance check --root Broiler.Mail --release`
> fails while any relevant unit is without a decision bound to its current fingerprint.

## 1. How To Use This File

Read it; do not edit it. A decision about a code unit is the `// Broiler-Human:` line on that
unit's declaration, and every table below is read out of those lines. There is nothing here
to fill in and nothing here to leave blank.

## 2. How A Review Is Recorded

In one place: the `// Broiler-Human:` line of the assurance annotation that sits on the
declaration being read. Nothing in this file is edited by hand, no second document carries a
per-item checklist, and no list of permitted aliases exists to be added to.

```csharp
// Broiler-AI:           Origin=AI; IP=Low; Security=High; Resources=2; Fingerprint=4A3BFD
// Broiler-Falsified-If: a negative value reaches the running total
// Broiler-Human:        PENDING
```

The last line has four shapes. A human writes three of them; the generator writes the fourth
and may never invent an alias, which the check asserts in both directions.

| Line | Meaning |
|---|---|
| `PENDING` | Nobody has recorded a decision for this unit. The generator leaves it exactly as it stands. |
| `<alias>` | A human states their own alias and leaves the machine field to the generator, which fills it with the declaration's fingerprint at the next run. |
| `<alias>; Fingerprint=<six hex>` | A decision bound to one exact version of one declaration. |
| `STALE; Previous=<alias>@<fingerprint>` | Written by the generator when the code moved after a decision. Only a human clears it, by stating their alias again. |

A human may state their own `IP=`, `Security=` and `Resources=` assessment beside their alias,
which is how a reader disagrees with the machine assessment on the line above: an assessment is
a comment and moves no fingerprint, so there is nowhere else to say it.

**No branch, commit or tag is recorded in this file.** Each decision names the fingerprint of
the declaration it was made against, and the state machine compares that value with the
declaration as it now stands. A commit says a tree moved; a fingerprint says whether this unit
did, which is the narrower and the more useful of the two.

## 3. Summary

| Metric | Value |
|---|---:|
| Files scanned | 71 |
| Code units | 705 |
| Relevant | 420 |
| Exempt | 285 |
| Assessed | 420 of 420 (100%) |
| Human reviewed | 0 of 420 (0%) |
| Unverified | 420 |
| Aliases naming a decision | 0 |

## 4. Review States

One row per state of the machine that reads the two lines. The states are computed from the
annotations and the current fingerprints; nothing stores them.

| State | Units |
|---|---:|
| NEW | 0 |
| AI_ASSESSED | 0 |
| HUMAN_PENDING | 420 |
| HUMAN_APPROVED_PENDING_FINGERPRINT | 0 |
| VERIFIED | 0 |
| STALE | 0 |
| EXEMPT | 285 |

## 5. Aliases In The Tree

No alias appears on a human line anywhere in the product tree. Nobody has recorded a
decision about any unit of this component.

## 6. Coverage By File

One row per covered file, carrying that file's generated header. `Unverified` counts the
relevant units in a state that blocks a release.

| File | Units | Relevant | Exempt | Unverified | IP risk | Security risk | Criteria |
|---|---:|---:|---:|---:|---|---|---:|
| `src/Broiler.Mail.Application/MailApplication.cs` | 16 | 3 | 13 | 3 | Low | High | 3/2 |
| `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` | 16 | 8 | 8 | 8 | Low | High | 7/7 |
| `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` | 6 | 3 | 3 | 3 | Low | High | 3/3 |
| `src/Broiler.Mail.Application/Preview/HtmlMessagePreview.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Application/Preview/IHtmlPreviewHost.cs` | 3 | 3 | 0 | 3 | Low | High | 3/3 |
| `src/Broiler.Mail.Application/Preview/IMessagePreview.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Application/Preview/PlainTextMessagePreview.cs` | 2 | 2 | 0 | 2 | Low | Medium | 2/0 |
| `src/Broiler.Mail.Application/Preview/ScrollableMessageText.cs` | 14 | 9 | 5 | 9 | Low | Medium | 6/0 |
| `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` | 35 | 14 | 21 | 14 | Low | High | 14/9 |
| `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` | 50 | 30 | 20 | 30 | Low | High | 25/5 |
| `src/Broiler.Mail.Application/ViewModels/InboxViewModel.cs` | 28 | 17 | 11 | 17 | Low | Medium | 15/0 |
| `src/Broiler.Mail.Application/ViewModels/MailShellViewModel.cs` | 9 | 3 | 6 | 3 | Low | Low | 2/0 |
| `src/Broiler.Mail.Application/ViewModels/SaveViewModel.cs` | 7 | 5 | 2 | 5 | Low | Medium | 4/0 |
| `src/Broiler.Mail.Application/ViewModels/SettingsViewModel.cs` | 8 | 3 | 5 | 3 | Low | Medium | 2/0 |
| `src/Broiler.Mail.Application/Views/AccountProfileView.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Application/Views/ComposerView.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Application/Views/ConfigurationForm.cs` | 5 | 5 | 0 | 5 | Low | Low | 2/0 |
| `src/Broiler.Mail.Application/Views/InboxView.cs` | 3 | 3 | 0 | 3 | Low | Medium | 3/0 |
| `src/Broiler.Mail.Application/Views/MailKeyboardNavigation.cs` | 6 | 6 | 0 | 6 | Low | Medium | 6/0 |
| `src/Broiler.Mail.Application/Views/MailShellView.cs` | 8 | 5 | 3 | 5 | Low | Medium | 4/0 |
| `src/Broiler.Mail.Application/Views/SettingsView.cs` | 2 | 2 | 0 | 2 | Low | Medium | 2/0 |
| `src/Broiler.Mail.Application/Views/TabContent.cs` | 5 | 4 | 1 | 4 | Low | Low | 2/0 |
| `src/Broiler.Mail.Application/Views/ViewportScrollView.cs` | 11 | 7 | 4 | 7 | Low | Low | 4/0 |
| `src/Broiler.Mail.Core/Accounts/AccountId.cs` | 3 | 2 | 1 | 2 | None | Medium | 2/0 |
| `src/Broiler.Mail.Core/Accounts/AccountProfile.cs` | 9 | 1 | 8 | 1 | None | Medium | 1/0 |
| `src/Broiler.Mail.Core/Accounts/CredentialKey.cs` | 6 | 2 | 4 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Core/Accounts/MailServerSettings.cs` | 15 | 4 | 11 | 4 | None | Medium | 4/0 |
| `src/Broiler.Mail.Core/Accounts/SentCopyMode.cs` | 4 | 1 | 3 | 1 | None | Low | 1/0 |
| `src/Broiler.Mail.Core/Messages/DraftSnapshot.cs` | 22 | 4 | 18 | 4 | None | Medium | 2/0 |
| `src/Broiler.Mail.Core/Messages/MailComposition.cs` | 15 | 12 | 3 | 12 | Low | High | 11/8 |
| `src/Broiler.Mail.Core/Messages/MailCompositionSource.cs` | 9 | 1 | 8 | 1 | None | Medium | 1/0 |
| `src/Broiler.Mail.Core/Messages/MailDraft.cs` | 12 | 1 | 11 | 1 | None | Medium | 1/0 |
| `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` | 4 | 4 | 0 | 4 | None | High | 4/4 |
| `src/Broiler.Mail.Core/Messages/MailInboxPage.cs` | 2 | 2 | 0 | 2 | None | Medium | 1/0 |
| `src/Broiler.Mail.Core/Messages/MailMessageBody.cs` | 7 | 1 | 6 | 1 | None | Medium | 0/0 |
| `src/Broiler.Mail.Core/Messages/MailMessageKey.cs` | 1 | 1 | 0 | 1 | None | High | 1/1 |
| `src/Broiler.Mail.Core/Messages/MailMessageSummary.cs` | 6 | 1 | 5 | 1 | None | Medium | 0/0 |
| `src/Broiler.Mail.Core/Messages/SendResult.cs` | 5 | 2 | 3 | 2 | None | Low | 1/0 |
| `src/Broiler.Mail.Core/Services/IAccountStore.cs` | 4 | 4 | 0 | 4 | Low | High | 4/4 |
| `src/Broiler.Mail.Core/Services/ICredentialStore.cs` | 4 | 4 | 0 | 4 | Low | High | 4/4 |
| `src/Broiler.Mail.Core/Services/IDraftStore.cs` | 5 | 4 | 1 | 4 | Low | High | 3/3 |
| `src/Broiler.Mail.Core/Services/IMailReceiver.cs` | 4 | 4 | 0 | 4 | Low | High | 4/4 |
| `src/Broiler.Mail.Core/Services/IMailSender.cs` | 3 | 2 | 1 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Core/Services/ISentCopyWriter.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Core/Services/ISettingsStore.cs` | 3 | 3 | 0 | 3 | Low | High | 3/3 |
| `src/Broiler.Mail.Core/Services/MailConnectionException.cs` | 1 | 1 | 0 | 1 | None | Low | 1/0 |
| `src/Broiler.Mail.Core/Settings/ApplicationSettings.cs` | 8 | 2 | 6 | 2 | None | Low | 1/0 |
| `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` | 5 | 5 | 0 | 5 | Low | High | 5/4 |
| `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` | 17 | 13 | 4 | 13 | Low | High | 12/11 |
| `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` | 7 | 3 | 4 | 3 | Low | High | 3/3 |
| `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` | 9 | 9 | 0 | 9 | Low | High | 9/9 |
| `src/Broiler.Mail.Infrastructure/Mail/OutgoingMessageFactory.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` | 10 | 6 | 4 | 6 | Low | High | 4/4 |
| `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` | 6 | 5 | 1 | 5 | Low | High | 5/5 |
| `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` | 9 | 6 | 3 | 6 | Low | High | 5/5 |
| `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` | 6 | 4 | 2 | 4 | Low | High | 4/4 |
| `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` | 4 | 3 | 1 | 3 | Low | High | 3/3 |
| `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` | 9 | 9 | 0 | 9 | Low | High | 8/7 |
| `src/Broiler.Mail.Windows/CompositionRoot.cs` | 3 | 3 | 0 | 3 | Low | High | 3/3 |
| `src/Broiler.Mail.Windows/DemoApplication.cs` | 20 | 13 | 7 | 13 | Low | Low | 5/0 |
| `src/Broiler.Mail.Windows/Hosting/ShellSmokeCheck.cs` | 8 | 7 | 1 | 7 | Low | Low | 2/0 |
| `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` | 26 | 18 | 8 | 18 | Low | Critical | 18/3 |
| `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` | 13 | 9 | 4 | 9 | Low | Critical | 5/4 |
| `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` | 9 | 7 | 2 | 7 | Low | Critical | 7/7 |
| `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` | 83 | 48 | 35 | 48 | Low | High | 39/20 |
| `src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs` | 8 | 4 | 4 | 4 | Low | High | 4/4 |
| `src/Broiler.Mail.Windows/Program.cs` | 2 | 2 | 0 | 2 | Low | High | 2/2 |
| `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` | 15 | 15 | 0 | 15 | Low | Critical | 15/15 |
| `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` | 27 | 15 | 12 | 15 | Low | Critical | 15/14 |
| `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` | 9 | 7 | 2 | 7 | Low | High | 6/6 |
| `src/Broiler.Mail.Windows/Services/WindowsTheme.cs` | 2 | 2 | 0 | 2 | Low | Low | 2/0 |

## 7. Decisions Recorded

No unit in this component carries a decision on its human line. Every one of them reads
`PENDING`.

## 8. Decisions The Code Has Outrun

No unit carries a decision that the code has since moved past.

## 9. Where A Decision Is Required First

The units at the top of the security vocabulary, with the observation that would show each
one wrong and the human line it carries. The set is read from the assessments rather than
written out, so a unit that becomes `High` joins it at the next generation.

- `Broiler.Mail.Application.MailApplication` in `src/Broiler.Mail.Application/MailApplication.cs` - Security=High, Spec=ADR-0001, `92648C`, PENDING
  - Falsified if: an unreadable drafts.json leaves DraftLoadError null, so the composer autosaves over the file
- `Broiler.Mail.Application.MailApplication.InitializeAsync(CancellationToken)` in `src/Broiler.Mail.Application/MailApplication.cs` - Security=High, Spec=ADR-0001, `C77BBA`, PENDING
  - Falsified if: an unreadable drafts.json leaves DraftLoadError null, so the composer autosaves over the file
- `Broiler.Mail.Application.Persistence.DraftJournal` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `1817C8`, PENDING
  - Falsified if: an edit made while an older snapshot is being saved is reported as saved although the store holds only the older snapshot
- `Broiler.Mail.Application.Persistence.DraftJournal.IsSaved` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `4ABDDC`, PENDING
  - Falsified if: IsSaved reports true after an Update with save disabled whose snapshot the store has not written
- `Broiler.Mail.Application.Persistence.DraftJournal.Error` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `797535`, PENDING
  - Falsified if: Error still returns the previous failure message after a retry has started and the store accepted the write
- `Broiler.Mail.Application.Persistence.DraftJournal.Update(DraftSnapshot?, bool)` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `CB9BC3`, PENDING
  - Falsified if: an Update arriving while the worker is saving an older snapshot is never written, leaving the store with the older draft
- `Broiler.Mail.Application.Persistence.DraftJournal.FlushAsync()` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `792312`, PENDING
  - Falsified if: FlushAsync returns true while a snapshot passed to Update has not been written by the store
- `Broiler.Mail.Application.Persistence.DraftJournal.StartWorker()` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `447205`, PENDING
  - Falsified if: two WriteAsync loops run at once and the second save fails with a draft conflict against this instance's own write
- `Broiler.Mail.Application.Persistence.DraftJournal.WriteAsync()` in `src/Broiler.Mail.Application/Persistence/DraftJournal.cs` - Security=High, Spec=none cited, `A99010`, PENDING
  - Falsified if: an edit made while SaveAsync is in flight is marked saved because the saved version takes the current version rather than the version that was written
- `Broiler.Mail.Application.Persistence.MemoryDraftStore` in `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` - Security=High, Spec=none cited, `827863`, PENDING
  - Falsified if: two SaveAsync calls made with the same expected revision both succeed, so the second replaces the first draft without a DraftConflictException
- `Broiler.Mail.Application.Persistence.MemoryDraftStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` - Security=High, Spec=none cited, `759050`, PENDING
  - Falsified if: LoadAsync returns a revision number paired with a draft that belongs to a different revision while a SaveAsync runs concurrently
- `Broiler.Mail.Application.Persistence.MemoryDraftStore.SaveAsync(long, DraftSnapshot?, CancellationToken)` in `src/Broiler.Mail.Application/Persistence/MemoryDraftStore.cs` - Security=High, Spec=none cited, `D29B73`, PENDING
  - Falsified if: two SaveAsync calls made with the same expected revision both succeed, so the second replaces the first draft without a DraftConflictException
- `Broiler.Mail.Application.Preview.HtmlMessagePreview` in `src/Broiler.Mail.Application/Preview/HtmlMessagePreview.cs` - Security=High, Spec=none cited, `AFC62E`, PENDING
  - Falsified if: an exception thrown by host.ShowAsync escapes the async click handler instead of becoming the HTML preview unavailable status
- `Broiler.Mail.Application.Preview.HtmlMessagePreview.CreateContent(MailMessageBody)` in `src/Broiler.Mail.Application/Preview/HtmlMessagePreview.cs` - Security=High, Spec=none cited, `1190B1`, PENDING
  - Falsified if: an exception thrown by host.ShowAsync escapes the async click handler instead of becoming the HTML preview unavailable status
- `Broiler.Mail.Application.Preview.IHtmlPreviewHost` in `src/Broiler.Mail.Application/Preview/IHtmlPreviewHost.cs` - Security=High, Spec=ADR-0005, `CEA041`, PENDING
  - Falsified if: a preview opened through ShowAsync fetches a remote image or stylesheet before the user chooses Load remote images
- `Broiler.Mail.Application.Preview.IHtmlPreviewHost.ShowAsync(MailMessageBody)` in `src/Broiler.Mail.Application/Preview/IHtmlPreviewHost.cs` - Security=High, Spec=ADR-0005, `D8A40E`, PENDING
  - Falsified if: a preview opened through ShowAsync fetches a remote image or stylesheet before the user chooses Load remote images
- `Broiler.Mail.Application.Preview.IHtmlPreviewHost.Close()` in `src/Broiler.Mail.Application/Preview/IHtmlPreviewHost.cs` - Security=High, Spec=none cited, `45295B`, PENDING
  - Falsified if: a preview still opening on its own thread when Close is called goes on to show its window
- `Broiler.Mail.Application.Preview.IMessagePreview` in `src/Broiler.Mail.Application/Preview/IMessagePreview.cs` - Security=High, Spec=none cited, `7E65E6`, PENDING
  - Falsified if: an implementation given an HTML message renders its HtmlText in the main window instead of through the isolated preview host
- `Broiler.Mail.Application.Preview.IMessagePreview.CreateContent(MailMessageBody)` in `src/Broiler.Mail.Application/Preview/IMessagePreview.cs` - Security=High, Spec=none cited, `5BDDB4`, PENDING
  - Falsified if: an implementation given an HTML message renders its HtmlText in the main window instead of through the isolated preview host
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `0E66E7`, PENDING
  - Falsified if: a password is written under a credential key for connection details that differ from the saved profile
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.SaveAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=none cited, `87F27B`, PENDING
  - Falsified if: Profile is replaced by field values edited after the store write began rather than by the candidate the store wrote
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.SavePasswordAsync(string, CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `5E5C0D`, PENDING
  - Falsified if: the one-argument SavePasswordAsync stores the secret in the SMTP slot instead of the IMAP slot
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.SavePasswordAsync(string, MailProtocol, CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `14B721`, PENDING
  - Falsified if: a password is written while the form host, port, user name or security differs from the saved profile instead of being refused
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.ForgetPasswordAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `D33B47`, PENDING
  - Falsified if: the one-argument ForgetPasswordAsync deletes the SMTP credential slot instead of the IMAP one
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.ForgetPasswordAsync(MailProtocol, CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `EEDAAC`, PENDING
  - Falsified if: after the saved profile switches that protocol to OAuth2, Forget password is refused and the previously saved secret stays in the credential store
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.TestConnectionAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `FDC9C0`, PENDING
  - Falsified if: a connection test runs while the form holds unsaved connection edits instead of being refused with the save-your-changes message
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.RequireSavedProfile(MailProtocol)` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=ADR-0002, `4CBB0D`, PENDING
  - Falsified if: a form whose host differs from the saved Profile returns Profile, so a password is bound to or tested against details the user has not saved
- `Broiler.Mail.Application.ViewModels.AccountProfileViewModel.BuildProfile()` in `src/Broiler.Mail.Application/ViewModels/AccountProfileViewModel.cs` - Security=High, Spec=none cited, `7565C7`, PENDING
  - Falsified if: a host typed with a port, such as imap.example.com:993, yields a candidate profile without an ArgumentException
- `Broiler.Mail.Application.ViewModels.ComposerViewModel` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, Spec=ADR-0004, `9A6344`, PENDING
  - Falsified if: a draft whose submission ended Accepted or Unknown is handed to the sender a second time
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.Start(MailMessageBody?, CompositionKind?)` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, Spec=none cited, `661BA7`, PENDING
  - Falsified if: a reply whose original sender address has a quoted local part containing a comma becomes two recipients when the joined To text is parsed again
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.BuildDraft()` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, Spec=none cited, `42EE4E`, PENDING
  - Falsified if: a draft started under one account builds after the saved account changed its EmailAddress, keeping the old From address instead of throwing
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.CheckDraft()` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, Spec=none cited, `4313D4`, PENDING
  - Falsified if: an invalid recipient makes CheckDraft throw instead of setting the validation message as the status
- `Broiler.Mail.Application.ViewModels.ComposerViewModel.SendAsync(CancellationToken)` in `src/Broiler.Mail.Application/ViewModels/ComposerViewModel.cs` - Security=High, Spec=ADR-0004, `BEF7BA`, PENDING
  - Falsified if: the sender is invoked before the draft store has written the Sending snapshot
- `Broiler.Mail.Application.Views.AccountProfileView` in `src/Broiler.Mail.Application/Views/AccountProfileView.cs` - Security=High, Spec=none cited, `C09C5B`, PENDING
  - Falsified if: the text of the SMTP password field reaches SavePasswordAsync without MailProtocol.Smtp, so it is stored and later sent as the IMAP password
- `Broiler.Mail.Application.Views.AccountProfileView.CreateContent()` in `src/Broiler.Mail.Application/Views/AccountProfileView.cs` - Security=High, Spec=none cited, `F9436B`, PENDING
  - Falsified if: the text of the SMTP password field reaches SavePasswordAsync without MailProtocol.Smtp, so it is stored and later sent as the IMAP password
- `Broiler.Mail.Application.Views.ComposerView` in `src/Broiler.Mail.Application/Views/ComposerView.cs` - Security=High, Spec=none cited, `3F7FB2`, PENDING
  - Falsified if: text typed in the Bcc field reaches model.Edit as its to or cc argument, so Bcc recipients appear in the sent headers
- `Broiler.Mail.Application.Views.ComposerView.CreateContent()` in `src/Broiler.Mail.Application/Views/ComposerView.cs` - Security=High, Spec=none cited, `31C5F3`, PENDING
  - Falsified if: text typed in the Bcc field reaches model.Edit as its to or cc argument, so Bcc recipients appear in the sent headers
- `Broiler.Mail.Core.Accounts.CredentialKey` in `src/Broiler.Mail.Core/Accounts/CredentialKey.cs` - Security=High, Spec=ADR-0002, `F383DF`, PENDING
  - Falsified if: keys for two accounts, two protocols, or two server settings that differ in anything but host letter case compare equal
- `Broiler.Mail.Core.Accounts.CredentialKey.For(AccountProfile, MailProtocol)` in `src/Broiler.Mail.Core/Accounts/CredentialKey.cs` - Security=High, Spec=ADR-0002, `B8FC89`, PENDING
  - Falsified if: a profile whose server host, port, username, security or authentication changed yields the same Binding as before, so the old secret is released to the new server
- `Broiler.Mail.Core.Messages.MailComposition` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `659CA5`, PENDING
  - Falsified if: a draft whose subject or a References entry contains CR or LF passes ValidateDraft and reaches the outgoing MIME headers
- `Broiler.Mail.Core.Messages.MailComposition.MaximumRecipients` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `35F557`, PENDING
  - Falsified if: a draft naming 101 recipients across To, Cc and Bcc passes ValidateDraft
- `Broiler.Mail.Core.Messages.MailComposition.MaximumSubjectLength` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `4D7692`, PENDING
  - Falsified if: a 999-character subject passes ValidateDraft
- `Broiler.Mail.Core.Messages.MailComposition.MaximumBodyLength` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `82B420`, PENDING
  - Falsified if: a plain-text body of 100,001 characters passes ValidateDraft
- `Broiler.Mail.Core.Messages.MailComposition.MaximumReferences` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `49ECCA`, PENDING
  - Falsified if: a draft carrying 101 References entries passes ValidateDraft
- `Broiler.Mail.Core.Messages.MailComposition.ValidateDraft(AccountProfile, MailDraft)` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `B32F87`, PENDING
  - Falsified if: a draft whose AccountId or FromAddress no longer matches the enabled account passes and is submitted under that account's credential
- `Broiler.Mail.Core.Messages.MailComposition.Create(AccountProfile, MailMessageBody, CompositionKind)` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=RFC-5322 s3.6.4, `0BAEC6`, PENDING
  - Falsified if: a reply-all to a received message that lists the account's own address in To or Cc, in any letter case, puts that address among the new recipients
- `Broiler.Mail.Core.Messages.MailComposition.ParseRecipients(string)` in `src/Broiler.Mail.Core/Messages/MailComposition.cs` - Security=High, Spec=none cited, `2C4912`, PENDING
  - Falsified if: recipient text naming 101 addresses is returned as a list instead of raising ArgumentException
- `Broiler.Mail.Core.Messages.MailEmbeddedImage` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, Spec=ADR-0005, `FE137F`, PENDING
  - Falsified if: the embedded images kept for one message add up to more than 2 MiB of decoded data
- `Broiler.Mail.Core.Messages.MailEmbeddedImage.MaximumImageBytes` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, Spec=ADR-0005, `025FBE`, PENDING
  - Falsified if: an inline image part that decodes to more than 1 MiB is kept among the message's embedded images
- `Broiler.Mail.Core.Messages.MailEmbeddedImage.MaximumTotalBytes` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, Spec=ADR-0005, `C03557`, PENDING
  - Falsified if: a message whose inline images decode to more than 2 MiB in total keeps every one of them
- `Broiler.Mail.Core.Messages.MailEmbeddedImage.MaximumImageCount` in `src/Broiler.Mail.Core/Messages/MailEmbeddedImage.cs` - Security=High, Spec=ADR-0005, `872EE4`, PENDING
  - Falsified if: a message with 17 distinct cid: image parts yields 17 embedded images
- `Broiler.Mail.Core.Messages.MailMessageKey` in `src/Broiler.Mail.Core/Messages/MailMessageKey.cs` - Security=High, Spec=ADR-0003, `4852D3`, PENDING
  - Falsified if: two keys that differ only in AccountId or UidValidity compare equal, so a body fetched for another account or UIDVALIDITY epoch is accepted for the selected message
- `Broiler.Mail.Core.Services.IAccountStore` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, Spec=none cited, `D42B1A`, PENDING
  - Falsified if: an implementation writes a password or other secret into the saved account profile data
- `Broiler.Mail.Core.Services.IAccountStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, Spec=none cited, `D78755`, PENDING
  - Falsified if: a saved accounts file that fails ConfigurationValidator.Validate is returned as profiles instead of raising InvalidDataException
- `Broiler.Mail.Core.Services.IAccountStore.SaveAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, Spec=none cited, `4728EA`, PENDING
  - Falsified if: a save interrupted before the replacement completes leaves the existing accounts file truncated or partly written
- `Broiler.Mail.Core.Services.IAccountStore.RemoveAsync(AccountId, CancellationToken)` in `src/Broiler.Mail.Core/Services/IAccountStore.cs` - Security=High, Spec=none cited, `F15C59`, PENDING
  - Falsified if: removing an account from a corrupt accounts file replaces that file with an empty list instead of refusing
- `Broiler.Mail.Core.Services.ICredentialStore` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, Spec=none cited, `ACF4A7`, PENDING
  - Falsified if: a secret saved for one host, port, user name, security mode or authentication method is returned after any of them changes
- `Broiler.Mail.Core.Services.ICredentialStore.ReadAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, Spec=none cited, `9746DD`, PENDING
  - Falsified if: ReadAsync returns the secret held in the account and protocol slot when the stored binding differs from the key's Binding
- `Broiler.Mail.Core.Services.ICredentialStore.WriteAsync(CredentialKey, string, CancellationToken)` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, Spec=none cited, `A1C5B7`, PENDING
  - Falsified if: after WriteAsync, ReadAsync with the key of the previous connection details still returns the earlier secret
- `Broiler.Mail.Core.Services.ICredentialStore.DeleteAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Core/Services/ICredentialStore.cs` - Security=High, Spec=none cited, `1D0FA6`, PENDING
  - Falsified if: DeleteAsync leaves readable a secret for the same account and protocol that was saved under older connection details
- `Broiler.Mail.Core.Services.IDraftStore` in `src/Broiler.Mail.Core/Services/IDraftStore.cs` - Security=High, Spec=none cited, `D7A595`, PENDING
  - Falsified if: two app instances that save against the same expected revision both succeed, so the later write overwrites the newer draft
- `Broiler.Mail.Core.Services.IDraftStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Core/Services/IDraftStore.cs` - Security=High, Spec=none cited, `D25BF2`, PENDING
  - Falsified if: a saved draft file whose revision is negative is returned as a valid state instead of reported invalid
- `Broiler.Mail.Core.Services.IDraftStore.SaveAsync(long, DraftSnapshot?, CancellationToken)` in `src/Broiler.Mail.Core/Services/IDraftStore.cs` - Security=High, Spec=none cited, `FDDDAB`, PENDING
  - Falsified if: a save whose expectedRevision differs from the stored revision replaces the stored draft instead of raising DraftConflictException
- `Broiler.Mail.Core.Services.IMailReceiver` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, Spec=none cited, `5A7A72`, PENDING
  - Falsified if: an implementation sets the Seen flag on a message while listing the inbox or reading its body
- `Broiler.Mail.Core.Services.IMailReceiver.TestConnectionAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, Spec=none cited, `D97FB4`, PENDING
  - Falsified if: a connection test authenticates over a plaintext connection when the server offers neither implicit TLS nor STARTTLS
- `Broiler.Mail.Core.Services.IMailReceiver.GetInboxAsync(AccountProfile, int, MailInboxCursor?, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, Spec=none cited, `8A157C`, PENDING
  - Falsified if: a continuation cursor issued for one account yields messages when passed with another account's profile
- `Broiler.Mail.Core.Services.IMailReceiver.GetBodyAsync(AccountProfile, MailMessageKey, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailReceiver.cs` - Security=High, Spec=none cited, `D9E021`, PENDING
  - Falsified if: a body is returned for a key whose UIDVALIDITY no longer matches the mailbox's current UIDVALIDITY
- `Broiler.Mail.Core.Services.IMailSender` in `src/Broiler.Mail.Core/Services/IMailSender.cs` - Security=High, Spec=none cited, `140067`, PENDING
  - Falsified if: an implementation submits a draft whose AccountId or FromAddress differs from the account it is sent with
- `Broiler.Mail.Core.Services.IMailSender.SendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Core/Services/IMailSender.cs` - Security=High, Spec=none cited, `F46089`, PENDING
  - Falsified if: a Bcc recipient appears in the header block of the message delivered to the To and Cc recipients
- `Broiler.Mail.Core.Services.ISentCopyWriter` in `src/Broiler.Mail.Core/Services/ISentCopyWriter.cs` - Security=High, Spec=none cited, `A3C8F8`, PENDING
  - Falsified if: an implementation resubmits the message by SMTP or repeats the APPEND after an append whose outcome is unknown
- `Broiler.Mail.Core.Services.ISentCopyWriter.AppendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Core/Services/ISentCopyWriter.cs` - Security=High, Spec=none cited, `5A054F`, PENDING
  - Falsified if: an APPEND whose tagged response was lost is reported as Failed rather than Unknown, inviting a second copy
- `Broiler.Mail.Core.Services.ISettingsStore` in `src/Broiler.Mail.Core/Services/ISettingsStore.cs` - Security=High, Spec=none cited, `DBC5DD`, PENDING
  - Falsified if: a corrupt or future-version settings file is replaced by a save instead of being preserved and reported
- `Broiler.Mail.Core.Services.ISettingsStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Core/Services/ISettingsStore.cs` - Security=High, Spec=none cited, `45B6DC`, PENDING
  - Falsified if: a settings file with an unknown member or an integer theme value is returned instead of reported invalid
- `Broiler.Mail.Core.Services.ISettingsStore.SaveAsync(ApplicationSettings, CancellationToken)` in `src/Broiler.Mail.Core/Services/ISettingsStore.cs` - Security=High, Spec=none cited, `DED35E`, PENDING
  - Falsified if: a save interrupted before the replacement completes leaves the existing settings file truncated or partly written
- `Broiler.Mail.Core.Validation.ConfigurationValidator` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, Spec=none cited, `DCF2A3`, PENDING
  - Falsified if: an undefined TransportSecurity or AuthenticationMethod value in a saved profile passes validation
- `Broiler.Mail.Core.Validation.ConfigurationValidator.Validate(AccountProfile)` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, Spec=none cited, `8FB9B1`, PENDING
  - Falsified if: a profile whose EmailAddress carries a display name, such as Eve <eve@example.com>, passes Validate
- `Broiler.Mail.Core.Validation.ConfigurationValidator.ValidateServer(MailServerSettings)` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, Spec=none cited, `6884FA`, PENDING
  - Falsified if: a host written with a port or scheme, such as mail.example.com:993 or imap://mail.example.com, passes ValidateServer
- `Broiler.Mail.Core.Validation.ConfigurationValidator.RequireText(string?, string, int)` in `src/Broiler.Mail.Core/Validation/ConfigurationValidator.cs` - Security=High, Spec=none cited, `194725`, PENDING
  - Falsified if: a value containing CR or LF within the length limit is accepted without an ArgumentException
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=none cited, `C55898`, PENDING
  - Falsified if: the IMAP password is sent over a connection whose server certificate failed platform validation or that never upgraded to TLS
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.MaximumPageSize` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0003, `431894`, PENDING
  - Falsified if: GetInboxAsync accepts a maximumCount of 51 and fetches more than 50 envelopes in one request
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.MaximumMessageBytes` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0003, `3C945E`, PENDING
  - Falsified if: a message body of more than 2 MiB is transferred in full and passed to the MIME decoder
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.ImapMailReceiver(ICredentialStore)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0002, `8F6932`, PENDING
  - Falsified if: a client made by the factory this constructor installs accepts a server certificate that fails platform chain validation
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.ImapMailReceiver(ICredentialStore, Func<ImapClient>, TimeSpan)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0002, `0F261A`, PENDING
  - Falsified if: code outside the test assembly reaches this constructor with a client factory that replaces certificate validation
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.TestConnectionAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0002, `705856`, PENDING
  - Falsified if: a connection test selects or examines a mailbox, or fetches a message, after authenticating
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.WithConnectionAsync<T>(AccountProfile, Func<ImapClient, CancellationToken, Task<T>>, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0002, `2E32CD`, PENDING
  - Falsified if: an account configured for STARTTLS authenticates in plaintext when the server does not advertise STARTTLS
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.GetInboxAsync(AccountProfile, int, MailInboxCursor?, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0003, `53B5C5`, PENDING
  - Falsified if: a FETCH reply that omits the envelope or UID of a message in the requested range yields a page instead of the inbox-changed error
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.GetBodyAsync(AccountProfile, MailMessageKey, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0003, `AA9481`, PENDING
  - Falsified if: a message larger than 2 MiB reaches MessageTextDecoder.DecodeAsync instead of being refused with the reading-limit error
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.SizeLimitProgress` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0003, `1ED042`, PENDING
  - Falsified if: a server that announces or sends more than 2 MiB plus one byte for the partial body fetch is read on instead of aborted
- `Broiler.Mail.Infrastructure.Mail.ImapMailReceiver.SizeLimitProgress.Report(long, long)` in `src/Broiler.Mail.Infrastructure/Mail/ImapMailReceiver.cs` - Security=High, Spec=ADR-0003, `B96F7A`, PENDING
  - Falsified if: a progress report of 2,097,153 bytes transferred returns instead of throwing the message-too-large error
- `Broiler.Mail.Infrastructure.Mail.ImapSentCopyWriter` in `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` - Security=High, Spec=ADR-0004, `6C5483`, PENDING
  - Falsified if: the IMAP password is sent to the Sent-copy server over a connection whose certificate failed platform validation or that never upgraded to TLS
- `Broiler.Mail.Infrastructure.Mail.ImapSentCopyWriter.ImapSentCopyWriter(ICredentialStore)` in `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` - Security=High, Spec=none cited, `25C087`, PENDING
  - Falsified if: a client made by the factory this constructor installs accepts a server certificate that fails platform chain validation
- `Broiler.Mail.Infrastructure.Mail.ImapSentCopyWriter.AppendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/ImapSentCopyWriter.cs` - Security=High, Spec=ADR-0004, `4B2DD6`, PENDING
  - Falsified if: an APPEND interrupted after it was sent is reported as Failed rather than Unknown
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=ADR-0003, `B447E4`, PENDING
  - Falsified if: a decoded body exceeds one of its caps: more than 32,000 text characters, more than 16 images, an image over 1 MiB or more than 2 MiB of images in total
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.MaximumTextCharacters` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=ADR-0003, `0ECAA8`, PENDING
  - Falsified if: a text body longer than 32,000 characters reaches the reading pane in full instead of being cut at this limit and marked truncated
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.DecodeAsync(MailMessageKey, Stream, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=ADR-0003, `5A61BB`, PENDING
  - Falsified if: a message whose multiparts nest more than 32 levels deep is parsed into entities below that depth instead of stopping at the parser's depth limit
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.SupportedImageTypes` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=ADR-0005, `64E094`, PENDING
  - Falsified if: a part declared as image/svg+xml, or as any type other than PNG, JPEG, GIF or WebP, is extracted as an embedded image
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.ExtractEmbeddedImages(MimeMessage)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=ADR-0005, `7DD7B4`, PENDING
  - Falsified if: the returned dictionary holds more than 16 images, an image larger than 1 MiB, or more than 2 MiB of image data in total
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.CompositionHeaders(MimeMessage)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=none cited, `5BC1DA`, PENDING
  - Falsified if: a received Message-ID, References or In-Reply-To value containing CR or LF is copied into the reply composition source
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.Header(string?, string)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=none cited, `666D03`, PENDING
  - Falsified if: an envelope subject or sender whose 512-character cut falls inside a surrogate pair is returned ending in a lone high surrogate
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.Clean(string, int, bool)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=none cited, `50B579`, PENDING
  - Falsified if: in single-line mode a CR, LF, TAB or NUL from the input survives into the returned string
- `Broiler.Mail.Infrastructure.Mail.MessageTextDecoder.ExtractHtmlText(string, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/MessageTextDecoder.cs` - Security=High, Spec=ADR-0003, `6153AD`, PENDING
  - Falsified if: an HTML-only message that omits the optional </head> end tag yields no readable text because everything after <head> stays suppressed
- `Broiler.Mail.Infrastructure.Mail.OutgoingMessageFactory` in `src/Broiler.Mail.Infrastructure/Mail/OutgoingMessageFactory.cs` - Security=High, Spec=ADR-0004, `3AF06A`, PENDING
  - Falsified if: a message built for SMTP submission carries a Bcc header naming a blind-copy recipient
- `Broiler.Mail.Infrastructure.Mail.OutgoingMessageFactory.Create(AccountProfile, MailDraft, bool)` in `src/Broiler.Mail.Infrastructure/Mail/OutgoingMessageFactory.cs` - Security=High, Spec=ADR-0004, `289988`, PENDING
  - Falsified if: with includeBcc false, an address from the draft's Bcc list appears in any header of the returned message
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, Spec=ADR-0004, `8FC6AB`, PENDING
  - Falsified if: an SMTP server configured for STARTTLS that does not offer it receives the AUTH password over the unencrypted connection
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender.SmtpMailSender(ICredentialStore)` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, Spec=ADR-0004, `B5D1E4`, PENDING
  - Falsified if: the client produced by the public constructor accepts a server certificate that system certificate validation rejects
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender.SmtpMailSender(ICredentialStore, Func<SmtpClient>, TimeSpan)` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, Spec=none cited, `B7C6FC`, PENDING
  - Falsified if: an assembly other than the component's test project can construct the sender with its own client factory or deadline
- `Broiler.Mail.Infrastructure.Mail.SmtpMailSender.SendAsync(AccountProfile, MailDraft, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Mail/SmtpMailSender.cs` - Security=High, Spec=ADR-0004, `86DE37`, PENDING
  - Falsified if: a disconnect, timeout or cancellation after client.SendAsync has begun is reported as Rejected instead of Unknown
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, Spec=ADR-0001, `DFAA50`, PENDING
  - Falsified if: a load or save leaves the accounts file holding more than one profile, or a profile that ConfigurationValidator rejects
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, Spec=ADR-0001, `13CB12`, PENDING
  - Falsified if: a corrupt or oversized accounts file loads as an empty account list instead of throwing InvalidDataException
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.SaveAsync(AccountProfile, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, Spec=ADR-0001, `446616`, PENDING
  - Falsified if: saving a profile whose Id differs from the stored account replaces that account instead of throwing
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.RemoveAsync(AccountId, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, Spec=none cited, `4E1969`, PENDING
  - Falsified if: removing an AccountId that is not stored changes or deletes the stored profile
- `Broiler.Mail.Infrastructure.Persistence.JsonAccountStore.ValidateAccounts(AccountProfile[])` in `src/Broiler.Mail.Infrastructure/Persistence/JsonAccountStore.cs` - Security=High, Spec=ADR-0001, `795E38`, PENDING
  - Falsified if: an accounts file with two profiles, or with a profile that ConfigurationValidator rejects, passes without an exception
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile<T>` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, Spec=ADR-0001, `398D88`, PENDING
  - Falsified if: two overlapping UpdateAsync calls, in one process or two, both apply their change to the same prior state so that one change is lost
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.Options` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, Spec=none cited, `4C6116`, PENDING
  - Falsified if: a configuration file with an unknown property, or an integer where an enum name belongs, deserializes without a JsonException
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.ReadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, Spec=ADR-0001, `7A3C57`, PENDING
  - Falsified if: an existing file that is corrupt, larger than maximumBytes or of another schema version yields the default value instead of an exception
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.UpdateAsync(Func<T, T>, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, Spec=ADR-0001, `421205`, PENDING
  - Falsified if: a failure or cancellation while writing leaves the target file truncated or partly written instead of holding its previous content
- `Broiler.Mail.Infrastructure.Persistence.JsonConfigurationFile.AcquireWriteLockAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonConfigurationFile.cs` - Security=High, Spec=none cited, `E7E027`, PENDING
  - Falsified if: a lock file held by another writer for more than 3 seconds lets the update proceed without the lock instead of throwing
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, Spec=none cited, `07E003`, PENDING
  - Falsified if: a save based on a stale revision overwrites a newer stored draft
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, Spec=none cited, `BC3C29`, PENDING
  - Falsified if: a corrupt or oversized drafts file loads as an empty store (revision 0, no draft) instead of throwing
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore.SaveAsync(long, DraftSnapshot?, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, Spec=none cited, `BBC0FE`, PENDING
  - Falsified if: a save whose expectedRevision differs from the stored revision writes the draft instead of throwing DraftConflictException
- `Broiler.Mail.Infrastructure.Persistence.JsonDraftStore.Validate(DraftStoreState)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonDraftStore.cs` - Security=High, Spec=none cited, `1E1911`, PENDING
  - Falsified if: a saved draft whose Sent-copy state is Pending while its submission state is not Accepted loads without an exception
- `Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore` in `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` - Security=High, Spec=ADR-0001, `801CCD`, PENDING
  - Falsified if: settings that ConfigurationValidator rejects are written to or returned from the settings file
- `Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore.LoadAsync(CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` - Security=High, Spec=ADR-0001, `C2AA06`, PENDING
  - Falsified if: a corrupt or oversized settings file loads as default settings instead of throwing InvalidDataException
- `Broiler.Mail.Infrastructure.Persistence.JsonSettingsStore.SaveAsync(ApplicationSettings, CancellationToken)` in `src/Broiler.Mail.Infrastructure/Persistence/JsonSettingsStore.cs` - Security=High, Spec=ADR-0001, `7B7A4B`, PENDING
  - Falsified if: settings with an undefined theme or a window size outside the validator's range are written to the settings file
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=ADR-0005, `824183`, PENDING
  - Falsified if: an attribute taken from the message other than a validated http or https link or image source, such as style, background or an on* handler, appears in the preview markup
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.MaximumHtmlCharacters` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=none cited, `F7A59A`, PENDING
  - Falsified if: HTML between 128,001 and 1,280,000 characters is previewed although the component tells the user its preview limit is 128,000 characters
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.AllowedTags` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=ADR-0005, `88BD38`, PENDING
  - Falsified if: the set contains a tag that loads a resource, submits a form or embeds active content, such as form, input, link, base, meta, video or object
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.SuppressedTags` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=ADR-0005, `5B8650`, PENDING
  - Falsified if: an <embed> element, which HTML never closes, suppresses every later token so the rest of the message is missing from the preview
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.Create(string)` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=none cited, `B34788`, PENDING
  - Falsified if: the one-argument overload emits an http or https img element instead of the blocked-image placeholder
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.Create(string, IReadOnlyDictionary<string, MailEmbeddedImage>?, bool)` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=ADR-0005, `39183E`, PENDING
  - Falsified if: one cid: image referenced by many img tags is inlined as a full base64 copy each time, so a message within the 2 MiB and 20,000-token limits produces gigabytes of markup
- `Broiler.Mail.Infrastructure.Preview.HtmlPreviewPolicy.TryExternalLink(string?, out Uri?)` in `src/Broiler.Mail.Infrastructure/Preview/HtmlPreviewPolicy.cs` - Security=High, Spec=ADR-0005, `6D2504`, PENDING
  - Falsified if: a value whose scheme is not http or https, or that carries userinfo or a control character, returns true
- `Broiler.Mail.Windows.CompositionRoot` in `src/Broiler.Mail.Windows/CompositionRoot.cs` - Security=High, Spec=none cited, `B958AC`, PENDING
  - Falsified if: the default data directory resolves to a location shared between Windows users instead of the current user's local application data
- `Broiler.Mail.Windows.CompositionRoot.DefaultDataDirectory` in `src/Broiler.Mail.Windows/CompositionRoot.cs` - Security=High, Spec=none cited, `3777B3`, PENDING
  - Falsified if: the returned path lies outside the current user's LocalApplicationData folder
- `Broiler.Mail.Windows.CompositionRoot.CreateApplication(string?)` in `src/Broiler.Mail.Windows/CompositionRoot.cs` - Security=High, Spec=none cited, `B98535`, PENDING
  - Falsified if: the accounts, settings or drafts store is given a file outside the data directory passed in
- `Broiler.Mail.Windows.Hosting.WindowsMailWindow` in `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` - Security=Critical, Spec=none cited, `0A6203`, PENDING
  - Falsified if: a WM_GETMINMAXINFO or WM_DPICHANGED for the render child window reaches WindowsWindowSizing.OnMessage, whose Marshal reads and writes through lParam then act on the wrong window
- `Broiler.Mail.Windows.Hosting.WindowsMailWindow.RunCore()` in `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` - Security=High, Spec=none cited, `7386EC`, PENDING
  - Falsified if: a GetMessage return of -1 is passed to TranslateMessage and DispatchMessage instead of ending the loop with a Win32Exception
- `Broiler.Mail.Windows.Hosting.WindowsMailWindow.OnNativeWindowMessage(nint, uint, nint, nint)` in `src/Broiler.Mail.Windows/Hosting/WindowsMailWindow.cs` - Security=Critical, Spec=none cited, `09320F`, PENDING
  - Falsified if: a WM_GETMINMAXINFO or WM_DPICHANGED addressed to a window other than NativeHandle is handed to WindowsWindowSizing.OnMessage, which then writes through that message's lParam
- `Broiler.Mail.Windows.Hosting.WindowsUiHost` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=Critical, Spec=none cited, `087FCE`, PENDING
  - Falsified if: a paste reads clipboard memory past the size GlobalSize reports for a block another process placed there
- `Broiler.Mail.Windows.Hosting.WindowsUiHost.TryGetText(out string)` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=Critical, Spec=none cited, `E01786`, PENDING
  - Falsified if: PtrToStringUni reads more characters than half the GlobalSize of the locked clipboard block, reading past memory another process allocated
- `Broiler.Mail.Windows.Hosting.WindowsUiHost.SetText(string)` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=Critical, Spec=none cited, `A65525`, PENDING
  - Falsified if: the global block passed to SetClipboardData is freed afterwards by the host although the clipboard now owns it
- `Broiler.Mail.Windows.Hosting.WindowsUiHost.PublishCaret(UiTextCaretInfo)` in `src/Broiler.Mail.Windows/Hosting/WindowsUiHost.cs` - Security=High, Spec=none cited, `80EB2A`, PENDING
  - Falsified if: an IMM input context obtained with ImmGetContext is left unreleased when positioning the composition window returns early or throws
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=Critical, Spec=none cited, `C8960E`, PENDING
  - Falsified if: a MINMAXINFO or RECT is read from or written to lParam with a managed layout larger than the native structure, touching memory past it
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing.OnMessage(nint, uint, nint, double)` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=Critical, Spec=none cited, `1B203E`, PENDING
  - Falsified if: StructureToPtr writes more bytes back through the WM_GETMINMAXINFO lParam than the 40-byte native MINMAXINFO it points to
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing.Rect` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=Critical, Spec=none cited, `4F156D`, PENDING
  - Falsified if: Marshal.SizeOf of Rect differs from the 16 bytes of the native RECT that WM_DPICHANGED and AdjustWindowRectExForDpi use
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing.MinMaxInfo` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=Critical, Spec=none cited, `993869`, PENDING
  - Falsified if: Marshal.SizeOf of MinMaxInfo is not the 40 bytes of the native MINMAXINFO, so StructureToPtr writes outside it
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing.GetWindowLongW(nint, int)` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=High, Spec=none cited, `2AF1F3`, PENDING
  - Falsified if: the declaration binds a signature other than user32 GetWindowLongW(HWND, int) returning a 32-bit LONG, so the style bits read are wrong
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing.AdjustWindowRectExForDpi(ref Rect, uint, bool, uint, uint)` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=High, Spec=none cited, `B3B4F6`, PENDING
  - Falsified if: the declared parameter order differs from user32 AdjustWindowRectExForDpi(LPRECT, DWORD, BOOL, DWORD, UINT), so the DPI or extended style is read from the wrong argument
- `Broiler.Mail.Windows.Hosting.WindowsWindowSizing.SetWindowPos(nint, nint, int, int, int, int, uint)` in `src/Broiler.Mail.Windows/Hosting/WindowsWindowSizing.cs` - Security=High, Spec=none cited, `D314B9`, PENDING
  - Falsified if: the declaration differs from user32 SetWindowPos(HWND, HWND, int, int, int, int, UINT), so the suggested DPI rectangle is applied with wrong coordinates or flags
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `222A47`, PENDING
  - Falsified if: a link click in the preview hands the external browser a URL that is not in the current document's ExternalLinks set
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.ImageHttpClient` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `75D67B`, PENDING
  - Falsified if: a remote image body still arriving 10 seconds after its request started keeps being read, because the client Timeout stops applying once response headers are in
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.HtmlPreviewWindow()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `1A8942`, PENDING
  - Falsified if: an embedded message image in the preview is decoded by a codec other than those ManagedImageCodecs.CreateCodecs returns, because an earlier registration made Use throw and the exception was swallowed
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.HtmlPreviewWindow(HtmlPreviewDocument, string, Action<Uri>, string?, IReadOnlyDictionary<string, MailEmbeddedImage>?)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `12DA3F`, PENDING
  - Falsified if: an ArgumentException from HtmlPreviewPolicy.Create inside LoadRemoteImagesAsync escapes the async Clicked handler of the Load remote images button and terminates the process
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.LoadRemoteImages()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `E8D147`, PENDING
  - Falsified if: an exception thrown by LoadRemoteImagesAsync is discarded while the status line already says that remote images were loaded
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.LoadRemoteImagesAsync()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `F31BD1`, PENDING
  - Falsified if: a remote image response larger than 5,000,000 bytes is read fully into memory before the size check discards it
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.OpenLink(string, bool)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `DA6972`, PENDING
  - Falsified if: a URL that is absent from the current document's ExternalLinks set, or arrives with userInitiated false, reaches the open-external callback
- `Broiler.Mail.Windows.Preview.HtmlPreviewWindow.RunCore()` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `EDEB0C`, PENDING
  - Falsified if: a GetMessage return of -1 is passed to TranslateMessage and DispatchMessage instead of ending the loop with a Win32Exception
- `Broiler.Mail.Windows.Preview.ScrollableHtmlView` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `35E864`, PENDING
  - Falsified if: a document shown through this view has an http(s) image or stylesheet it names fetched over the network instead of denied
- `Broiler.Mail.Windows.Preview.ScrollableHtmlView.ScrollableHtmlView(string, Func<IBroilerRenderer?>, Action<string>)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `F1BC57`, PENDING
  - Falsified if: an http(s) image named in the html passed to the constructor is fetched over the network when the view renders
- `Broiler.Mail.Windows.Preview.ScrollableHtmlView.UpdateHtml(string)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `2D6686`, PENDING
  - Falsified if: an http(s) image named in the html passed to UpdateHtml is fetched over the network when the view next renders
- `Broiler.Mail.Windows.Preview.HtmlViewElement` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `9D4E0E`, PENDING
  - Falsified if: rendering a document whose img src is an http(s) or file: URL sends a request for it instead of the load being denied
- `Broiler.Mail.Windows.Preview.HtmlViewElement.CreateContainer(string)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `EA9C6B`, PENDING
  - Falsified if: an img whose src is not a data: URL, or a linked stylesheet, is loaded by the container instead of being blocked by the ImageLoad and StylesheetLoad handlers
- `Broiler.Mail.Windows.Preview.HtmlViewElement.HtmlViewElement(string, Func<IBroilerRenderer?>, Action<string>)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `E5E400`, PENDING
  - Falsified if: an http(s) image named in the html passed to the constructor is fetched over the network when the element renders
- `Broiler.Mail.Windows.Preview.HtmlViewElement.UpdateHtml(string)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `591A3A`, PENDING
  - Falsified if: after UpdateHtml a paint or link hit-test runs against the container that UpdateHtml disposed
- `Broiler.Mail.Windows.Preview.HtmlViewElement.OnInput(UiInputEvent)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=none cited, `2248F9`, PENDING
  - Falsified if: the link callback fires for an input other than a left-button release, such as a pointer-down, a right click or a key press over a link
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `B984F4`, PENDING
  - Falsified if: a request the renderer sends through this transport, including one for a file: or loopback URL, is dispatched to the network or the file system
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport.Deny(HttpRequestMessage)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `5A1C67`, PENDING
  - Falsified if: Deny returns a response whose status is anything other than 403 Forbidden or whose body is not empty
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport.SendAsync(HttpRequestMessage, RequestContext, CancellationToken)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `D8BAB5`, PENDING
  - Falsified if: SendAsync completes with a response other than the empty 403 that Deny builds
- `Broiler.Mail.Windows.Preview.DenyingRequestTransport.Send(HttpRequestMessage, RequestContext, CancellationToken)` in `src/Broiler.Mail.Windows/Preview/HtmlPreviewWindow.cs` - Security=High, Spec=ADR-0005, `EF07AC`, PENDING
  - Falsified if: Send returns a response other than the empty 403 that Deny builds
- `Broiler.Mail.Windows.Preview.WindowsHtmlPreviewHost` in `src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs` - Security=High, Spec=ADR-0005, `5F4FCA`, PENDING
  - Falsified if: a URI whose scheme is not http or https is passed to Process.Start with shell execution by the preview's open-external callback
- `Broiler.Mail.Windows.Preview.WindowsHtmlPreviewHost.ShowAsync(MailMessageBody)` in `src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs` - Security=High, Spec=ADR-0005, `B20A8C`, PENDING
  - Falsified if: a Close or Dispose that runs before the preview thread publishes its window leaves that window open and shown
- `Broiler.Mail.Windows.Preview.WindowsHtmlPreviewHost.Close()` in `src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs` - Security=High, Spec=none cited, `2A0B85`, PENDING
  - Falsified if: a preview window already published in _window is never closed after Close is called
- `Broiler.Mail.Windows.Preview.WindowsHtmlPreviewHost.Dispose()` in `src/Broiler.Mail.Windows/Preview/WindowsHtmlPreviewHost.cs` - Security=High, Spec=none cited, `3163DB`, PENDING
  - Falsified if: a ShowAsync call made after Dispose opens a preview window
- `Broiler.Mail.Windows.Program` in `src/Broiler.Mail.Windows/Program.cs` - Security=High, Spec=none cited, `D26BF0`, PENDING
  - Falsified if: a --smoke-test run creates or reads files under the default data directory instead of only a fresh directory under the temp path
- `Broiler.Mail.Windows.Program.Main(string[])` in `src/Broiler.Mail.Windows/Program.cs` - Security=High, Spec=none cited, `366F93`, PENDING
  - Falsified if: an argument list other than none, --help, --demo, --smoke-test or --data-directory with a non-blank path starts the application instead of returning exit code 2
- `Broiler.Mail.Windows.Services.WindowsClipboard` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=Critical, Spec=none cited, `8C35C2`, PENDING
  - Falsified if: TryGetText reads past the GlobalSize of a clipboard block that another process placed on the clipboard
- `Broiler.Mail.Windows.Services.WindowsClipboard.UnicodeText` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `EE45C3`, PENDING
  - Falsified if: the value is not CF_UNICODETEXT (13), so a clipboard block in another format is read as NUL-terminated UTF-16 text
- `Broiler.Mail.Windows.Services.WindowsClipboard.MaximumBytes` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=Critical, Spec=none cited, `81EAFA`, PENDING
  - Falsified if: a clipboard block larger than 1,048,576 bytes is marshalled into a managed string instead of being refused
- `Broiler.Mail.Windows.Services.WindowsClipboard.TryGetText(out string)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=Critical, Spec=none cited, `108A7F`, PENDING
  - Falsified if: PtrToStringUni reads more characters than half the GlobalSize of the locked clipboard block
- `Broiler.Mail.Windows.Services.WindowsClipboard.SetText(string)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=Critical, Spec=none cited, `D29CC4`, PENDING
  - Falsified if: after SetClipboardData succeeds the finally block still passes the handle the system now owns to GlobalFree
- `Broiler.Mail.Windows.Services.WindowsClipboard.OpenClipboard(nint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `186A76`, PENDING
  - Falsified if: a failed OpenClipboard, with the clipboard held by another window, is returned as true, so TryGetText reads data it has not opened
- `Broiler.Mail.Windows.Services.WindowsClipboard.CloseClipboard()` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `9C1DBE`, PENDING
  - Falsified if: the clipboard stays open to other processes after TryGetText or SetText returns, because the declaration does not bind user32 CloseClipboard
- `Broiler.Mail.Windows.Services.WindowsClipboard.EmptyClipboard()` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `49EEBD`, PENDING
  - Falsified if: a failed EmptyClipboard is returned as true, so SetText hands its block to SetClipboardData on a clipboard it does not own
- `Broiler.Mail.Windows.Services.WindowsClipboard.GetClipboardData(uint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `A95361`, PENDING
  - Falsified if: the format argument is marshalled other than as a 32-bit UINT, so user32 is asked for a format other than the one requested
- `Broiler.Mail.Windows.Services.WindowsClipboard.SetClipboardData(uint, nint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `D4C996`, PENDING
  - Falsified if: a successful SetClipboardData is marshalled as a zero handle, so SetText frees a block the system now owns
- `Broiler.Mail.Windows.Services.WindowsClipboard.GlobalAlloc(uint, nuint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `7B51B1`, PENDING
  - Falsified if: the byte count is marshalled narrower than SIZE_T, so the block allocated is smaller than the text SetText copies into it
- `Broiler.Mail.Windows.Services.WindowsClipboard.GlobalLock(nint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `C12DF1`, PENDING
  - Falsified if: the pointer result is marshalled narrower than a pointer, so a 64-bit address is truncated before Marshal reads or writes through it
- `Broiler.Mail.Windows.Services.WindowsClipboard.GlobalUnlock(nint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `7FE0EF`, PENDING
  - Falsified if: the handle argument is marshalled narrower than a pointer, so GlobalUnlock is applied to a different block than the one locked
- `Broiler.Mail.Windows.Services.WindowsClipboard.GlobalSize(nint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `B2A448`, PENDING
  - Falsified if: the SIZE_T result is declared 32 bits wide, so a clipboard block larger than 4 GiB wraps below MaximumBytes and passes the size check
- `Broiler.Mail.Windows.Services.WindowsClipboard.GlobalFree(nint)` in `src/Broiler.Mail.Windows/Services/WindowsClipboard.cs` - Security=High, Spec=none cited, `43E113`, PENDING
  - Falsified if: the handle argument is marshalled narrower than a pointer, so GlobalFree releases a different block than the one SetText allocated
- `Broiler.Mail.Windows.Services.WindowsCredentialStore` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, Spec=ADR-0002, `1D3EF1`, PENDING
  - Falsified if: ReadAsync returns the password of a stored credential whose UserName differs from the requested key's Binding
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.Generic` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `3019E7`, PENDING
  - Falsified if: the value is not CRED_TYPE_GENERIC (1), so the password is saved as a domain credential that Windows itself offers for network sign-in to the target
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.PersistLocalMachine` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `2D868A`, PENDING
  - Falsified if: the value is CRED_PERSIST_ENTERPRISE (3) rather than CRED_PERSIST_LOCAL_MACHINE (2), so the saved password roams to other machines with the user's profile
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.NotFound` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `E37452`, PENDING
  - Falsified if: the value is not ERROR_NOT_FOUND (1168), so a Win32 failure such as ERROR_ACCESS_DENIED is swallowed as an absent credential by ReadAsync and DeleteAsync
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.MaximumBlobBytes` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, Spec=none cited, `FC2A69`, PENDING
  - Falsified if: a password longer than 1,280 characters passes the WriteAsync length check because the value exceeds CRED_MAX_CREDENTIAL_BLOB_SIZE (2,560 bytes)
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.ReadAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, Spec=ADR-0002, `85421B`, PENDING
  - Falsified if: a stored credential whose BlobSize is odd or larger than 2,560 bytes is decoded with PtrToStringUni instead of raising InvalidDataException
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.WriteAsync(CredentialKey, string, CancellationToken)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, Spec=ADR-0002, `E38D58`, PENDING
  - Falsified if: the BlobSize handed to CredWrite is larger than the byte length of the unmanaged copy of the secret, so Credential Manager reads past its end
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.DeleteAsync(CredentialKey, CancellationToken)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=ADR-0002, `3F4BA5`, PENDING
  - Falsified if: a CredDelete failure other than ERROR_NOT_FOUND returns normally, so the caller treats a password that is still stored as forgotten
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.Target(CredentialKey)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=ADR-0002, `12FF9B`, PENDING
  - Falsified if: two different account and protocol pairs format to the same target name, so saving one slot's password replaces another's
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.NativeCredential` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=Critical, Spec=none cited, `FEEA36`, PENDING
  - Falsified if: a field's order or width differs from Win32 CREDENTIALW, so BlobSize and Blob are read from the wrong offsets on x64
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.CredRead(string, uint, uint, out IntPtr)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `6B9172`, PENDING
  - Falsified if: the declaration omits SetLastError, so ReadAsync compares a stale Win32 error with ERROR_NOT_FOUND
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.CredWrite(ref NativeCredential, uint)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `D111B8`, PENDING
  - Falsified if: the credential's strings are marshalled as ANSI, so CredWriteW stores a TargetName and UserName that CredReadW never finds
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.CredDelete(string, uint, uint)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `7C4E10`, PENDING
  - Falsified if: the declaration omits SetLastError, so DeleteAsync compares a stale Win32 error with ERROR_NOT_FOUND and can report a failed delete as done
- `Broiler.Mail.Windows.Services.WindowsCredentialStore.CredFree(IntPtr)` in `src/Broiler.Mail.Windows/Services/WindowsCredentialStore.cs` - Security=High, Spec=none cited, `B6E4D2`, PENDING
  - Falsified if: the declaration binds an export other than advapi32 CredFree, so the buffer CredRead returns is released by the wrong allocator
- `Broiler.Mail.Windows.Services.WindowsTextInput` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, Spec=none cited, `4FCA45`, PENDING
  - Falsified if: ImmSetCompositionWindow is handed a form whose layout differs from Win32 COMPOSITIONFORM, so IMM32 reads the caret position from the wrong offsets
- `Broiler.Mail.Windows.Services.WindowsTextInput.PublishCaret(UiTextCaretInfo)` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, Spec=none cited, `F8B574`, PENDING
  - Falsified if: an input context from ImmGetContext is left unreleased when the scale callback or ImmSetCompositionWindow throws
- `Broiler.Mail.Windows.Services.WindowsTextInput.CompositionForm` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, Spec=none cited, `7817E6`, PENDING
  - Falsified if: the struct is smaller than Win32 COMPOSITIONFORM (28 bytes), so ImmSetCompositionWindow reads past the end of the caller's copy
- `Broiler.Mail.Windows.Services.WindowsTextInput.ImmGetContext(nint)` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, Spec=none cited, `964CC6`, PENDING
  - Falsified if: the window or context handle is declared narrower than a pointer, so a 64-bit HIMC is truncated before ImmSetCompositionWindow uses it
- `Broiler.Mail.Windows.Services.WindowsTextInput.ImmReleaseContext(nint, nint)` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, Spec=none cited, `1742AC`, PENDING
  - Falsified if: a parameter is declared narrower than a pointer, so the context released is not the one ImmGetContext returned
- `Broiler.Mail.Windows.Services.WindowsTextInput.ImmSetCompositionWindow(nint, ref CompositionForm)` in `src/Broiler.Mail.Windows/Services/WindowsTextInput.cs` - Security=High, Spec=none cited, `3139A5`, PENDING
  - Falsified if: the form is passed by value rather than by reference, so IMM32 dereferences the struct's first field as a pointer

## 10. What This Record Does Not Say

It is not an approval of the component, and a full table above would not be one either. It
records which declarations somebody stated a decision about, and against which version of
each. It does not record what they read, how long they spent, or whether they were right.

A fingerprint is six hex characters of SHA-256 over a declaration's token texts. It answers
whether a unit changed since a decision was recorded against it. It is not a collision-free
identifier across units and it is not a cryptographic commitment, so it detects a change and
does not resist a forger with commit access.

An assessment is a comment, so changing one moves no fingerprint anywhere, and nothing
mechanical checks that it is right; the check holds its values to their vocabularies and no
further.

420 of the 420 assessed units declare `Origin=AI`. Reading a declaration is the only thing
that makes it read.
