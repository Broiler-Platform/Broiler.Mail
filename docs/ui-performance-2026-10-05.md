# UI performance, 5 October 2026

The second UI-12 record, taken at the end of the UI round of 4–5 October 2026. It compares Mail's
`main` with the two local branches of that round, on the same machine and with the same harness. It
also records the first full runs of the HTML preview workloads, the frame-phase timers (`--detail`),
and simulated display scales, all added to the harness on 4 October. Workloads, columns, and what
they include are defined in the
[2 October baseline](ui-performance-baseline-2026-10-02.md), whose
[harness additions](ui-performance-baseline-2026-10-02.md#harness-additions-4-october-2026) describe
the new options. The acceptance runs of the same round are in
[ui-acceptance-2026-10-05.md](ui-acceptance-2026-10-05.md).

## How the runs were made

```powershell
scripts/Measure-UI.ps1 -Executable <build> -Output <directory>
scripts/Measure-UI.ps1 -Executable <build> -Output <directory> -Workloads select,resize,type,long-html -Detail
scripts/Measure-UI.ps1 -Executable <build> -Output <directory> -Workloads scroll,select,resize -Scales 100,200
```

- **Script:** `scripts/Measure-UI.ps1` and `scripts/ui-budgets.json` from `claude/ui-13-acceptance`.
  Neither has changed since `c7fe45a`.
- **Builds:** NativeAOT win-x64 builds, published as for the acceptance runs.
- **Launching:** the script started every process the same way (PowerShell `Start-Process`), so the
  startup figures compare only with each other.
- **Repeats:** three runs per row (the default), and each cell is the median of the three.
- **Machine state:** the runs were made on 5 October between 09:42 and 09:55, straight after the
  final2 acceptance runs (the final3 acceptance runs came later, 12:44–13:35), with nothing else
  building, testing, or measuring.
- **Machine:** AMD64 Family 25 Model 33 (AMD Zen 3), 16 logical processors, Windows 11 Enterprise
  10.0.26200, one monitor at 150 %. Release NativeAOT, .NET 10.0.12, x64.
- **Window:** 1100×720 DIPs, light theme.

| Build | Revision | Packages |
| --- | --- | --- |
| main | `757e81b` | Broiler.UI 0.1.0-preview.17, Hosting preview.5, Graphics preview.7, Native preview.6, Input preview.5 |
| published | `f7fdb0b` on `claude/ui-13-acceptance` | the same as main |
| adoption | `afb0926` on `claude/ui-09-upstream-adoption` (the final2 acceptance build) | Broiler.UI 0.1.0-preview.18-local.6, Hosting 0.1.0-preview.7-local.4, Native preview.7, Graphics preview.7, Input preview.5 (local packs, unpublished) |

The final verified revisions were not measured again:

- `claude/ui-13-acceptance` at `4c36950` adds reader text and layout fixes to `f7fdb0b`.
- `claude/ui-09-upstream-adoption` has since been rebased and rewritten, so `afb0926` is no longer
  on it. Its verified revision `e870135` uses UI local.7 and Hosting local.5, which add the
  scroll-stop and form-gap changes.

## Main, published, and adoption

Frame build in ms, as in the baseline. Each cell is the median of three runs.

| Workload | Build | Frames | Build p50 | Build p95 | Build p99 | Input→frame p95 | Alloc KB/frame p50 | Working set MB |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| idle | main | 0 | — | — | — | — | — | 58.9 |
| idle | published | 0 | — | — | — | — | — | 59.6 |
| idle | adoption | 0 | — | — | — | — | — | 57.8 |
| scroll | main | 300 | 1.16 | 1.33 | 1.77 | 1.57 | 63.5 | 76.1 |
| scroll | published | 300 | 1.16 | 1.34 | 1.46 | 1.48 | 65.6 | 76.3 |
| scroll | adoption | 300 | 1.47 | 1.68 | 1.87 | 1.83 | 70.9 | 74.4 |
| select | main | 41 | 8.79 | 9.35 | 9.65 | 9.81 | 333.6 | 75.3 |
| select | published | 41 | 8.77 | 9.25 | 9.60 | 9.94 | 336.4 | 75.6 |
| select | adoption | 40 | 9.10 | 9.66 | 10.04 | 10.30 | 342.8 | 75.2 |
| type | main | 601 | 1.08 | 1.19 | 1.32 | 5.12 | 62.5 | 76.1 |
| type | published | 601 | 1.09 | 1.19 | 1.35 | 5.12 | 62.5 | 76.4 |
| type | adoption | 600 | 1.06 | 1.17 | 1.50 | 5.19 | 62.1 | 76.5 |
| theme | main | 20 | 1.16 | 1.32 | 1.45 | 3.42 | 63.1 | 63.2 |
| theme | published | 20 | 1.30 | 1.43 | 1.65 | 3.13 | 69.3 | 63.6 |
| theme | adoption | 20 | 1.67 | 1.98 | 2.04 | 3.90 | 75.4 | 63.4 |
| resize | main | 30 | 13.62 | 14.11 | 15.46 | 18.69 | 436.2 | 73.3 |
| resize | published | 30 | 14.40 | 15.15 | 15.32 | 18.27 | 442.3 | 73.6 |
| resize | adoption | 30 | 14.10 | 14.65 | 14.93 | 18.56 | 449.3 | 72.2 |
| splitter | main | 61 | 5.22 | 6.54 | 6.80 | 6.79 | 243.8 | 75.5 |
| splitter | published | 61 | 5.30 | 6.40 | 7.13 | 6.69 | 249.0 | 75.5 |
| splitter | adoption | 60 | 5.64 | 6.93 | 7.26 | 7.21 | 254.5 | 75.4 |

No step went unpainted in any run, and no frame was drawn while idle.

Startup with the seven baseline workloads (21 runs each), first frame / prepared fixture
interactive:

| Build | Startup median (ms) |
| --- | --- |
| main | 578 / 601 |
| published | 579 / 600 |
| adoption | 576 / 599 |

These figures include `Start-Process`, as in the baseline.

The `main` row is the comparison point for this record. The 2 October tables were taken on Broiler.UI
preview.14, and this record does not attribute the difference between them and `main`.

## Frame phases (`--detail`, published build)

The phase timers cost a little per frame, so these rows compare only with other detailed runs. Times
are p50 / p95 in ms.

- Dispatch is per input.
- Drain, measure, arrange, and render list add up to the frame build.
- Render+present is the host's Direct2D drawing and vsynced `Present` on the UI thread. It is CPU
  time that can include waiting for a buffer, not GPU execution.

| Workload | Frame build p50 / p95 | Dispatch | Drain | Measure | Arrange | Render list | Render+present | Input→present p95 |
| --- | --- | --- | --- | --- | --- | --- | --- | ---: |
| select | 8.75 / 9.15 | 0.34 / 0.59 | 0 / 0 | 3.51 / 3.80 | 2.92 / 2.98 | 2.30 / 2.43 | 2.33 / 2.56 | 12.10 |
| type | 1.09 / 1.17 | 3.53 / 3.94 | 0 / 0 | 0.34 / 0.36 | 0.28 / 0.29 | 0.47 / 0.52 | 0.75 / 0.86 | 5.95 |
| resize | 13.78 / 14.31 | — | 0 / 0 | 8.34 / 8.70 | 3.01 / 3.24 | 2.39 / 2.53 | 2.96 / 3.30 | 21.54 |
| long-html | 0.10 / 179.57 | 0.03 / 0.13 | 0 / 0 | 0 / 0 | 0.01 / 0.02 | 0.09 / 179.56 | 0.38 / 16.61 | 181.94 |

- **Measure is the largest phase** of select (3.5 of 8.75 ms) and resize (8.3 of 13.8 ms).
- **Rendering and presenting add 2.3–3.0 ms on the UI thread.** That brings resize to 21.5 ms from
  input to present at p95, over a 16.7 ms frame. Select stays at 12.1 ms.
- **Typing spends more on dispatch than on the frame.** Dispatching a key takes 3.5 ms (p50), about
  three times the frame build. This was not profiled.
- **In long-html, the tile raster falls in the render-list phase.** Frames that draw a tile take
  about 180 ms there. The other frames build in 0.1 ms.

## Simulated display scales (published build)

`--scale` renders Mail at that scale on this 150 % display. Windows' DPI, the window frame,
`WM_DPICHANGED`, and monitor moves are not exercised, so these are not real DPI results. The 150 %
row is the system scale of the run above.

| Workload | Scale | Build p50 | Build p95 | Build p99 | Input→frame p95 | Alloc KB/frame p50 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| scroll | simulated 100 % | 1.16 | 1.34 | 1.47 | 1.47 | 65.6 |
| scroll | system 150 % | 1.16 | 1.34 | 1.46 | 1.48 | 65.6 |
| scroll | simulated 200 % | 1.15 | 1.34 | 1.44 | 1.50 | 65.6 |
| select | simulated 100 % | 8.71 | 9.17 | 10.08 | 9.63 | 336.4 |
| select | system 150 % | 8.77 | 9.25 | 9.60 | 9.94 | 336.4 |
| select | simulated 200 % | 8.68 | 9.29 | 9.68 | 9.73 | 336.4 |
| resize | simulated 100 % | 13.76 | 15.30 | 15.88 | 17.94 | 442.3 |
| resize | system 150 % | 14.40 | 15.15 | 15.32 | 18.27 | 442.3 |
| resize | simulated 200 % | 14.10 | 14.65 | 14.81 | 17.89 | 442.3 |

The frame build does not change measurably with the scale, and allocation per frame is identical.
Drawing at a different scale happens in rendering and presentation, which this table does not
include. Startup (9 runs each), first frame / interactive: 561 / 607 ms at 100 %, 567 / 619 ms at
200 %.

## HTML preview

Both workloads run on the `long-html` fixture and measure the preview window (900×700 DIPs, scale
1.5, opened at zoom 1) after it has opened. Tile counts and times include the opening.

| | long-html, published | long-html, adoption | preview-zoom, published | preview-zoom, adoption |
| --- | ---: | ---: | ---: | ---: |
| Steps / frames (drawing tiles) | 672 / 672 (47) | 672 / 672 (47) | 24 / 24 (24) | 24 / 24 (24) |
| Open to first frame, ms | 541 | 542 | 535 | 524 |
| Frame build p50 / p95 / p99, ms | 0.10 / 180.1 / 190.4 | 0.11 / 182.5 / 190.2 | 344.1 / 371.2 / 375.1 | 342.0 / 380.0 / 387.7 |
| Build p95, frames with every tile cached / drawing tiles, ms | 0.13 / 195.5 | 0.16 / 198.4 | — / 371.2 | — / 380.0 |
| Input→frame p95, ms | 180.3 | 182.6 | 371.4 | 380.9 |
| Tile hits / misses | 1,339 / 48 | 1,339 / 48 | 0 / 25 | 0 / 25 |
| Re-rasters after eviction / evictions / discards | 16 / 32 / 0 | 16 / 32 / 0 | 0 / 0 / 24 | 0 / 0 / 24 |
| Raster per tile p50 / p95, ms | 174.5 / 188.5 | 177.3 / 192.3 | 173.7 / 196.0 | 171.8 / 199.0 |
| Upload per tile p95, ms | 7.8 | 8.2 | 8.8 | 9.2 |
| Peak tile cache, MB | 124.9 | 124.9 | 7.8 | 7.8 |
| Layouts (total ms) | 1 (150) | 1 (151) | 25 (4,073) | 25 (3,910) |
| Allocation per frame p50, KB | 4.6 | 4.6 | 79,882 | 79,891 |
| Working set, MB | 739 | 739 | 198 | 194 |

**long-html.** The cache behaved as the 4 October harness notes predict. Scrolling down and back
draws 48 tiles: the 32 of the cut document on the way down, and on the way up the 16 that the
16-tile cache no longer holds, after 32 evictions. The peak cache, 125 MB, is within the 256 MB cap.
Frames that find every tile cached build in 0.13–0.16 ms. A frame that brings a new tile into view
takes about 180–200 ms, because it draws that tile first.

**preview-zoom.** Each zoom step lays the document out again (25 layouts, about 160 ms each) and
draws the one tile in view (about 174 ms at p50). Together that is about 340 ms, close to the
step's 344 ms at p50. Each zoom frame allocates about 80 MB.

**The single-thread raster.** Tiles are drawn by the preview window's own thread alone
([ADR-0005](decisions/0005-isolated-html-preview.md)). Broiler.HTML's parallel raster made that
STA thread wait in a way that dispatched input and UI Automation calls in the middle of a tile under
NativeAOT, which froze input. ADR-0005 measured the cost at 200 %: 110–120 ms a tile alone against
54–65 ms in parallel, about twice as long. The parallel raster was not measured in this run. The
follow-up is a sequential raster option in Broiler.HTML that does not block an STA thread; it would
let the parallel raster, and its speed, return.

## Budgets (proposed, report-only)

The proposed targets of the baseline (`scripts/ui-budgets.json`) are not tied to a reference
machine. No run used `-Strict`, so nothing failed. The summaries mark:

| Build or run | Results over budget | Which |
| --- | ---: | --- |
| main | 3 | select frame build p95; resize frame build p95 and input to frame p95 |
| published | 7 | the same 3; long-html and preview-zoom frame build p95 and input to frame p95 |
| adoption | 7 | the same 7 |
| published, `--detail` | 5 | select build p95; resize build p95 and input to frame p95; long-html build p95 and input to frame p95 |
| published, simulated 100 % and 200 % | 6 | select build p95 and resize build p95 and input to frame p95, at each scale |

Every other budget passed:

- **Idle:** no frames.
- **Painting:** no unpainted steps.
- **Allocation for steady interactions:** scroll, typing, splitter, and long-html stayed within
  256 KB per frame. The splitter came closest: 243.8 KB on main, 249.0 KB published, and 254.5 KB
  in the adoption build.
- **Cached preview frames:** long-html's frames with every tile cached built within 8 ms
  (0.13 / 0.16 ms).

Select (334–343 KB) and resize (436–449 KB) allocate more than 256 KB per frame. The budget does not
judge them, since it covers steady interactions only.

The preview workloads are over by construction, because a frame that needs a new tile draws it
first. Whether these budgets, and which reference machine, should become a gate with `-Strict` is a
decision for the user.

## Observations

- **The adoption build is slower in four workloads.** Against the published build, at p50:
  - scroll +0.31 ms (1.16 → 1.47);
  - theme +0.37 ms (1.30 → 1.67);
  - select +0.33 ms (8.77 → 9.10);
  - splitter +0.34 ms (5.30 → 5.64).

  In each, the p50s of the three runs of one build do not overlap those of the other. Typing
  (−0.03 ms) and resize (−0.30 ms) are not slower. The adoption build also allocates 5–7 KB more
  per frame in these workloads. The cause was not profiled. Scroll, theme, and splitter stay
  within the 8 ms target; select's p95 was over it in every build.
- **The adoption build draws one frame fewer** in select, type, and splitter (40, 600, and 60
  frames, against 41, 601, and 61), with every step painted. This was not investigated.
- **Resize on the published branch is slower than on `main`:** +0.78 ms at p50 (13.62 → 14.40) and
  +1.04 ms at p95 (14.11 → 15.15). Theme is +0.14 ms at p50. Again the p50s of the runs do not
  overlap. Neither was profiled. The branch also changed the `large-inbox` fixture (a notice above
  the list, 600 demo messages), and the scroll workload on it did not change measurably (1.16 ms
  on both).
- **long-html's working set is 739 MB**, against a peak tile cache of 125 MB. This was not
  investigated.
- **The preview fixture's interactive startup varied:** 597 ms published (6 runs), 697 ms in the
  published `--detail` run (3 runs), and 705 ms in the adoption build (6 runs). First frame was
  581–596 ms in all three. Too few runs to call a difference.
- **One timing test is sensitive to load.**
  `MeasurementTests.A_Detailed_Preview_Times_The_Dispatch_Phases_And_Paint_Of_A_Measured_Input`
  failed 2 of 40 full-suite runs during the round, both while other builds or tests were running,
  and passed whenever it ran alone.

## Still not measured

- **GPU and presentation.** GPU execution and the moment a frame reaches the screen. The
  render+present timer covers CPU time on the UI thread only. `FrameRendered` exists on
  Broiler.Graphics main, which is unpublished and holds unreviewed changes; whether to take it is a
  decision for the user.
- **Real input latency.** Inputs are synthesized above the native message path and the input
  bridge.
- **Real DPI.** Only the simulated scales above were run; there was no second monitor and no DPI
  change.
- **Text-layout call counts.** Neither Broiler.Graphics nor Broiler.UI exposes a public way to count
  them.
- **Correctness checks** after each workload.
- **Profiling.** No CPU sampling of select and resize (the baseline's next step), of the adoption
  build's extra 0.3–0.4 ms, or of the published branch's slower resize.
- **The final revisions.** The published stack at `4c36950` and its tip `12ceded`, and the adoption
  branch at `e870135` on UI local.7 and Hosting local.5.
- **The published releases.** The adoption figures have to be taken again once Broiler.UI
  preview.18 and Hosting preview.7 are published.
- **Other machines and ARM64.**
