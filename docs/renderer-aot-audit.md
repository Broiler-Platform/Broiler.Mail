# Renderer NativeAOT compatibility audit

Reviewed 2026-11-02 against restored Broiler.HTML.Image and
Broiler.HTML.Image.Compat 0.1.0-preview.30 assets, with the Core/Dom/Orchestration
runtime assemblies fingerprinted alongside them (not an assumption about sibling
source revisions). Exact assembly SHA-256 values are recorded in
[renderer-aot-audit.json](../eng/renderer-aot-audit.json), together with a
line-ending-normalized fingerprint of NativeAot.Substitutions.xml.

Two method substitutions remain narrowly scoped:

1. CompatProvider.TryLoadDefaultProvider: the fallback uses Assembly.Load and
   reflected bootstrapper discovery. Mail's HtmlRuntime explicitly registers
   StubCompatBootstrapper before creating a container. The substitution removes
   only fallback discovery; EnsureDefaultProvider still throws if registration is
   missing. Published HTML layout/raster acceptance now exercises this registration.
2. CompatCanvasOperations.InvokeVoid: Save/Restore/Translate dispatch uses runtime
   reflection. The pinned compatibility assembly's StubCanvas methods were inspected
   through their method bodies: all three contain only the IL `ret` opcode (2A).
   The managed raster pipeline owns transforms; substituting this dispatch is valid
   only for that registered backend, not for arbitrary future canvas providers.

The supporting source implementations are CompatProvider.cs,
CompatCanvasOperations.cs, and StubBitmapCompatSurface.cs in Broiler.HTML. The gate
pins the consumed binary assets because sibling source alone is insufficient.
Any renderer package or substitution change fails the Windows publisher until the
audit is repeated. Refreshing hashes without reviewing semantics is not an audit.

Validation: actual table/text layout and nonempty raster pixels passed inside both
Windows x64 published variants on this checkout. Native storage/protocol probes
also compiled with ILLink/ILCompiler warnings treated as errors. No IL2xxx/IL3xxx
warnings were observed in these local publish logs. ARM64 results must come from
the configured matching-architecture CI runner; they were not executed locally.

This audit addresses compatibility only. HTML containment and native-window/input
acceptance are separate gates and remain open.
