# M0 Windows x64 evidence

Synthetic NativeAOT probe snapshots from 2 November 2026. See the
[M0 progress record](../../m0-foundation-status.md) for commands, scope, and limitations.
The validation manifest identifies the base revision and dirty working tree;
these files do not certify a committed/released build or another architecture.

- storage-results.json: runtime checks and one synthetic timing sample.
- storage-inventory.json: resolved package/license metadata and published binary hashes.
- protocol-results.txt: TLS/STARTTLS receive/send fixture outcomes.
- net-session-results.json: consumed Broiler.Net preview.4 session-isolation/policy checks.
- net-session-inventory.json: published Net probe packages/licenses and executable hash.
- validation-manifest.json: source/SDK/runtime identity of the probe run.

Raw logs, executable/symbol files, and generated product packages remain ignored
under artifacts. No provider credentials or real mail appear in these snapshots.
The validation manifest was refreshed when all three native probes were rerun for
the Net review; the earlier storage/protocol samples are retained as historical
measurements rather than overwritten with another timing sample.
