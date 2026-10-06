// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   2
// Annotated:        0/2
// Exempt:           0
// Human-reviewed:   0/2
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       2
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Text.Json.Serialization;

namespace Broiler.Mail.Core.Accounts;

// Property order, casing, numeric enums and escaping are part of the existing credential hash.
internal sealed record CredentialBinding(string Host, int Port, string UserName,
    TransportSecurity Security, AuthenticationMethod Authentication);

[JsonSerializable(typeof(CredentialBinding))]
internal partial class CredentialJsonContext : JsonSerializerContext;
