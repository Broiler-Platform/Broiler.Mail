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
Since 4 October it also takes `-Workloads`, `-Scales`, `-Detail`, `-Strict`, and `-Evaluate`; see
[Harness additions](#harness-additions-4-october-2026). One workload can be run directly:

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

Machine for this baseline: AMD64 Family 25 Model 33 (AMD Zen 3), 16 logical processors,
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

Proposed for the UI side measured here, and not tied to a reference machine until one is agreed:

- frame build p95 ≤ 8 ms for every interactive workload, leaving half of a 16.7 ms frame for
  rendering and presentation;
- input to frame p95 ≤ 16.7 ms;
- no frames while idle;
- steady interactions (scroll, splitter, typing) allocate ≤ 256 KB per frame.

At the baseline, scroll, splitter, typing, and theme met the frame-build target; select (9.14 ms
p95) and resize did not, and every workload but resize met the input-to-frame target. Only typing
and theme met the allocation target. With optimization 1, scroll, splitter, typing, and theme meet
the allocation target as well; select and resize remain above it, and select's build p95 (8.73 ms,
8.4 ms in the published-package run) is still just over 8 ms.

## Next steps

1. CPU-sample the resize and select frames (re-wrapping at a new width) to find their remaining cost.
2. Add GPU render time once Broiler.Graphics publishes `FrameRendered`.
3. Cover long HTML in the preview window (the `long-html` fixture now provides the document) and
   100 % / 200 % display scales. Tiles below the first were painted from the wrong offset at any scale
   other than 100 % until UI-11 fixed it, so earlier preview observations at 150 % do not apply.

## Harness additions, 4 October 2026

These extend the harness without changing what a default run records: a run without `--detail`
times each frame as one span exactly as above, and its report keeps every field of the first
version under the same name (`reportVersion` 2 adds fields only). Numbers in this section come
from short smoke runs that checked the harness works; they are not a new baseline. The official
runs are still to be taken on a quiet machine.

**Detail timers (`--detail`, `Measure-UI.ps1 -Detail`).** Each frame is split into draining posted
UI work, measuring, arranging, and building the render list (the roots are measured and arranged
first, so `UiSession.RenderFrame` then only renders; the drawn output is the same). Each input's
dispatch is timed, and so is the host's render-and-present call: the time from the end of the
frame build until the render window's `WM_PAINT` that built it returns (for resize, until
`SetWindowPos` returns; the frame window's own `WM_PAINT` draws nothing and is not counted).
That is Direct2D drawing, `EndDraw`, and a vsynced `Present` on the UI thread, CPU wall time that
can include waiting for a buffer; it is not GPU execution or the moment the frame reaches the
screen. The report adds p50/p95/p99/max for `dispatchMs`, `drainMs`, `measureMs`, `arrangeMs`,
`renderListMs`, `renderPresentMs`, and `inputToPresentMs`. The timers cost a little per frame, so
compare detailed runs only with detailed runs; the summary keeps them in rows of their own. Tests
run detailed splitter and resize demos in a real window, and a measured input and a resize in a
real preview, and check these samples; others check that the inbox shell is laid out once before
`RenderFrame` and draws the same as a plain frame. One difference from a plain frame remains:
invalidations raised while the timer measures or arranges are cleared by that frame's
`RenderFrame` instead of staying in `UiSession.Invalidations` for the next one. The host is asked
to repaint either way, and nothing reads that list today.

**HTML preview workloads.** Both run on the `long-html` fixture, open the reader's HTML preview
(900×700 DIPs, its default), and report the preview window's frames after it has opened:

| Workload | Sequence |
| --- | --- |
| long-html | wheel down to the end of the document and back to the top, three notches (96 DIPs) per input |
| preview-zoom | Ctrl+Plus to 300 %, Ctrl+Minus to 50 %, and Ctrl+Plus back to the opening zoom, one level per input |

The preview's tile cache counts, only while measured, the tiles found in the cache (hits), tiles
drawn because they were not (misses), misses for a tile drawn before and evicted since
(re-rasters after eviction), evictions, and tiles discarded by a new layout, width, zoom, or scale.
A tile drawn after a zoom is a miss, not a re-raster: it shows other content than the discarded
one, so preview-zoom's re-raster cost is its misses and their raster time, next to its discards.
The report names the zoom the preview opened at (`previewOpeningZoom`), and a measured preview at
a simulated scale names that scale in its title, as the main window does. Each drawn
tile's raster time (Broiler.HTML painting on the preview thread, single-threaded per ADR-0005) and
upload time (handing the pixels to the renderer) are recorded, with the peak cache size and the
document's layouts. Frames that drew a tile are reported apart from frames that found every tile
cached, and opening (from the reader's button to the preview's first frame) is reported on its
own. The tile counts include the opening's tiles.

At zoom 1 the long newsletter is cut at 32,768 DIPs, 32 tiles, so scrolling down and back draws
48 tiles: all 32 on the way down, and on the way up the 16 that the 16-tile cache no longer holds,
with 32 evictions. The smoke runs, the last of them on the final harness, counted exactly that.

**Simulated scales (`-Scales 100,150,200`).** The script runs each workload `-Repeat` times at each
scale with `--scale`, and the summary labels those rows "simulated N %". Mail then lays out,
rasterizes, and converts input at that scale on this display; in the preview workloads the measured
preview takes the same scale. Windows' own DPI, the window frame, `WM_DPICHANGED`, and moves between
monitors are not exercised, so these rows are not real DPI evidence (UI-13's display checks remain
open).

**Budgets.** `scripts/ui-budgets.json` holds the proposed targets above for every workload: frame
build p95 ≤ 8 ms and input to frame p95 ≤ 16.7 ms for the interactive ones (scroll, select, typing,
theme, resize, splitter, long-html, preview-zoom), and for long-html also frame build p95 ≤ 8 ms over
the frames that found every tile cached; no frames while idle; allocation per frame ≤ 256 KB for the
steady interactions (scroll, typing, splitter, long-html), judged at p50 as above, so up to half of
a run's frames may allocate more; and no unpainted steps for any workload but idle. The summary
marks each workload, scale, and detail setting pass or over against the median of its runs (the
mean of the two middle runs for an even count) and shows unpainted steps. The budgets are proposed,
not agreed, and not tied to a reference machine. They are report-only: the script exits with 1
only with `-Strict`, for a result over budget or a budget without data (a run that drew nothing, or
a report without the field). The preview workloads draw new tiles synchronously on the preview
thread, so their build and input-to-frame p95 are over the targets; their notes say so, and the
cached-tile row shows long-html's other frames. The startup line takes the median over the seven
baseline workloads' runs at each scale, as above, and gives the preview workloads apart.

`-Evaluate <directory>` summarizes stored reports, including those of the first version, without
running the app. It writes `summary-evaluated.md` and `summary-evaluated.json` into `-Output`, or
beside the reports, headed with the evaluation date and when the reports were written, and leaves
the measurement's own summary as it was. Evaluated that way, the published preview.14 reports above
put select's build p95 (8.4 ms) and resize over the targets. In a run, the summary covers only the
reports that run wrote, even in a directory that holds earlier ones.

**Still not measured.** Text-layout call counts: Broiler.Graphics exposes no way to observe the
active `BTextMeasurer` provider, and Broiler.UI has no layout counters, so counting needs a public
upstream API rather than reflection into either package. Also GPU execution and present-to-screen
latency (DXGI statistics in Broiler.Graphics), input through the native message path, and
correctness checks after each workload.
