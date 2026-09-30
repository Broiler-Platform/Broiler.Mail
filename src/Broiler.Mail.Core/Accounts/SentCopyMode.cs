// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   1
// Annotated:        1/1
// Exempt:           3
// Human-reviewed:   0/1
// IP risk:          None
// Security risk:    Low
// Criteria:         1/0
// Resource impact:  0/10 max
// Unverified:       1
//
// GENERATED - DO NOT EDIT MANUALLY

namespace Broiler.Mail.Core.Accounts;

// Broiler-AI:           Origin=AI; Spec=ADR-0004; IP=None; Security=Low; Resources=0; Fingerprint=CECBF1
// Broiler-Falsified-If: the member order stops matching the Account view's Sent-copy list, so choosing append stores a different mode
// Broiler-Human:        PENDING
public enum SentCopyMode { NotConfigured, ProviderManaged, AppendToFolder }
