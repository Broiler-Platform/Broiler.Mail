# Explicit production HTTPS acceptance diagnostic

This opt-in console diagnostic compiles the production remote-image broker and uses its normal DNS-pinned sockets and platform certificate validation. It contacts only a fixed W3C PNG and three badssl certificate fixtures. It accepts no URL override, profile, account, mail, credential, custom resolver or certificate trust setting. It is not invoked by Mail startup or CI. Public fixtures require internet access and can change; a failure must be investigated rather than bypassing TLS.

Build locally for the current supported host, then invoke explicitly with a new report filename:

```powershell
dotnet publish eng/Broiler.Mail.ImageNetworkProbe/Broiler.Mail.ImageNetworkProbe.csproj -c Release -r win-x64 -o artifacts/image-network-probe
./artifacts/image-network-probe/Broiler.Mail.ImageNetworkProbe.exe --public-https-check --output artifacts/https-check.json
```

The public image must load as a bounded PNG. The three certificate endpoints must yield no broker image. Because the broker intentionally reports coarse `NetworkError`, a second default `SslStream` handshake over the same production DNS-pinned connector independently requires successful authentication for W3C and `AuthenticationException` for each negative fixture. There are no certificate callbacks or custom trust policies. Exit 0 means all four checks passed; exit 1 means a check failed; exit 2 means the explicit arguments were missing. The report is created without overwriting an existing file. Logs and reports contain fixed public URLs and coarse outcomes, not mailbox data.

This does not certify renderer containment, every resource path, other architectures, provider protocols or accessibility. See the [retained Windows x64 evidence](../../docs/evidence/m2-production-https-win-x64/README.md).
