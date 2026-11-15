// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   12
// Annotated:        1/12
// Exempt:           16
// Human-reviewed:   0/12
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  1/10 max
// Unverified:       12
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

/// <summary>Non-secret configuration. Credentials belong in ICredentialStore.</summary>
// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=AB2C4D
// Hairs-Falsified-If: two profiles that differ only in OutgoingServer or SentFolder compare equal, so a password can be saved against unsaved SMTP edits
// Hairs-Human:        PENDING
public sealed record AccountProfile
{
    public required AccountId Id { get; init; }
    public required string DisplayName { get; init; }
    public required string EmailAddress { get; init; }
    private string _senderAliases = "";
    /// <summary>Explicit provider-authorized sender addresses, one per line. This does not provision aliases at the provider.</summary>
    public string SenderAliases { get => _senderAliases; init => _senderAliases = value ?? ""; }
    public IEnumerable<string> SenderAddresses() => new[] { EmailAddress }.Concat(SenderAliases.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).Where(value => value.Length > 0));
    public bool HasSenderAddress(string address) => SenderAddresses().Contains(address, StringComparer.Ordinal);
    private string _defaultSenderAddress = "";
    public string DefaultSenderAddress { get => _defaultSenderAddress; init => _defaultSenderAddress = value ?? ""; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SenderIdentitySettings>? SenderIdentities { get; init; }
    public SenderIdentitySettings? IdentityFor(string address) => SenderIdentities?.SingleOrDefault(value => value.Address == address);
    public string SenderDisplayName(string address) => IdentityFor(address)?.DisplayName ?? DisplayName;
    public string SenderSignature(string address, bool replying)
    {
        var identity = IdentityFor(address);
        return replying ? identity?.ReplySignature ?? (UseSeparateReplySignature ? ReplySignature : identity?.Signature ?? Signature)
            : identity?.Signature ?? Signature;
    }
    public bool ReplyFromReceivingAlias { get; init; }
    public bool UseSeparateReplySignature { get; init; }
    private string _replySignature = "";
    public string ReplySignature { get => _replySignature; init => _replySignature = value ?? ""; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DefaultSender => string.IsNullOrEmpty(DefaultSenderAddress) ? EmailAddress : DefaultSenderAddress;
    private string _signature = "";
    public string Signature { get => _signature; init => _signature = value ?? ""; }
    public required MailServerSettings IncomingServer { get; init; }
    public MailServerSettings? OutgoingServer { get; init; }
    public SentCopyMode SentCopyMode { get; init; }
    public string? SentFolder { get; init; }
    private AccountFolderOverrides _folderOverrides = new();
    public AccountFolderOverrides FolderOverrides
    {
        get => _folderOverrides;
        init => _folderOverrides = value ?? new();
    }
    public bool IsEnabled { get; init; } = true;
}
