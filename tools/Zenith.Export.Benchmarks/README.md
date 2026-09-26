# Export benchmark

Sequential native CGL measurements using the original Synthesia X ZRP and the same FFmpeg exporter as the app. Requires .NET 9, FFmpeg on `PATH`, and the original assets prepared according to the repository README. No application window is opened.

```sh
dotnet run -c Release --project tools/Zenith.Export.Benchmarks -- \
  tests/fixtures/demo.mid artifacts/performance/export-paired 120 2 both \
  x264-medium,videotoolbox-20Mbps
```

Positional arguments:

| Argument | Default |
| --- | --- |
| MIDI path | `tests/fixtures/demo.mid` |
| Output directory | `artifacts/performance/export-baseline` |
| Frames | `120` |
| Repetitions | `2` |
| Encoding overlap | `false`; accepts `true` or `both` |
| Comma-separated modes | `render-only,x264-medium,x264-ultrafast,videotoolbox-20Mbps` |

Resolution is 1920 × 1080, 60 fps, SSAA 1. Each run loads a fresh pack, renders ten negative-time warmup frames, and then renders from time zero. Setup is measured separately. `both` alternates serial then overlap within each repetition. The tool writes `results.json` after each run and a separate MP4 for each encoding mode/repetition/overlap setting. Use an idle machine and run GPU workloads sequentially for comparisons.

`RenderIncludingReadbackMs` includes scene work, GPU completion, readback, frame allocation, and flipping. `BetweenCallbacksMs` also includes frame copying and exporter scheduling; it is not a pure pipe or encoder measurement. `LastWriteAndDrainMs` includes the last pipe write and encoder completion. `ManagedAllocatedBytes` is process-wide managed allocation during the timed section, not peak memory or GPU memory. Independent script instances can use different random values, so these output files are not a deterministic image-quality comparison.

See [measurements, correctness checks, and limitations](../../docs/PERFORMANCE.md).
