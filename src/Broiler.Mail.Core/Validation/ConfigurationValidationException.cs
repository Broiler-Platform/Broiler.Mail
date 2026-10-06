// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        0/1
// Exempt:           1
// Human-reviewed:   0/1
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Validation;

/// <summary>Identifies the input responsible for a configuration error without parsing display text.</summary>
public sealed class ConfigurationValidationException(string field, string message) : ArgumentException(message)
{
    public string Field { get; } = field;
}
