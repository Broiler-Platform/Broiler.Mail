# Broiler.Mail product completion roadmap

Updated: 2026-11-04. This is the active forward plan. It supersedes the delivery
order of versions 3–7 in [the original roadmap](roadmap.md#historical-milestone-plan),
while preserving their acceptance records. All work below is planned unless the
baseline explicitly identifies existing implementation. Milestones are ordered
outcomes, not promised dates or package version numbers.

## Direction and implementation baseline

Make Mail a dependable everyday client: downloaded mail survives restart, HTML
appears in the normal reading pane, accounts stay independent, and releases are
repeatable across supported native targets. **JavaScript is excluded by design**
from mail rendering and application behavior. NativeAOT is a first-class desktop
release variant throughout development, rather than a final packaging exercise.

The inspected source already provides:

- Per-user settings, one saved account, protected credentials, local draft recovery,
  IMAP receiving, SMTP submission, replies/forwards, and Sent-copy state handling.
- A Windows inline HTML reader through Broiler.HTML, with a remembered text toggle.
  Script/resource restrictions exist; process isolation does not. The older window
  adapter remains available to measurement tools.
- A Windows project with `PublishAot=true`, source-generated JSON, and a scoped
  renderer compatibility substitution. AOT support is already intended; new
  dependencies and workflows still need publish-time and runtime verification.
- A Publish workflow for Windows x64/ARM64 in NativeAOT and framework-dependent
  variants, plus Android APK/AAB packaging, shared versioning, checksums, and a
  draft release. Linux's executable is a placeholder; no Mail WebAssembly head exists.

Important gaps: Windows account management and explicit sender aliases are implemented;
broader account orchestration/provider acceptance remain pending. The durable Windows inbox
cache supports folder navigation; saved-header search and optional indexing over previews/complete retained decoded bodies are available, including an independent sender filter. General attachments remain open.
Inline rendering still lacks process containment and full M2 UI acceptance.
Historical acceptance results do not certify new builds or providers.

The [M0–M4 completion audit](m0-m4-completion-status.md) enumerates every remaining parent gate and distinguishes implementation from external acceptance. Local `.eml` opening/import now has independent local identities and verified-backup retention; this does not close attachment streaming, printing or renderer containment.

The final M0–M4 pass uses Windows x64 as its local acceptance target. Renderer isolation and cross-platform certification are deferred. See the [final scope and remaining work](m0-m4-completion-status.md#final-m0m4-scope).

## Delivery order

| Milestone | Outcome | Dependency |
| --- | --- | --- |
| M0 — Contracts and release baseline | Durable identities and AOT remain viable as features grow. | Start now; publish checks continue through every milestone. |
| M1 — Durable local profile and offline reader | Downloaded mail is available after restart without a server. | M0; design account partitioning now. |
| M2 — HTML in the reading pane | Formatted mail opens directly inside the default mail view. | M0; can proceed alongside M1. |
| M3 — Multiple accounts and identities | Personal and work mail operate independently. | M1; use its account-scoped storage. |
| M4 — Everyday mail | Folders, attachments, search, and multiple drafts cover daily work. | M1/M3; M2 for HTML acceptance. |
| M5 — Reliable synchronization and provider access | Reconnects, offline edits, and sign-in are predictable. | Incremental receive starts in M1; mutation replay follows M4. |
| M6 — Supported releases | Tested native packages install and upgrade reliably. | Release engineering starts in M0; certify only completed platform workflows. |
| M7 — Productivity and expansion | Conversations, contacts, rules, and optional integrations. | Stable everyday workflows and measured demand. |

## M0 — Establish contracts and keep releases working

**Updated 2026-11-02:** the Windows x64 foundation, provider/protocol probes, and
published application checks pass. M0 remains open for matching-architecture CI
evidence on Windows ARM64 and Linux; those jobs are configured but not run locally.
See [M0 progress and validation](m0-foundation-status.md) for exact coverage and
remaining probes. Decisions [0007](decisions/0007-durable-profile-and-cache.md),
[0008](decisions/0008-inline-reader-and-containment.md), and
[0009](decisions/0009-native-release-baseline.md) establish the forward design.
The [Broiler.Net session/profile review](broiler-net-session-profile-review.md)
confirms reusable HTTP sessions but no durable Mail profile store; preview.4 now
has a managed/NativeAOT compatibility and isolation probe in the M0 checks.

- [x] Record decisions for profile layout/store engine, message identity, inline
  renderer hosting/containment, and release target selection. Update ADR 0005's
  separate-window policy when the inline design is approved; retain its resource
  restrictions. UI embedding and operating-system process isolation are independent.
- [x] Define account-scoped storage/connection contracts and cancellable background
  work before changing screens. Preserve stable account IDs during migration.
- [x] Validate the selected transactional SQLite store on Windows x64. The local
  NativeAOT storage, protocol and session probes and package smoke checks passed.
  Matching Windows ARM64/Linux certification remains a deferred follow-up.
- [x] Extend publish smoke checks to exercise profile creation, restart, MIME parsing,
  HTML rendering, credentials, and fixture IMAP/SMTP against the published binaries.
  Track trimming/AOT warnings; fix or narrowly justify each warning and re-audit
  `NativeAot.Substitutions.xml` whenever renderer packages change.
- [x] Add a dependency gate forbidding known JavaScript engines, runtime script evaluation,
  and dynamic-code requirements in native Mail's resolved runtime dependency graph.

**Exit:** contracts and decisions are recorded; a clean Windows NativeAOT publish
passes representative fixture workflows. No real account is needed in CI.

## M1 — Persistent profile and downloaded mail

**Started 2026-11-02:** Windows now adopts existing profiles with backups and a
process lease, persists headers/decoded bodies to SQLite, and opens saved mail
after restart without a server. See [M1 implementation and remaining work](m1-persistence.md).
Settings now includes storage usage, verified backups, separate-profile restore,
and recovery-backed cache cleanup. Cleanup pauses receive and snapshots flush drafts.
Download policy (save-on-open or headers-only) and live quota changes are implemented,
along with cancellable, resumable-by-retry downloads of the loaded inbox headers.
Loaded-message downloads remain bounded to 500 rows. Explicit selected-folder batches now support manual continuation through saved headers, up to 500 per folder per click; separate manual selected-folder header pages now acquire older headers beyond loaded rows. Automatic/authoritative synchronization remains pending.
The local profile and manual offline workflows below meet the Windows x64 scope. M5 owns ongoing synchronization and folder-rename tracking. The larger attachment workflow remains in M4.

- [x] Keep the manifest, account configuration, mailbox metadata, downloaded mail,
  drafts and manual checkpoints in a versioned per-user profile. Store credentials
  in platform protection services and keep the search index rebuildable.
- [x] Migrate existing `accounts.json`, `settings.json`, and `drafts.json` safely.
  Back up first; use atomic/versioned migration and a single-writer lock. Report
  unsupported newer schemas without overwriting them. Define restore/rollback
  compatibility and recover interrupted migration without silent resets.
- [x] Key saved IMAP records by `(AccountId, MailboxId, UIDVALIDITY, UID)` and
  store flags, dates, download state and manual observation checkpoints. Track
  durable folder renames and ongoing sync in M5.
- [x] Persist fetched headers and successfully downloaded supported decoded bodies/CID parts before
  reporting them as available offline. Reuse cached bodies on selection/restart;
  show cached, downloading, incomplete, stale, and failed states explicitly.
- [x] Capture an explicit complete current-folder header observation with before/after UID censuses, epoch/count checks and bounded metadata batches; reject partial or changing membership.
- [x] Persist header upserts, the verified UID set/hash and observation checkpoint atomically, retaining previously saved mail absent from the observation.
- [x] Upgrade to cache v5 after a verified v4 backup; include observations in restore and remove only the appropriate checkpoint/census on recovery-backed account/whole-cache cleanup.
- [x] Provide explicit current-folder Capture/Inspect controls, disabled-account local inspection, correct retry/cancellation guidance and stale-context rejection.
  See [complete header observations](m1-mailbox-header-snapshots.md). The 4,096-message cap is a refusal boundary, not a complete-folder claim for larger mailboxes. Continuous/incremental synchronization and folder rename identity remain open.
- [x] Provide explicit headers-only, save-on-open and selected-folder offline
  downloads with live quotas, cancellation, retry and keep-until-cleared retention.
  Recovery-backed cache cleanup preserves drafts and pending operations. Save
  non-inline attachments separately through the original-source workflow in M4.
- [x] Persist bounded exact offline-folder selections independently for each account.
- [x] Add reader select/unselect/reload controls with saved-only and coverage notices.
- [x] Download supported bodies explicitly across selected folders using at most 500 newest saved headers each.
- [x] Preserve confirmed saves across cancellation/failure and retry safely with account/epoch checks.
- [x] Include selections in verified profile backup/restore and published NativeAOT checks; keep them on cache clear.
  See [five selected-folder offline items](m1-selected-offline-folders.md) for limits and evidence.
- [x] Continue selected-folder body batches through older saved headers with exclusive per-folder epoch/UID cursors.
- [x] Expose explicit Next/Restart and completion controls without automatic downloads or deleting saved mail.
- [x] Advance cursors only after successful saves; preserve retry pages and refuse changed epochs until restart.
  See [three offline paging items](m1-offline-batch-paging.md) for the original bounded walk.
- [x] Persist validated account-scoped offline continuation keys alongside exact selections.
- [x] Resume explicit batches and retain completion across restart/account switch/Reload without networking.
- [x] Advance durable checkpoints after body saves, preserving retries and rejecting changed selections/epochs.
- [x] Persist explicit Restart before resetting the walk; retain progress through ordinary maintenance.
- [x] Back up/restore checkpoints with mail and reset them before recovery-backed cache clear; verify published NativeAOT behavior.
  See [five durable offline progress items](m1-durable-offline-progress.md) for compatibility and limits.
- [x] Receive explicit bounded header pages across the current account's selected offline folders without body downloads or automatic navigation.
- [x] Validate full mailbox/epoch identity, unique descending UIDs, page bounds and advancing compatible server continuations before saving.
- [x] Advance per-folder session checkpoints only after confirmed cache commits; retry unfinished batch pages and reject late/canceled/context-changed results.
- [x] Add separate Receive/Next/Restart header controls, saved-page coverage, scoped Retry/Cancel and local Show saved headers controls.
- [x] Verify beyond-500-header acquisition, restart/disabled-account reading, backup/restore/cache-clear boundaries and published NativeAOT execution.
  See [five selected-folder header items](m1-offline-header-acquisition.md). Server continuations remain session-only; authoritative/background synchronization remains open.
- [x] Add profile location/status, storage usage, cache cleanup, backup/restore, and
  downloaded-mail removal controls. Clarify that an OS-protected password does not encrypt
  cached mail; document local file permissions and evaluate optional cache encryption
  with a defined key-recovery policy.

**Exit:** receive headers and selected bodies, close, disconnect, and restart: those
messages still open; uncached mail is labeled unavailable. Verify interrupted writes,
corrupt data, full disk, competing instances, migration, and UIDVALIDITY changes.
A cache reset must preserve drafts and never delete server mail.

## M2 — Inline HTML as the default reading experience

**Started 2026-11-02:** Windows now embeds the restricted, tiled HTML surface in
the default reader, with a persistent Plain text / HTML preference and disposal on
selection changes. No preview window is required. Initialization failure keeps the
text reader. See [M2 progress](m2-inline-reader.md). Process containment, explicit
broader remote-image networking acceptance, native accessibility acceptance, and crash/hang recovery
remain open; the milestone is not yet complete. Inline zoom controls, system
text-scale following, and recoverable managed render-failure text fallback now exist.
The [remote-image broker](m2-remote-image-broker.md) is implemented and tested using
Broiler.Net. Inline per-message consent, cancellation, partial retry and stale-result
rejection are implemented. Windows x64 production public-DNS/HTTPS and certificate-rejection
checks now have [NativeAOT evidence](evidence/m2-production-https-win-x64/README.md); broader target/resource-path acceptance remains open;
automatic downloads stay blocked.
Inline keyboard zoom and shell Tab/Enter hyperlink traversal now have regression
coverage, including reader focus scope, AltGr, precision wheel input, hidden compact
panes and a keyboard-operable text alternative. Wider accessibility/desktop
acceptance and process containment remain open.
Inline page colors now follow light/dark/high-contrast themes with raster evidence,
preserving reading position and approved images across palette changes. Broader
operating-system accessibility/high-contrast acceptance remains open.

- [x] Replace the open/close-preview action with an embeddable reading surface.
  Prefer available HTML in the main reading pane; provide a visible Plain text / HTML
  toggle, persist the preference, and handle plain-text-only mail naturally.
  A separate window may later be an optional pop-out, never the required HTML path.
- [x] Extract a platform-neutral relative-viewport contract for inline HTML and selectable text.
- [x] Preserve approximate reading position across HTML/text mode changes and managed render fallback, rejecting stale surfaces.
- [x] Add keyboard-accessible Top/End controls for HTML-capable messages and their text alternative.
  See [three reader viewport items](m2-reader-viewport.md) for validation and remaining limits.
- [x] Add bounded literal find over one decoded message preview with explicit hit/input limits.
- [x] Provide submitted-query, next/previous and wrap/status controls for message text find.
- [x] Select/reveal matches in the text alternative without changing saved reader preferences or message contents.
- [x] Add reader-scoped Ctrl+F/F3/Shift+F3/Escape and reset find state on message changes.
- [x] Copy only the current match explicitly, with no clipboard reads and failure/stale-context guards.
  See [five message find items](m2-message-find.md) for preview coverage and validation.
- [x] Reset per-open-message remote-image permission, discarding approved downloads and rejecting pending/queued results while retaining embedded images and reader settings.
- [x] Add explicit **Block remote images again** controls with HTML-mode guards, usable focus and an explanation that earlier requests cannot be undone.
- [x] Exercise blocking and fresh consent in published NativeAOT smoke and regress cancellation, stale results, image retention, theme, zoom and reading position.
  See [three remote-image reset items](m2-remote-image-reset.md) and their Windows x64 evidence. Broader network acceptance and containment remain open.
- [x] Exercise the production DNS-pinned remote-image broker against public HTTPS in an explicit NativeAOT diagnostic, requiring a valid image and expired/hostname-mismatch/self-signed endpoint rejection without TLS overrides.
  See [Windows x64 production HTTPS evidence](evidence/m2-production-https-win-x64/README.md). This diagnostic is outside startup/CI and does not close all-resource-path or other-target acceptance.
- [x] Route shell and reader HTML through `IInlineHtmlPreviewHost`. Keep the
  separate measurement window behind an explicit native demo command. Show the
  HTML unavailability reason alongside selectable message text.
- [x] Retire the legacy pop-out reader action and contract. The Windows inline
  adapter creates reader-owned surfaces. A separate host owns the demo measurement window.
- [x] Preserve no-script rendering, blocked automatic remote requests, bounded CID
  image resolution, explicit per-message remote-image permission, and deliberate
  external-link opening. Apply policy to CSS URLs, fonts, redirects, and every
  resource path. Never pass secrets or arbitrary profile paths into the renderer.
- [ ] **Deferred release gate.** Resolve containment with a restricted renderer process and
  bounded IPC/shared surfaces where viable. Specify timeout, cancellation, memory
  limits, termination, and text fallback. If containment is incomplete, label the
  build accordingly; inline presentation alone does not close the security gate.
  Keep the rendering solution JavaScript-free.
- [x] Discard stale results on message/account changes. Keep reader clipping, links,
  keyboard scrolling, zoom and theme behavior, with selectable and copyable text
  available through the plain-text reader. Direct selection in the HTML canvas and
  the wider native accessibility and DPI matrix remain follow-ups.

**Exit:** selecting HTML mail displays it in the ordinary reader without opening
another window. Test HTML-only/multipart mail, CID images, long/table layouts,
malformed content, blocked resources, rapid switching, and renderer failure.
No automatic network requests or script execution occur in these fixtures.

## M3 — Multiple accounts, folders, and sender identities

**Started 2026-11-02:** a versioned account-collection store now imports legacy
configuration without changing its bytes, persists stable IDs/order/selection and
disabled state, and participates in verified profile backup/restore. See
[M3 account progress](m3-account-collection.md). Windows now restores the selected
account, offers selection/add/edit/setup, retains a draft's sender, and cancels old
receive work. Enable/disable and ordering now have guarded management controls.
Recovery-backed removal now offers explicit keep/forget-password choices, refuses
retained draft owners and keeps downloaded mail. Explicit [sender aliases](m3-sender-aliases.md) are now implemented; provider
authorization/certification, broader switch acceptance and per-account orchestration
remain open. [Per-account plain-text
signatures](m4-signatures.md) now persist and initialize new drafts without rewriting
existing drafts.

- [x] Replace the single-account store restriction with a versioned account collection;
  add add/edit/test/disable/remove/switch UI, ordering, names, and account indicators.
  Implemented in the Windows head; architecture/platform certification remains M6 work.
- [x] Partition password credentials for IMAP and SMTP, saved mail, drafts and
  manual receive jobs by stable account ID. Keep failures and cancellation scoped
  to the account. OAuth and ongoing synchronization are M5 work.
- [x] Add an explicit session-only account header queue with two FIFO receive slots, per-account coalescing and isolated progress/errors/cancellation, skipping disabled accounts.
- [x] Revalidate captured account configuration before connection/publication and serialize application configuration writes with the final scoped cache commit.
- [x] Preserve selected reader/draft identities during account switching; exclude active receives from removal/cleanup/storage maintenance until cancellation finishes.
- [x] Bound pending work and UI notifications through cancel/requeue and roster replacement; validate loopback TLS/STARTTLS credential separation, UI and NativeAOT lifecycle.
  See [independent account receives](m3-account-receive-queue.md). These jobs save one latest INBOX header page, not complete-folder synchronization. OAuth and wider provider/target acceptance remain open.
- [x] Add per-account navigation/status and an optional unified saved inbox with account-attributed rows and saved-text reading.
  Existing saved search remains explicitly account-scoped; notifications belong to M5. The unified view is offline/read-only and bounded to 50 newest saved INBOX UIDs per account, with explicit Refresh and no automatic receiving.
  See [unified saved inbox](m3-unified-saved-inbox.md); live unified synchronization, cross-account search and unified mail actions remain future work.
- [x] Persist bounded per-account start-folder and ordered pinned-folder preferences with strict atomic storage.
- [x] Restore each account's chosen start folder from local headers at startup/switch, with stale/wrong-account guards and no automatic networking.
- [x] Provide explicit account-scoped pinned-folder shortcuts, preserving saved-only navigation and disabled-account use.
- [x] Add bounded navigation Reload/Reset controls with corrupt/future-state isolation and save-failure feedback.
- [x] Include navigation in verified backup/restore, keep it on cache clear and exercise published NativeAOT lifecycle checks.
  See [five account navigation items](m3-account-navigation.md). A bounded optional unified saved-inbox view is implemented below; broader per-account orchestration remains open.
- [x] Persist validated per-account sender aliases with an explicit provider-permission label.
- [x] Offer primary/alias selection before draft creation; retain the pinned account/From
  across restart and reject submission if that address is no longer configured.
- [x] Persist a validated per-account default sender and initialize new drafts from it.
- [x] Add an opt-in unique receiving-address reply policy, restricted to configured
  To/Cc addresses and overridden by explicit sender choice.
- [x] Add a separate per-account reply signature, preserving new-message/forward
  signatures and existing draft text. See [composition defaults](m3-composition-defaults.md).
- [x] Exclude primary and all configured aliases from reply-all's own-recipient expansion.
  See [three sender-identity subitems](m3-sender-aliases.md). Real-provider certification
  remains open; alias-specific names/signatures are implemented below.
- [x] Persist validated presentation overrides only for configured primary/alias sender addresses.
- [x] Pin per-address display names in new drafts and preserve them in SMTP/Sent MIME across restart.
- [x] Apply per-address plain-text signatures to newly started messages and forwards, with inheritance/suppression.
- [x] Apply per-address reply signatures after explicit/default/unique receiving-address selection.
- [x] Add account editor inheritance/reset controls and verify profile backup/restore plus NativeAOT identity checks.
  See [five sender identity items](m3-sender-identity-defaults.md) for exact scope and remaining gaps.
- [x] Refuse account removal for owned active/uncertain/confirmed mail changes, rechecking configuration/drafts/mutation ownership around backup and final deletion.
- [x] Compare persisted sender override collections structurally for conditional removal, preserving changed-confirmation and partial-password-cleanup recovery behavior.
  See [account removal guards](m3-account-removal-guards.md) for scope and validation.
- [x] Separate disable, recovery-backed account removal, local downloaded-mail
  cleanup and remote actions. Guard draft and mutation ownership, and offer explicit
  keep or forget choices for the account's passwords. Expanded orphan metadata and
  credential recovery remains a follow-up.
- [x] Inspect downloaded header/body/mailbox counts for one stable account without networking.
- [x] Create verified recovery snapshots before transactionally clearing only one account's cached mail across epochs.
- [x] Preserve other accounts' search/index/checkpoints and all drafts/configuration/credentials; reset only target download progress.
- [x] Add separate Keep-first local-mail confirmation with draft flush, receive quiescence, cancellation and reader reload/failure release.
- [x] Validate account-scoped cleanup, restore and injected failures through regression and published NativeAOT checks.
  See [five account downloaded-mail cleanup items](m3-account-downloaded-mail-cleanup.md). Broader account-removal recovery acceptance remains open.
- [x] Discover up to 64 unconfigured account IDs with retained downloaded mail, excluding authoritative configured IDs without importing configuration.
- [x] Report per-ID old-epoch header/body/current-mailbox counts and capped-list coverage without receiving or deleting mail.
- [x] Recheck orphan status under configuration locks before verified recovery backup and scoped transactional cleanup, preserving other accounts and local edits.
- [x] Add explicit List/select/Keep-first cleanup controls, including when no accounts remain, with draft flush, receive quiescence and cancellation/failure release.
- [x] Validate re-added-account refusal, legacy/corrupt configuration, retention/restore and published NativeAOT orphan cleanup.
  See [five removed-account mail items](m3-orphan-downloaded-mail.md). Other orphan configuration/credentials and broader removal recovery remain separate work.

**Exit:** two accounts with overlapping UIDs and folder names can read/send/restart
independently. Test switching during receive/render/send, disabled/offline accounts,
credential edits, aliases, and account removal with unfinished drafts.

## M4 — Everyday mail workflows

**Started 2026-11-02:** read-only named-mailbox protocol access now preserves
folder-scoped identities and continuation cursors. Bounded LIST discovery now
preserves server hierarchy and attributes. The Windows reader now selects folders,
opens their saved headers/bodies offline, and receives/pages/downloads within the
selected mailbox. Special-use discovery and persisted per-account role overrides
now map Sent/Drafts/Archive/Trash/Junk. Explicit read/unread changes now verify
server state and update cached flags. Star/unstar is now persistent across restart;
verified UID MOVE now has a capability-gated service and destination identity
contract. Reader move/archive/trash controls now reconcile confirmed moves in the
cache and retain downloaded bodies under verified destination identities. Verified
one-level move undo is now available for the current session. Confirmed permanent
deletion from Trash uses scoped UID EXPUNGE. Confirmed cache-repair receipts now
survive restart; MOVE/delete network-phase intent and uncertain-outcome review are now implemented;
read/star intent and typed cache repair are also implemented. Broader recovery
acceptance remains pending.
See [M4 folder progress](m4-folders.md) and [UID MOVE foundation](m4-moves.md).

- [x] Browse folders and map Sent/Drafts/Archive/Trash/Junk via server capabilities
  and explicit per-account overrides. Report ambiguous, missing and unverified roles;
  retain offline navigation. Broader provider/native UI acceptance remains open.
- [x] Add explicit read/unread changes with mailbox/UID checks, verified server
  outcomes, scoped cache updates and no automatic replay after uncertain outcomes.
- [x] Add explicit star/unstar, received/starred-state display, durable cache flags
  and recovery-backed schema migration with confirmed/unknown outcome handling.
- [x] Add explicit move/archive/trash controls with verified destination identity,
  transactional cached-body transfer and local-only retry after cache failure.
- [x] Add session-scoped one-level move undo using a verified reverse MOVE and
  newly assigned UID, with local-only reconciliation retry after cache failure.
- [x] Add separately confirmed permanent deletion from the verified Trash role,
  requiring UIDPLUS/scoped UID EXPUNGE with no automatic replay or broad fallback.
- [x] Persist confirmed MOVE cache-repair receipts and repair them locally at Windows startup.
- [x] Persist confirmed permanent-deletion cleanup receipts with idempotent local repair.
- [x] Include repair receipts in verified profile backup/restore and expose manual repair,
  pending-work notices/mutation guards and cleanup protection. See [cache recovery](m4-cache-recovery.md).
- [x] Persist MOVE intent before network work and publish verified outcome receipts
  before returning confirmed results.
- [x] Persist permanent-deletion intent and retain uncertain outcomes across restart
  without replaying server commands or deleting cached mail.
- [x] Provide backed-up, separately confirmed review/dismissal of uncertain records,
  with active-operation and confirmed-receipt guards. See [mutation intents](m4-mutation-intents.md).
- [x] Persist read/unread intent and verified flag outcomes before local reconciliation.
- [x] Persist star/unstar intent and retain uncertain outcomes without server replay.
- [x] Repair typed confirmed flag receipts locally, preserving bodies/other flags,
  and include uncertain flags in backed-up review. See [flag recovery](m4-flag-recovery.md).
- [x] Validate IONOS IMAP TLS/password sign-in, folder/header/body/original-source reads and SMTP STARTTLS/password sign-in with the configured local account and sanitized NativeAOT probe evidence.
- [x] Verify one separately authorized plain-text self-message is accepted and observed in INBOX, with durable one-shot/replay protection and no other recipients or mailbox changes.
  See [provider acceptance](m4-provider-acceptance.md). This single account/configuration does not certify Sent copies, mutations/fault recovery, other providers/authentication or targets.
- [ ] **Deferred acceptance.** Complete broader provider, crash/power-loss fault and target-platform recovery checks.
- [x] Inventory bounded received MIME attachments from a verified saved original, excluding attachment text from the reader body.
- [x] Validate attachment transfer encodings, MIME boundaries and bounded structure before accepting an inventory.
- [x] Save a selected part through source re-verification, exact identity/checksum matching and atomic safe filenames.
- [x] Add explicit inline reader inventory/save/cancel controls with current-message guards and copyable save receipts.
- [x] Verify byte fidelity, failure isolation, retention and published NativeAOT extraction with synthetic fixtures.
  See [five received-attachment items](m4-received-attachments.md): 2 MiB originals, 32 attachments, separate bounded exports; sending and larger-message support remain open.
- [x] Decode RFC2231 single extended filename/name values with explicit charset bytes and ordinary-name fallback precedence.
- [x] Join bounded, order-independent filename continuations, including mixed encoded/plain segments and split multibyte characters.
- [x] Parse quoted filename parameters with semicolons/escaped quotes and preserve legacy names without double decoding.
- [x] Reject ambiguous, malformed and oversized extended names during extraction while retaining tolerant body preview and safe output paths.
- [x] Verify international filename extraction/save in regression and published NativeAOT application/TLS/STARTTLS fixtures.
  See [five attachment filename items](m4-attachment-filenames.md). Streaming, compose/send and larger messages remain open.
- [x] Add and remove bounded local files in Compose. Retain exact attachment bytes across
  draft restart and profile backup. Send multipart MIME through the existing submission
  journal and preserve the parts in configured Sent copies.
- [x] Frame outgoing SMTP DATA in bounded chunks while preserving dot stuffing and
  cancellation. Exercise a multipart round trip over loopback TLS. See the
  [attachment workflow and limits](m4-compose-attachments.md).
- [x] Encode multipart attachments in bounded chunks for SMTP DATA and IMAP Sent
  copies. Verify the announced IMAP literal size and preserve private Bcc handling.
- [x] Support up to 8 MiB of attachments per draft and 12 MiB per received original
  in the Windows x64 product scope. Stream verified originals to disk, report
  byte progress for downloads, attachment reads and large uploads, and preserve
  cancellation and safe extraction. The reader skips non-inline attachment decoding.
  File-backed drafts and MIME extraction beyond these bounds are deferred follow-ups.
- [x] Provide indexed local full-text search within the current account with
  folder, date, sender and unread filters. Describe saved-content coverage and
  distinguish it from server search and downloading older mail.
  The [offline header-search storage foundation](m4-search.md) now provides scoped,
  bounded sender/subject matching and read/star filters. The reader now exposes
  current-folder and optional all-saved-folders search within one account, with explicit coverage, offline-only body opening and
  cancellation, rolling UTC received-date filters (7/30/365 days), and bounded
  next-page navigation preserving the submitted filters. Sender-only/subject-only
  matching and flag/date searches without a keyword are also available. Saved
  plain-text body previews can now be searched alone or with headers, with explicit
  truncation/download coverage. Custom UTC calendar intervals now support open
  endpoints, explicit validation and fixed submitted bounds across paging/retry.
  [Complete retained-body indexing](m4-retained-body-search.md) now finds text beyond
  the preview with independent sender-header filtering, versioned retention and
  explicit old/incomplete/over-limit coverage. Broader accessibility/target acceptance remains open.
- [x] Retain complete supported decoded reader text separately from the bounded display preview, refusing incomplete/over-limit data as complete coverage.
- [x] Upgrade cache v3 to v4 after a verified recovery copy and rebuild the derived v2 index explicitly without deleting old indexes on refusal/cancellation.
- [x] Expose separate complete-body and headers/complete-body phrase modes while preserving literal/indexed preview semantics.
- [x] Add independent saved-sender filtering with account/folder/date/read/star/download filters, sender-only queries and submitted-filter paging.
- [x] Report retained-body coverage and validate restart, MOVE/delete/epoch isolation, source quota rollback, backup restore and named controls at 100%/200% text scale.
  See [retained-body search](m4-retained-body-search.md) for bounded MIME scope and validation.
- [x] Add backed-up schema-v3 source revision tracking for search consistency.
- [x] Add an optional, separately stored and rebuildable SQLite FTS5 saved-text index.
- [x] Maintain a valid index transactionally on header/body/MOVE/delete changes and reject stale indexes.
- [x] Expose indexed word-phrase field modes with existing filters and key-based paging.
- [x] Add Settings inspect/rebuild/disable controls, backup exclusion and cache-clear lifecycle.
  See [five indexing items](m4-indexed-search.md) for coverage, limits and validation.
- [x] Complete all/read/unread filtering in saved search, including flag-only queries.
- [x] Complete all/starred/unstarred filtering and read/star combinations in saved search.
- [x] Add downloaded/not-downloaded body filtering, retained across paging and retries.
  See [three search controls](m4-search-state-coverage.md) for exact scope and evidence.
- [x] Page saved search across current-account folders with UTF-8 binary folder/UID ordering and existing literal/indexed filters.
- [x] Bind all-folder continuations/history to a bounded account-specific saved-mailbox epoch fingerprint; explain scope changes and require a fresh search.
- [x] Add explicit Current folder/All saved folders scope with submitted-query pinning, bounded controls and no automatic searches.
- [x] Attribute folders in result rows/semantics and the reader, use full-key list IDs, and preserve exact offline-only body selection and context guards.
- [x] Verify cross-folder boundaries, Unicode order, epoch changes, indexed modes, stale results and published NativeAOT search/restart behavior.
  See [five account-wide saved-search items](m4-account-search.md). This remains one-account saved-preview search; full-body indexing and cross-account search remain open; the separate unified saved-inbox view is read-only.
- [x] Add per-account plain-text signatures, initialized once for new drafts and
  retained unchanged in restored/existing draft text. See [signature progress](m4-signatures.md).
- [x] Add a visible attachment-reference reminder in editable drafts, explaining
  current plain-text-only sending. See [three everyday subitems](m4-three-everyday-items.md).
- [x] Add bounded previous-page navigation to saved search with submitted-query
  history and no server receive. See [three everyday subitems](m4-three-everyday-items.md).
- [x] Add copyable parsed-header/threading inspection in the reader, including
  offline metadata and an explicit raw-source coverage label.
- [x] Add a durable, bounded, explicitly populated recipient book partitioned by account ID.
- [x] Provide recipient add/update/remove controls that preserve draft recipients on removal.
- [x] Offer at most ten local name/address prefix suggestions with pinned-draft account scope.
- [x] Insert selected saved addresses into To/Cc/Bcc through validation and autosave without sending.
- [x] Include saved recipients in verified profile backup/restore and published NativeAOT checks.
  See [five saved-recipient items](m4-saved-recipients.md) for exact behavior and limits.
- [x] Add a bounded durable draft collection with CAS updates and a verified pre-library backup.
- [x] Save and close an active draft without losing raw edits or submission outcomes.
- [x] Browse saved drafts by account and atomically resume/swap while retaining the pinned sender.
- [x] Separately confirm removal of a selected saved draft without changing active/server mail.
- [x] Preserve all drafts in backup/restore/cache cleanup and guard retained owners during account removal.
  See [five multiple-draft items](m4-multiple-drafts.md) for storage compatibility and validation.
- [x] Support multiple autosaved drafts, explicit saved-recipient completion, and a visible manual outbox. Preserve the current explicit
  accepted/rejected/unknown SMTP states and independent Sent-copy outcome.
- [x] Project current and saved local records into an account-scoped outbox without networking or new queue storage.
- [x] Filter independent unsent/in-progress/review/accepted outcomes, treating interrupted saved operations as unknown.
- [x] Provide copyable account/identity/submission details and honest current-save/coverage guidance.
- [x] Open retained records in Compose through guarded save/atomic swap without sending, retrying Sent copies or losing raw edits.
- [x] Add Mail menu/Ctrl+5 access, live progress browsing, modal-lifecycle guards and published NativeAOT recovery checks.
  See [five visible outbox items](m4-visible-outbox.md). Background queue dispatch and scheduling remain M5 work.
- [x] Open explicit local `.eml` files read-only and inspect bounded escaped source, without executing markup or assigning server UIDs.
- [x] Import an explicit opened snapshot into account-scoped atomic local storage, preserving exact bytes and independent local GUID receipts.
- [x] List/reopen imports explicitly with account/close/selection/cancellation guards and disabled-account support.
- [x] Include irreplaceable imported originals in verified profile backup/restore and retain them through cache cleanup.
- [x] Decode standalone local MIME into a bounded text alternative, excluding attachment text and active HTML resources, with explicit source/text switching.
- [x] Show bounded local `.eml` HTML inline on Windows after an explicit action. Keep selectable text available and remove links and images from the render input.
- [x] Export a verified selected import as exact original bytes with flushed atomic promotion and no overwrite or automatic launching.
- [x] Remove only an explicitly confirmed import after verifying its exact receipt/bytes in a completed whole-profile recovery backup, with Keep focused first.
- [x] Guard management operations against stale account/selection/close results and verify recovery restore, failed/missing-target backups, cancellation and quota-slot reuse.
  See [local originals](m4-local-originals.md) for limits, compatibility and evidence. Larger originals and printing remain open.
- [x] Provide bounded `.eml` open/import/export and original-message source inspection.
  Track larger originals with streaming attachments and defer printing to the renderer follow-up.
- [x] Retrieve bounded complete original source by full message identity using read-only TLS/EXAMINE/UID operations and size verification.
- [x] Reject mismatched UID/offset, malformed/duplicate/oversized body responses and incomplete source rather than misattributing/exporting bytes.
- [x] Save original .eml bytes atomically with unique filenames, checksum and independent count/byte limits; never overwrite or automatically open files.
- [x] Queue sibling source-export/catalog/attachment operations through bounded process-local coordination before acquiring the interprocess file lock; preserve queued cancellation, receipt quotas and subsequent writer progress.
- [x] Add explicit reader-scoped source export with copyable path/checksum, cancellation, failure/retry and stale/account/maintenance guards.
- [x] Validate original octets and export retention/backup boundaries in regression, TLS/STARTTLS probes and published NativeAOT execution.
  See [five original-source export items](m4-original-source-export.md). Open/import, export browsing after restart, general attachments and printing remain open.
- [x] Re-read a current export locally with directory/filename/length/checksum verification and no network access.
- [x] Format a bounded original-byte prefix with explicit escaping, normalization and truncation coverage.
- [x] Provide inline read-only selectable Show/Hide source controls with bounded scrolling and focus return.
- [x] Copy only the displayed source prefix explicitly, with no clipboard reads and failure/hidden/stale guards.
- [x] Reject stale/canceled/replaced/queued source reads and verify reader focus plus published NativeAOT/protocol lifecycle behavior.
  See [five source inspection items](m4-original-source-inspection.md) for current-export scope and remaining work.

- [x] Persist a bounded source-generated receipt catalog after original file promotion, retaining exact message identity without absolute stored paths.
- [x] List receipted originals for the current account/folder/epoch/UID after restart, without fetching or guessing uncataloged file identities.
- [x] Verify a selected local original before explicitly enabling source inspection and received-attachment extraction without another download.
- [x] Add List/select/Use/Cancel controls with no automatic selection, safe focus and stale/canceled/replaced-result guards.
- [x] Validate catalog compatibility/corruption/quota, file changes, separate backup/copy/cache-clear retention and published NativeAOT reuse.
  See [five saved-original reuse items](m4-saved-originals.md). General file browsing, arbitrary import/open and printing remain open.

- [x] List receipted originals across the current account's folders/epochs with bounded account-specific catalog access and no cache/body requirement.
- [x] Provide a Saved originals library with explicit List/select/Verify controls and selectable full account/message identity and checksum details.
- [x] Reuse verified inline byte-source preview and attachment extraction from the library without fetching or automatically opening files.
- [x] Add Mail menu/Ctrl+6 access with close/account/selection/disposal guards, canceling hidden work and clearing stale details.
- [x] Verify disabled/no-cache library use, account isolation, queued operations, cache-clear retention and published NativeAOT library navigation/rendering.
  See [five saved-original library items](m4-original-library.md). The library handles cataloged exports; arbitrary file import/open and printing remain open.

**Exit:** folder mutations reconcile correctly, attachment round trips preserve bytes,
search describes its scope, and drafts/attachment references survive restart.
Lost SMTP acknowledgements never trigger automatic duplicate sending.

## M5 — Sync, offline operations, and real providers

- [ ] Add bounded incremental synchronization, fair per-account scheduling, periodic
  refresh/IMAP IDLE, reconnect backoff, manual refresh, and visible last-sync state.
  Negotiate optional capabilities; retain a correct baseline fallback.
- [ ] Reconcile deletions/expunges, flag changes, moved folders, UIDVALIDITY resets,
  retention, and concurrent edits from other clients. Commit data and checkpoints
  consistently so interrupted sync does not lose mail or skip changes.
- [ ] Persist supported offline mutations with explicit conflict/replay rules and
  recoverable errors. Keep queued sends separate: unknown submission outcomes
  require reconciliation/user action, never blind replay.
- [ ] Implement system-browser OAuth with PKCE where required, protected refresh
  tokens, expiry/revocation/reauthorization handling, and provider configuration.
  Bring this forward for any chosen first provider that requires it; native Mail
  itself gains no JavaScript engine from browser-based authentication.
- [ ] Maintain a tested provider/authentication/capability matrix covering IMAP,
  SMTP, Sent-copy behavior, special folders, and large messages. Controlled fixtures
  remain the default; live checks need dedicated accounts and recorded results.
- [ ] Add opt-in notifications with privacy controls, quiet hours, and durable
  deduplication. State whether background receive requires the application running;
  account for Android lifecycle/network/battery restrictions.

**Exit:** interrupted sync and offline/reconnect cycles converge without duplicate
rows or lost edits; token expiry and one failing account recover independently.
Only tested provider/authentication combinations are advertised.

## M6 — Publish/release parity and supported targets

Use the checked-in Writer/Cells Publish workflows as patterns: selectable platforms,
one product version, reusable build jobs, variant validation, signing, regression
tests, and a release job gated on every selected target. Keep Mail-specific runtime
checks and package requirements instead of copying another application's assumptions.

| Target | Current baseline | Planned release gate |
| --- | --- | --- |
| Windows x64 + ARM64 | Publish matrix and NativeAOT/framework-dependent variants exist. | Preserve both; native-architecture execution of profile, reader, HTML, and send fixtures; upgrade and signing validation. |
| Linux x64 | Project exists; `Main` is a placeholder. | Implement host/input/clipboard, secure credentials, inline rendering, and profile paths; then NativeAOT + framework-dependent packages and real desktop checks. |
| Linux ARM64 | No advertised Mail release target. | Add only after dependency/toolchain proof and native runner/device acceptance. |
| Android | APK/AAB jobs and native head exist. | Release signing and verification, stable application ID/version code, install/update/device tests, credential/storage/lifecycle and inline-reader parity. Validate Android's own AOT/toolchain separately from desktop NativeAOT. |
| WebAssembly/browser | No Mail web head. | Feasibility decision only: direct IMAP/SMTP, secrets, browser storage, and no-JavaScript constraints need a separate design. Writer/Cells web output does not establish Mail feasibility; exclude from initial release parity. |
| macOS | No Mail head. | Later feasibility spike for native hosting, credentials, graphics, signing/notarization, and AOT dependencies; no support claim yet. |

- [ ] Add target-selection and reusable invocation inputs matching suite integration
  needs, reject empty/unsupported selections, and test release asset completeness.
  Gate tagging/release creation on product regression tests and all selected builds;
  disabled jobs must not accidentally block or bypass the gate.
- [ ] Retain one version/tag across executable, About, manifests, packages, Android
  version code, and release notes; serialize version allocation and test retries.
- [ ] Verify package contents and runtime requirements for each variant. AOT does
  not imply one-file output: ship required native storage/graphics libraries or
  deliberately validate static linking. Ship complete framework-dependent folders.
- [ ] Add signed Android release artifacts with certificate fingerprint checks;
  define Windows signing and Linux package/signature policy. Separate unsigned
  validation artifacts from supported distribution and keep signing keys in CI secrets.
- [ ] Provide checksums, dependency/license inventory, build provenance, install and
  recovery guidance, schema compatibility notes, and tested profile-preserving upgrades.
  Start with portable packages; add installers/package repositories after update and
  uninstall behavior is defined. Automatic updates follow a signed update design.
- [ ] Record startup, memory, large-mailbox/search responsiveness, and rendering budgets
  from measured baselines. Include keyboard, screen-reader, high contrast, DPI/IME,
  localization, and RTL checks in each advertised platform's acceptance evidence.

**Exit:** a selected-target dry run produces verifiable packages with matching
versions; failed selected jobs prevent a release. Clean-machine install, upgrade,
restart/offline read, send/receive, and inline HTML pass on every advertised target.
The Windows AOT lane stays mandatory; Linux AOT becomes mandatory when Linux ships.

## M7 — Additional completeness after the core

- [ ] Conversations from References/In-Reply-To with safe fallback grouping, local
  contacts/address completion, saved searches, tags, and configurable shortcuts.
- [ ] Explainable opt-in rules, templates, snooze/reminders, and spam-folder actions.
  Clarify local versus server execution and behavior while the app is closed.
- [ ] Evaluate rich email composition with plain-text alternatives and interoperable
  MIME, without JavaScript; do not couple it to finishing inline received-mail HTML.
- [ ] Assess calendar invitations/contact exchange, S/MIME/OpenPGP with explicit key
  management, mailbox export/backup formats, JMAP, and advanced server search as
  separately scoped features. Never imply cryptographic verification from a UI badge
  without implementing and testing its trust model.

## First implementation slices

1. M0: storage/profile ADR, account-scoped contracts, and published AOT regression fixture.
2. M1: migrate existing files; persist one inbox's headers and selected bodies; prove
   offline restart and crash recovery before adding background synchronization.
3. M2: embed the renderer in the reader; retain resource restrictions, text fallback,
   and a tracked containment gate. Accept against native UI fixtures.
4. M3: enable two saved accounts end to end, including sender-pinned drafts and
   overlapping UID tests. Then extend folders, attachments, and mutations.
5. In parallel with these implementation slices, improve selected-target release
   orchestration; complete the Linux host before offering Linux downloads.

Completion evidence should name source revision, package versions, target/architecture,
fixture or provider, automated results, and remaining manual checks. Update this plan
as slices land; do not mark planned behavior complete based solely on a build.
