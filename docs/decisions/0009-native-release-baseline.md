# Decision 0009: Native release baseline and dependency policy

Date: 2026-11-02. Status: accepted direction; broader release orchestration pending M6.

Preserve Windows x64/ARM64 NativeAOT and framework-dependent publish variants and
one product version across selected targets. Linux is not publishable until its
placeholder head becomes a tested host. Android retains its separate packaging/AOT
toolchain. Browser/WebAssembly is excluded from initial parity: a Mail design must
resolve protocol access, storage, credentials, and the no-JavaScript requirement
before a target can be selected. Writer/Cells workflows are orchestration references.

Publish-Windows runs the dependency gate against the RID-specific restored runtime
graph after publish, before smoke acceptance or packaging. The gate rejects known
JavaScript/scripting engines and dynamic compilation runtime packages, including
transitive dependencies. Runtime asset names are also checked. Build/analyzer-only
tools are not application runtime dependencies. This is an enforceable known-engine
guard, not proof that arbitrary dependencies contain no dynamic code: the NativeAOT
compiler and runtime smoke tests remain essential. New engines require expanding
the deny rules; no blanket exclusions or warning suppressions should hide them.

Keep a published-binary profile save/reopen and credential fixture under isolated
temporary state, never the real user profile. A uniquely generated test account
must have its test credential removed in finally. Existing shell rendering remains
part of --smoke-test, together with multipart MIME parsing and actual HTML rastering.
The separately published ProtocolProbe exercises production IMAP/SMTP adapters
against controlled loopback TLS/STARTTLS servers; it is not part of the shipped app.
The Windows publisher additionally reopens the synthetic profile and OS credential
in a second instance of the same executable. Its diagnostic accepts only a fixture
GUID beneath the fixed temporary smoke root, never an arbitrary profile path.
This verifies process restart persistence, not the as-yet unimplemented profile migration.

NativeAot.Substitutions.xml remains scoped to its current compatibility backend.
Re-audit it with every renderer dependency upgrade. The publisher enforces the
reviewed package/substitution fingerprints; see [the current audit](../renderer-aot-audit.md).
Track unsuppressed IL2xxx/IL3xxx
warnings per package/revision; every suppression needs a narrow rationale and a
runtime regression fixture. No-JavaScript design helps AOT but does not prove
reflection, codec, native library, or serializer compatibility. Packages may need
native companion files; never infer single-file deployment from PublishAot alone.
