# UI performance baseline, 2 October 2026

This is the first measured baseline for roadmap package UI-12. It records what the UI thread spends
per frame on fixed demo fixtures, so later changes can be compared against numbers instead of
impressions. Run it again after any change that affects layout, rendering, invalidation, or input.

## How to reproduce

```powershell
scripts/Measure-UI.ps1 -Repeat 3
```

The script publishes the Windows app as NativeAOT (the shipped configuration), runs every workload
three times, and writes the JSON reports and `summary.md` to `artifacts/measurements/<timestamp>/`.
One workload can be run directly:

```powershell
Broiler.Mail.Windows.exe --demo large-inbox --measure scroll --report scroll.json
```

`--measure` needs a named demo scenario (fixed clock and data). The run waits until the scenario is
prepared, settles for 500 ms, runs the workload, prints the statistics, writes `--report`, and
exits. Inputs are synthesized and dispatched through the window's own input path one at a time; each
waits for the frame that shows it, followed by an 8 ms pause.

| Workload | Fixture | Sequence |
| --- | --- | --- |
| idle | inbox | three seconds without input |
| scroll | large-inbox (500 messages) | 150 wheel notches down, then 150 up, over the message list |
| select | inbox | Down arrow through 40 messages, each loading its body |
| type | large-draft | 600 characters typed into the composer body, Enter every 60, with autosave |
| theme | inbox | 20 live switches between the dark and light themes |
| resize | inbox | 30 window size changes |
| splitter | inbox | 60 keyboard moves of the inbox splitter |

## What is measured, and what is not

- **Frame build:** layout and render-list construction on the UI thread (`BuildRenderList`),
  including draining posted UI work. This is the part of a frame Broiler.Mail and Broiler.UI control.
- **Input to frame:** from dispatching a synthetic input to the end of the frame build that shows
  it. It includes the wait for the paint message, but not GPU rendering or presentation.
- **Allocated per frame:** managed bytes the UI thread allocated during the frame build.
- **Startup:** process start to the first frame, and to the prepared fixture being interactive.
- **Not included:** GPU rendering and presentation (`Broiler.Graphics` raises `FrameRendered` with
  the render duration only from its next release; preview.7 has no hook), native message delivery
  and the input bridge (inputs are synthesized above them), text-layout call counts, and HTML tile
  misses. Long HTML and other DPI scales are not covered yet.

## Results

Reference machine for this baseline: AMD64 Family 25 Model 33 (AMD Zen 3), 16 logical processors,
Windows 11 Enterprise 10.0.26200, display scale 150 %. Release NativeAOT build, .NET 10.0.12, x64.
Window 1100×720 DIPs, light theme. Each cell is the median of three runs; allocation totals were
identical across runs.

| Workload | Steps | Frames | Build p50 ms | Build p95 ms | Build p99 ms | Input→frame p95 ms | Alloc KB/frame p50 | Working set MB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| idle | 0 | 0 | — | — | — | — | — | 58.4 |
| scroll | 300 | 300 | 5.82 | 6.31 | 6.89 | 6.51 | 285 | 73.6 |
| select | 40 | 41 | 8.55 | 9.14 | 9.51 | 9.77 | 1,620 | 73.0 |
| type | 600 | 601 | 1.21 | 1.41 | 1.76 | 5.40 | 67 | 73.7 |
| theme | 20 | 20 | 2.83 | 3.08 | 3.12 | 4.32 | 233 | 64.6 |
| resize | 30 | 30 | 14.34 | 16.34 | 16.54 | 19.87 | 1,718 | 73.5 |
| splitter | 60 | 61 | 5.50 | 6.91 | 8.16 | 7.17 | 929 | 73.1 |

Startup: first frame 569 ms median, prepared fixture interactive 587 ms median (all 21 runs). These
startup figures include launching through PowerShell's `Start-Process`; started directly, the same
build reaches its first frame in about 230 ms. Compare startup only between runs launched the same way.

## Findings

- **Idle rendering stops.** No frame is drawn while nothing changes.
- **Resize is the expensive case.** The UI side alone takes 14–16 ms per size step and allocates
  about 1.7 MB per frame, so with GPU rendering added a resize step will not fit a 16.7 ms frame.
- **Allocation, not computation, stands out elsewhere.** Selecting a message allocates about
  1.6 MB per frame, a keyboard splitter move about 0.9 MB, and a wheel notch over the list about
  285 KB, while building those frames takes 5–9 ms. Typing is cheap per frame (67 KB, 1.2 ms).
- Allocation per run is deterministic, which makes it a reliable regression signal.

## Optimization 1: rich-edit allocations (Broiler.UI 0.1.0-preview.14)

Allocation sampling (`BROILER_MAIL_SAMPLE_ALLOCATIONS=1` on a JIT build with
`-p:EventSourceSupport=true`; see `AllocationSampler`) attributed most of the resize, select, and
splitter allocation to Broiler.UI's rich-text editor, which Mail uses for the reader and composer:

- `RichEditLayoutSettings.RunFont` built a new `BFontStyle` on every call, and layout and painting
  asked for one per run, segment, and measurement (about 40 % of sampled bytes);
- `foreach` over `RichTextParagraph.Runs` (an `IReadOnlyList`) boxed an enumerator per paragraph,
  and `RichTextParagraph.StyleAt` did the same for every character wrapping measured (about 33 %);
- painting re-split, re-shaped, and copied the text of every visible line on every frame, which is
  why scrolling the list allocated inside the reader.

Broiler.UI branch `claude/richedit-allocations` resolves fonts once per style (cleared when the
font or zoom changes), uses index loops and an index-based style lookup on these paths, and keeps
each visual line's segments across frames until the lines are rebuilt (lines holding an inline image
are not kept, since its size can change once decoded). Measured with that branch packed locally
(`0.1.0-preview.14-local.2`), same machine and script, median of three runs:

| Workload | Alloc KB/frame before → after | Build p50 ms before → after | Build p95 ms before → after |
| --- | --- | --- | --- |
| scroll | 285 → 102 | 5.82 → 3.93 | 6.31 → 4.44 |
| select | 1,620 → 310 | 8.55 → 8.16 | 9.14 → 8.73 |
| type | 67 → 61 | 1.21 → 1.14 | 1.41 → 1.25 |
| theme | 233 → 49 | 2.83 → 0.88 | 3.08 → 1.02 |
| resize | 1,718 → 408 | 14.34 → 13.69 | 16.34 → 16.26 |
| splitter | 929 → 228 | 5.50 → 4.98 | 6.91 → 6.15 |

Published as Broiler.UI 0.1.0-preview.14 (Broiler.UI#73) and consumed by Mail; a NativeAOT run
against the published package reproduced these numbers (allocation per frame identical; select
p50 8.05 ms, resize 13.50 ms, scroll 3.92 ms, theme 0.85 ms). Idle still draws no frames; startup is
unchanged when launched the same way. Resize and select are
now dominated by computation, mostly re-wrapping text at a new width, not allocation; finding that
cost needs CPU sampling rather than allocation sampling.

## Optimization 2: HTML preview tiles (Mail)

The preview window paints long documents as 1024-DIP tiles. Two problems, both in Mail:

- **Each tile was encoded to PNG and decoded again by the renderer.** Timed in a Release JIT build,
  a typical tile (1650×1536 pixels: 1100 DIPs at 150 %) took 314 ms for the encode and decode and
  2896×2896 took 557 ms; copying the same pixels takes 1–2 ms. Every new tile, on opening the preview
  or scrolling into new content, stalled the window by that much. Tiles now go to the renderer as
  pixel buffers (`BBitmap.ToPixelBuffer`), which is what the renderer stored after decoding anyway.
  The preview isolation tests dropped from 12 s to 3 s.
- **Memory was bounded only by tile count.** Sixteen tiles of width × DPI by 1024 × DPI pixels allow
  about 4.5 GB at 7680 DIPs and 300 %. A tile is now capped at 8 M pixels (rendered at a slightly
  lower scale and drawn stretched only when the cap applies; ordinary windows are unaffected) and
  the cache at 256 MB, evicting least recently used tiles by bytes as well as count.

## Proposed targets (to agree before they become gates)

On the reference machine, for the UI side measured here:

- frame build p95 ≤ 8 ms for every interactive workload, leaving half of a 16.7 ms frame for
  rendering and presentation;
- input to frame p95 ≤ 16.7 ms;
- no frames while idle;
- steady interactions (scroll, splitter, typing) allocate ≤ 256 KB per frame.

At the baseline, scroll, select, splitter, typing, and theme met the time target and resize did
not; only typing and theme met the allocation target. With optimization 1, scroll, splitter, typing,
and theme meet the allocation target as well; select and resize remain above it.

## Next steps

1. CPU-sample the resize and select frames (re-wrapping at a new width) to find their remaining cost.
2. Add GPU render time once Broiler.Graphics publishes `FrameRendered`.
3. Cover long HTML in the preview window (the `long-html` fixture now provides the document) and
   100 % / 200 % display scales. Tiles below the first were painted from the wrong offset at any scale
   other than 100 % until UI-11 fixed it, so earlier preview observations at 150 % do not apply.
