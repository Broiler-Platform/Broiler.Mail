# M4 provider acceptance

Updated 2026-11-04. Live IONOS Windows x64 checks now complement the synthetic TLS/STARTTLS fixtures. See [retained results and exact scope](evidence/m4-ionos-provider-win-x64/README.md).

| Configuration | Read-only IMAP | SMTP authentication | Explicit self-message | Not certified |
| --- | --- | --- | --- | --- |
| IONOS, IMAP TLS/993 + SMTP STARTTLS/587, password, Windows x64 NativeAOT probe | Passed authentication, folder listing, headers, decoded body and complete source | Passed | Accepted and observed in INBOX, one separately authorized attempt | Sent APPEND, mutations, fault/recovery, large attachments, OAuth, other targets |

The probe reads existing local configuration and protected credentials without opening/adopting/migrating the profile or repairing the cache. Start only after the account owner authorizes live access:

```powershell
dotnet publish eng/Broiler.Mail.ProviderProbe/Broiler.Mail.ProviderProbe.csproj -c Release -r win-x64 -o artifacts/provider-probe-win-x64
./artifacts/provider-probe-win-x64/Broiler.Mail.ProviderProbe.exe --read-only --profile <existing-directory> --output <sanitized-result.json>
```

The separately guarded `--plan-self-send` / `--send-to-self` modes are operator tools, not an application or CI behavior. Prepare and review the exact self-message, obtain explicit send authorization, retain the account snapshot and use a fresh approved attempt ID only once. A durable started intent deliberately refuses reuse after any outcome. Do not clear the journal or create a new attempt to work around an uncertain submission; reconcile it first. The tool never offers arbitrary recipient arguments or automatically writes a Sent copy.

Live diagnostics retain sanitized coarse results, not personal mail data or credential/transcript logs. A passed self-message proves that attempt's acceptance and observation; it does not guarantee future delivery or complete M4. The [completion audit](m0-m4-completion-status.md) retains the missing implementation and acceptance gates.

An additional authorized read-only [complete-header observation](evidence/m1-mailbox-snapshot-win-x64/README.md) passed against IONOS (11 INBOX headers). Its separate diagnostic mode is `--read-only-header-snapshot --profile <existing-directory> --output <new-sanitized-json>`; it performs no SMTP, body/source fetch, profile/cache migration/write or server mutation. It reserves a new report file and does not overwrite one. This is outside startup and CI and does not reuse send authorization.
