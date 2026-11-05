# M3/M4: per-account plain-text signatures

Updated: 2026-11-03. This increment adds account signatures; multiple drafts/outbox
and broader milestone acceptance remain open.

Account identity now includes a multiline plain-text signature editor. Save commits
it with the same stable account ID and account collection; switching accounts loads
that account's signature. Legacy profiles with a missing or null signature load an
empty value. Signatures are non-secret configuration included in normal profile
backup/restore; no credential, cache schema or account-collection version changes
are needed. Source-generated JSON handles the additional property. Downgrading to
older strict readers is not certified.

Signatures accept up to 4,000 UTF-16 characters, including line breaks and tabs.
Unsupported controls and malformed surrogate sequences are rejected by account
validation and before draft creation. Empty/whitespace signatures preserve existing
composition behavior. Text is plain text; it is not interpreted as HTML or code.

Starting a new message, reply, reply-all or forward copies the selected account's
signature into the new draft once. Line endings normalize to LF. The body begins
with two blank lines, the standard `-- ` signature separator and signature text;
for replies/forwards, the original quoted/forwarded content follows beneath it.
The user can edit or remove this copied text like any other draft text.

Changing account selection or editing the saved signature never rewrites an
existing draft. Restored drafts retain their stored body without reapplying current
configuration. Existing sender authority, autosave, submission and Sent-copy states
remain in charge; no new send/retry behavior is introduced. Signature defaults are
shared by all sender aliases on the account; alias-specific signatures remain open.

## Validation

Nine added shared cases cover independent account signatures, account-store reopen,
draft flush/reopen, no reapplication after configuration/account changes, new-message
behavior with empty/whitespace signatures, reply/reply-all/forward quote preservation,
control/Unicode/length validation, and editor capture/save at 100%/200% text.
Published profile smoke saves an account signature, reloads it, creates and flushes
a signed draft, then reopens that draft against a changed signature and verifies its
text is unchanged. All data and paths are synthetic temporary fixtures.

Validation passed: 979 shared tests, 79 focused Windows checks and both Windows x64
publish variants, including NativeAOT. See [Windows x64 validation](evidence/m4-signatures-win-x64/README.md).

## Remaining M0–M4 work

M0 still needs matching-architecture CI evidence beyond Windows x64. M1's broader
retention/offline-folder and platform acceptance remain open. M2 still needs renderer
process containment and broader native accessibility/provider acceptance. M3 now has explicit sender aliases and still needs provider acceptance and broader
per-account orchestration. M4 still needs durable mutation
recovery, general attachments, indexed full-text search, multiple drafts/outbox and
message import/export. This signature increment does not close those milestones.
