# M3: five per-address sender identity items

Updated 2026-11-03. Five remaining sender-identity subitems are implemented:

1. **Validated per-address configuration.** Optional `SenderIdentities` entries belong to an existing account and must name its primary address or an already configured alias, using the same exact address spelling as the sender whitelist. At most 17 distinct entries are accepted. A display name is at most 200 valid Unicode characters with no controls; each plain-text signature has the existing 4,000-character/control/Unicode limits. Null means inherit, while an empty override deliberately suppresses a value. Configuration cannot provision aliases, authorize a new From address or change credentials. Invalid, duplicate, foreign and null entries are refused by the existing strict account stores.
2. **Pinned sender display names.** A newly created draft snapshots the selected primary/alias display name into `FromDisplayName`. The composer shows it beside the pinned address. Autosave, restart, library parking/resume and MIME serialization preserve it despite later account changes. SMTP and Sent copies use the draft's name with its existing authorized From address; SMTP authentication and envelope address remain governed by the account and sender whitelist. Empty names serialize as a bare address. ASCII punctuation is quoted/escaped; Unicode names use folded Q-encoded words of at most 72 characters, without splitting a Unicode scalar. Valid mailbox syntax is parsed before display-name decoding so quoted/encoded commas do not invent extra recipients.
3. **Per-address new/forward signatures.** When a new message or forward starts, its selected sender's signature override takes priority over the account signature. Empty overrides omit the signature. Signatures are ordinary editable body text applied once, never rewritten when account settings change or an existing draft is reopened/resumed. The account default and explicit sender picker continue to select the address before the signature is chosen.
4. **Per-address reply signatures.** Explicit identity reply overrides take priority. Otherwise the existing account reply policy applies: a separate account reply signature wins when enabled; with the shared policy, the identity's normal signature (or account fallback) is used. Empty reply overrides suppress signatures independently. Automatic unique receiving-address selection runs first, so the selected alias gets its own reply defaults. Explicit sender choice still takes priority. Reply threading, own-address exclusions and Bcc privacy are unchanged.
5. **Account editor and inheritance/reset controls.** In Account → Sender aliases, choose Sender address to customize. Each display-name/new-message/reply setting has an Inherit or Override policy, with an empty override explicitly documented as omission. Edits participate in ordinary account Save and unsaved-change/credential gates; resetting the selected sender restores inheritance. Removed aliases with retained overrides remain selectable so their overrides can be reset before saving. Save validation expands the alias section on identity errors. Profile selection reloads the editor; verified account backup/restore retains settings and published app smoke constructs the editor in both Windows variants.

## Compatibility and validation

The optional account property is absent when unused and uses the existing strict,
source-generated schema-1 account envelope. `FromDisplayName` is also optional:
legacy drafts without it retain the prior behavior of taking the sending account's
current display name; they are not silently rewritten. Older strict builds refuse
files with the new properties. Use a compatible build or an earlier verified full
profile backup for rollback, retaining later drafts/edits first. No file is reset.

Twenty-seven added shared cases cover account partitioning and persistence,
validation, punctuation/Unicode MIME mailbox handling and word bounds, legacy/empty
names, signature inheritance/suppression, new/forward/reply selection, restored and
parked drafts, editor save/reset and verified profile backup/restore. The Windows
published fixture checks account settings, newly started and reopened drafts,
receiving-alias reply suppression, MIME identity, backup/restore and editor controls.
The NativeAOT TLS/STARTTLS protocol fixture sends a draft whose original alias name
is retained after account presentation changes, verifying the quoted From header,
alias envelope and private Bcc with the existing SMTP/IMAP invariants.

See [Windows x64 validation evidence](evidence/m3-five-identity-items-win-x64/README.md).
Real-provider alias authorization/certification, broader per-account orchestration,
unified views, OAuth/separate-credential work and native accessibility acceptance
remain open. M0 ARM64/Linux certification, M1 broader offline policy, and M2 process
containment/acceptance are also still pending. This increment adds no JavaScript
or package dependency and does not claim completion of M0–M3.
