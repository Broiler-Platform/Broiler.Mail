// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           3
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Mail.Core.Accounts;
using Broiler.Mail.Core.Messages;
using Broiler.Mail.Core.Services;
using Broiler.Mail.Core.Settings;

namespace Broiler.Mail.Infrastructure.Persistence;

internal sealed class ConfigurationEnvelope<T> where T : class
{
    public required int SchemaVersion { get; init; }
    public required T Data { get; init; }
}

[JsonSerializable(typeof(ConfigurationEnvelope<AccountProfile[]>), TypeInfoPropertyName = "Accounts")]
[JsonSerializable(typeof(ConfigurationEnvelope<ApplicationSettings>), TypeInfoPropertyName = "Settings")]
[JsonSerializable(typeof(ConfigurationEnvelope<DraftStoreState>), TypeInfoPropertyName = "Drafts")]
internal partial class ConfigurationJsonContext : JsonSerializerContext
{
    // Generic enum converters retain rejection of integer enum values without runtime code generation.
    internal static ConfigurationJsonContext Storage { get; } = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<TransportSecurity>(allowIntegerValues: false),
            new JsonStringEnumConverter<AuthenticationMethod>(allowIntegerValues: false),
            new JsonStringEnumConverter<SentCopyMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<AppTheme>(allowIntegerValues: false),
            new JsonStringEnumConverter<InboxDensity>(allowIntegerValues: false),
            new JsonStringEnumConverter<DraftSubmissionState>(allowIntegerValues: false),
            new JsonStringEnumConverter<SentCopyState>(allowIntegerValues: false),
        },
    });
}
