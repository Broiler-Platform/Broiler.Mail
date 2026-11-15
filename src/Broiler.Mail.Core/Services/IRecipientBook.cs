// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   8
// Annotated:        0/8
// Exempt:           0
// Human-reviewed:   0/8
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Mail.Core.Accounts;
using System.Net.Mail;
using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Services;

/// <summary>Explicitly saved, account-scoped recipients. No automatic address harvesting.</summary>
public interface IRecipientBook
{
    Task<IReadOnlyList<SavedRecipient>> LoadAsync(AccountId account, CancellationToken token = default);
    Task SaveAsync(SavedRecipient recipient, CancellationToken token = default);
    Task RemoveAsync(AccountId account, string address, CancellationToken token = default);
}

public sealed record SavedRecipient(AccountId AccountId, string Address, string Name)
{
    public static SavedRecipient Create(AccountId account, string address, string name)
    {
        if (account.Value == Guid.Empty) throw new ArgumentException("A recipient account is required.");
        if (address is null || address.Length > 320 || address.Any(char.IsControl) || name is null || name.Length > 120 || name.Any(char.IsControl))
            throw new ArgumentException("Use an address of at most 320 characters and a name of at most 120, without controls.");
        address = address.Trim(); name = name.Trim();
        if (!MailAddress.TryCreate(address, out var parsed) || !string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase) || !address.Contains('@'))
            throw new ArgumentException("Enter one email address without a display name or recipient list.");
        return new(account, parsed.Address, name);
    }
    [JsonIgnore] public string Label => Name.Length == 0 ? Address : $"{Name} <{Address}>";
}

public sealed record RecipientBookState(IReadOnlyList<SavedRecipient> Recipients);
