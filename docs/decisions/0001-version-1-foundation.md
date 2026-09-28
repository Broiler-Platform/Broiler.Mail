# Decision 0001: Version 1 foundation

Status: accepted for the first implementation step. Date: 2026-09-27.

Start on Windows with .NET 10 and the existing platform-neutral Core, Application,
and Infrastructure projects. Use published Broiler.UI control packages and the
Broiler.Graphics Windows backend, pinned in `Directory.Packages.props`. No sibling
checkout is required to build. Native services stay in the Windows project.

The first usable slice is one non-secret account profile plus application settings.
Store these locally in versioned JSON with validation, guarded writes, and explicit
recovery from invalid files. Use a stable account ID from the outset; expose only
one account until the multiple-account milestone. Apply theme and initial window
dimensions on restart to keep this step small.

The planned mail baseline is manually configured IMAP with certificate validation,
implicit TLS or mandatory STARTTLS, and a provider-supported password/app-password
authentication path. This is a protocol direction, not a claim of tested provider
support. The initial provider/authentication acceptance fixture and mail-library
version will be selected with the connection-test step. Provider-required OAuth
must be implemented before that provider is advertised as supported.

Credentials belong in Windows protected storage, separately from profile JSON.
This step therefore has no password field, credential writes, or connection attempts.
SMTP and HTML preview remain version 2 work.

Version 1 step 1 is complete when a profile and settings can be edited, validated,
saved, and reloaded across application instances, failures remain visible without
claiming a save, and invalid existing files are preserved. Automated workflow and
persistence tests cover these criteria; native visual/interaction review remains
part of the full version 1 acceptance process.
