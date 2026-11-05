# Three M3/M4 composition-default subitems

Updated: 2026-11-03. Account now offers a Composition defaults disclosure. Defaults
are copied only into newly started drafts; existing/recovered drafts retain their
pinned account/From and saved text.

## 1. Saved per-account default sender

Default sender address may be blank (primary address), the exact primary address,
or an explicitly configured alias. Other values are rejected. The chosen default
persists in source-generated account JSON and normal profile backup/restore. Legacy
missing/null values use primary. The composer initializes its sender choice from
this setting; explicit pre-draft selection overrides it for that draft. Starting a
draft consumes the explicit choice, so the next draft again begins with account
defaults. Config changes never rewrite an existing draft.

Removing an alias that is still the saved default requires choosing a valid default
before saving the account. Draft submission independently retains the prior enabled
account/authorized-address guard; setting a default does not provision provider
permission or change credentials/server usernames.

## 2. Opt-in receiving-address policy for replies

The default policy uses the chosen/default sender. An explicit account policy may
instead use the unique configured account address present in a reply's parsed To/Cc.
Matching is case-insensitive; the selected sender uses the configured spelling.
Only the primary address and configured aliases are candidates. Neither From,
Reply-To, absent/Bcc recipients nor arbitrary header addresses authorize a sender.

A unique candidate is used for a new reply/reply-all. Zero or multiple candidates
fall back to the saved default. A user's explicit sender choice always takes
priority. Forwarding uses chosen/default sender, not receiving-address selection.
The resulting draft pins the address and visibly reports From before any send;
later header/config changes do not rewrite it. This opt-in policy uses visible
parsed recipients, not verified delivery-envelope metadata.

## 3. Separate reply signature

Reply signature policy defaults to the normal account signature. The user may opt
into a separate multiline plain-text signature for reply/reply-all, with the same
4,000-character/control/Unicode validation as the normal signature. An empty
separate signature intentionally suppresses a reply signature. New messages and
forwards keep the normal signature.

Text is copied once before quoted content using the existing signature separator;
original quoted text is preserved. Saved drafts never reapply later configuration.
These are account defaults shared by aliases; individual alias display names and
signature overrides remain open.

## Validation and remaining scope

Ten added shared cases cover configuration/draft restart, default/override authority,
unique/ambiguous/unknown/uppercase receiving matches, opt-in behavior, separate
reply/reply-all/forward signatures, suppression, invalid configuration and retained
draft text. Existing 100%/200% editor tests now save the default address, reply policy
and separate signature, and disclosure semantics cover Composition defaults.

Published profile smoke saves/reopens these defaults, pins a default alias, retains
restored sender/text, and starts a reply with unique receiving alias plus separate
signature. NativeAOT TLS/STARTTLS protocol fixtures now initialize the alias draft
through the saved default and retain removed-alias refusal/private-Bcc checks.
Validation passed: 1,032 shared tests, 79 focused Windows checks, NativeAOT TLS and
STARTTLS protocol fixtures and both Windows x64 publish variants. See [Windows x64 evidence](evidence/m3-three-composition-defaults-win-x64/README.md).

Still open: real-provider alias permission/certification, alias-specific display
names/signatures, broader native accessibility/account orchestration, other-platform
certification and the remaining M0–M4 implementation/acceptance gaps.

## Per-address overrides (2026-11-03)

Configured primary/alias addresses can now override the display name, normal
signature and reply signature with explicit inheritance or empty suppression.
Sender selection precedes identity resolution, and newly created drafts pin names
and body text. See [five sender identity items](m3-sender-identity-defaults.md).
