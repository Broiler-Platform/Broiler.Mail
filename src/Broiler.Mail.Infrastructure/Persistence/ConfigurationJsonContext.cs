// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        2/2
// Exempt:           3
// Human-reviewed:   0/2
// IP risk:          None
// Security risk:    High
// Criteria:         1/1
// Resource impact:  1/10 max
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

// Broiler-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=66937C
// Broiler-Human:        PENDING
internal sealed class ConfigurationEnvelope<T> where T : class
{
    public required int SchemaVersion { get; init; }
    public required T Data { get; init; }
}

// Broiler-AI:           Origin=AI; IP=None; Security=High; Resources=1; Fingerprint=C943A6
// Broiler-Falsified-If: a configuration file with an unknown property, or an integer where an enum name belongs, deserializes without a JsonException
// Broiler-Human:        PENDING
[JsonSerializable(typeof(ConfigurationEnvelope<LocalOriginalState>), TypeInfoPropertyName = "LocalOriginals")]
[JsonSerializable(typeof(ConfigurationEnvelope<SourceExportCatalogState>), TypeInfoPropertyName = "SourceExportCatalog")]
[JsonSerializable(typeof(ConfigurationEnvelope<AccountNavigationState>), TypeInfoPropertyName = "NavigationPreferences")]
[JsonSerializable(typeof(ConfigurationEnvelope<OfflineFolderState>), TypeInfoPropertyName = "OfflineFolders")]
[JsonSerializable(typeof(ConfigurationEnvelope<RecipientBookState>), TypeInfoPropertyName = "Recipients")]
[JsonSerializable(typeof(ConfigurationEnvelope<MutationIntentState>), TypeInfoPropertyName = "MutationIntents")]
[JsonSerializable(typeof(ConfigurationEnvelope<CacheRecoveryState>), TypeInfoPropertyName = "CacheRecovery")]
[JsonSerializable(typeof(ConfigurationEnvelope<AccountProfile[]>), TypeInfoPropertyName = "Accounts")]
[JsonSerializable(typeof(ConfigurationEnvelope<AccountCollectionState>), TypeInfoPropertyName = "AccountCollection")]
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
