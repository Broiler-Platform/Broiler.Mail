# M3: three completed sender-identity subitems

Updated: 2026-11-03. Local alias configuration and explicit draft identity are
implemented; real-provider alias permission and broader M3 acceptance remain open.

## 1. Persisted alias configuration

Account offers a Sender aliases disclosure with a multiline editor, one email
address per line. Configure only addresses the provider permits this account to
send as; this does not provision or verify an alias at the provider. Up to 16 aliases
and 6,000 characters are accepted. Display names, malformed addresses, embedded control
characters, duplicate aliases (case-insensitive) and the primary address are rejected.
Blank lines are ignored and never authorize an empty sender.

Aliases are non-secret account configuration included in existing source-generated
JSON account/profile backup workflows. Missing/null values in legacy files become
empty. Account IDs, primary addresses, server usernames, IMAP/SMTP credential slots
and cache partitions do not change. Configuration uses the existing schema envelope;
older strict readers are not certified for downgrade.

## 2. Explicit sender selection and durable draft authority

Before starting a draft, the composer offers the primary address and configured
aliases when multiple choices exist. The saved default sender (primary unless configured otherwise) is the initial
choice after account selection. The selected address is copied into a new message/reply/forward once,
with the same account ID and account signature. Existing drafts hide the selector;
selecting another sender is refused while a draft exists. Automatic reply selection is off by default; the explicit account policy described
in [composition defaults](m3-composition-defaults.md) may select one uniquely matched
configured receiving address.

Autosave/restart retains the chosen From address and body. Account/configuration
changes never rewrite an existing draft. Building and submitting require an enabled
matching account and the exact pinned address still present as primary or alias.
Removing an alias blocks that draft until the address is explicitly reconfigured;
the text remains recoverable. The SMTP adapter independently validates authority
before opening transport, and uses the draft alias for envelope and MIME From with
the account's existing credentials. Rejection/accepted/unknown states and Sent-copy
rules are unchanged. Different display names/signatures per alias are not implemented.

## 3. Reply-all excludes every own address

Reply and reply-all recipient deduplication now seeds its exclusion set with the
primary address and every configured alias, case-insensitively. Own addresses are
excluded from From/Reply-To and visible To/Cc expansion; foreign recipients and
thread references are preserved. No Bcc is inferred or exposed. An explicitly chosen
alias remains the draft's From address independently of recipient exclusion.

## Validation and remaining scope

Ten added shared cases cover configuration/draft restart, removed-alias refusal and
reauthorization, invalid/duplicate/excessive aliases, blank-line safety, reply-all
exclusions, UI selection/pinning and SMTP alias submission/refusal over both TLS and
STARTTLS. Existing editor tests now save aliases at 100%/200% text; disclosure naming
and keyboard semantics remain covered.

The NativeAOT protocol probe now verifies alias SMTP envelope/header, removed-alias
refusal before a second DATA and reply-all self-alias exclusion, alongside existing
IMAP mutation/TLS and private-Bcc checks. Published profile smoke saves/reloads an
alias, pins it in a signed draft, reopens the draft and tests removal/reauthorization.
All accounts, credentials and transport fixtures are synthetic.

Validation passed: 1,022 shared tests, 79 focused Windows checks, NativeAOT TLS and
STARTTLS protocol fixtures, and both Windows x64 publish variants. See [Windows x64 evidence](evidence/m3-three-alias-items-win-x64/README.md).

Alias-specific display names/signatures are now implemented in the [five identity-default items](m3-sender-identity-defaults.md); multiple drafts are also available through the [draft library](m4-multiple-drafts.md). Still open: real-provider alias authorization/certification, broader account orchestration/unified views, other-platform acceptance and remaining M0–M4 gaps.
