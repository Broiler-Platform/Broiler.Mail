# IONOS live provider checks — Windows x64

Executed 2026-11-04 using the existing locally configured account with the user's authorization. The user separately approved exactly one plain-text self-message. No other send or mailbox mutation was authorized or performed.

A standalone NativeAOT `eng/Broiler.Mail.ProviderProbe` used the production receiver, SMTP tester/sender and read-only Windows credential adapter. IMAP used `imap.ionos.de:993`, TLS, password authentication; SMTP used `smtp.ionos.de:587`, STARTTLS, password authentication. Normal certificate validation remained enabled. The probe dependency gate passed (7 runtime libraries); no IL2xxx/IL3xxx publish warnings.

[Read-only results](read-only-results.json): all seven stages passed—existing configuration, IMAP TLS/authentication, folder discovery, three-header EXAMINE page, BODY.PEEK decoded body, complete original-source size verification and SMTP TLS/authentication/QUIT. No user profile/cache/credential writes or server mail changes were made.

[Self-message results](self-send-results.json): the server accepted the single approved message and the same Message-ID was observed in INBOX through read-only source retrieval. Subject: `Broiler.Mail acceptance check 3806579de9de4da99cd04a2744070472`. The body identifies it as an acceptance test and says no reply is needed. Sender and only recipient were the configured account itself. The message is left in INBOX; no Sent APPEND, flags, moves, deletion or cleanup occurred.

A flushed CreateNew one-shot intent precedes SMTP dispatch, is bound to the approved account/server snapshot and unique attempt, and prevents another call with that attempt after acceptance, rejection, unknown outcome or interruption. No automatic SMTP retries. Five synthetic regression cases validate recipient confinement, all submission outcomes, changed-account/cancellation refusal and interruption/replay protection. The plan/synthetic attempt journal remain in ignored `artifacts/`.

Retained results contain provider hosts, a one-way account fingerprint, coarse stage results/counts and synthetic attempt identifiers only. No addresses, credentials, subject lists, message bodies, actual server UIDs or protocol transcripts are retained. Body/source bytes were not exported. The tool is opt-in, never run by application startup or CI. Do not rerun the send mode without a new explicit authorization.

This is one provider/account/configuration, not general provider certification. Live Sent-copy behavior, provider mutation/recovery and fault injection, other authentication methods/providers/platforms, large messages, attachment sending and renderer containment remain open.
