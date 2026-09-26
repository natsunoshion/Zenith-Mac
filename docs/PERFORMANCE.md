# macOS export performance

## Available choices

- **Software H.264** retains the existing CRF or bitrate controls. The default codec remains `libx264`.
- **Apple H.264 hardware encoding** is an explicit option using `h264_videotoolbox` and a bitrate in kbps. It does not use CRF, and its quality is not equivalent to a given x264 CRF value. The exporter passes `-allow_sw 0`: if hardware encoding is unavailable or busy, the error retains FFmpeg's details and suggests selecting software H.264. Custom FFmpeg arguments are still appended last and can explicitly override this policy.
- The exporter now overlaps one encoder write with the following frame's render. Scripted rendering itself stays sequential. Video and optional mask writes can run together.

VideoToolbox provides access to Apple's video encoding facilities; FFmpeg's encoder translates `allow_sw=0` to the requirement for a hardware encoder. These changes do not remove the application's OpenGL readback or CPU-to-FFmpeg pipe. [Apple VideoToolbox](https://developer.apple.com/documentation/videotoolbox), [FFmpeg 7.1 encoder source](https://www.ffmpeg.org/doxygen/7.1/videotoolboxenc_8c_source.html).

## Measurements

Measured on 2026-09-26: Apple M3 Pro, 36 GiB memory, macOS 15.7.4, .NET 9, FFmpeg 7.1.1. The GL driver reported `Apple M3 Pro / 4.1 Metal - 89.4`; this is the current OpenGL backend, not a native Metal renderer.

Each sample rendered the original Synthesia X ZRP's default settings with the generated 128-note `tests/fixtures/demo.mid`, at **1920 × 1080, 60 fps, SSAA 1**, for 120 frames (two output seconds), without audio, a mask, a General background image, or a drop shadow. Compilation, resource loading, and ten preroll warmup frames were timed separately and excluded from the totals below. The example is sparse: these results do not predict performance for dense black MIDI, heavy particle profiles, backgrounds, shadows, higher SSAA, or other machines, and do not establish optimal shader performance.

### Initial serial baseline

Two consecutive runs before enabling overlap:

| Mode | Total seconds, run 1 / 2 | Effective fps, run 1 / 2 |
| --- | ---: | ---: |
| Render and readback only | 0.455 / 0.438 | 263.8 / 273.9 |
| x264, CRF 17, medium | 1.654 / 1.595 | 72.6 / 75.2 |
| x264, CRF 17, ultrafast | 1.275 / 1.210 | 94.1 / 99.1 |
| VideoToolbox H.264, 20,000 kbps | 1.217 / 1.161 | 98.6 / 103.3 |

The render-only callback averaged 3.65–3.79 ms per frame. This includes CPU scene work, GPU completion, `glReadPixels`, allocation, and vertical flipping; it is **not an isolated GPU or readback measurement**. During x264 medium export, about 0.67 seconds were inside rendering callbacks and 0.75–0.79 seconds between them, with another 0.14–0.15 seconds for the last write and encoder drain. That made write/render overlap worth measuring. The presets and hardware bitrate produce different compression and quality; the table is a throughput comparison only.

### Paired overlap comparison

A separate run alternated serial and overlap in the same process for each repeat. This avoids presenting the fastest result from separate, noisy runs as the improvement:

| Mode | Serial seconds, run 1 / 2 | Overlap seconds, run 1 / 2 | Mean time reduction |
| --- | ---: | ---: | ---: |
| x264, CRF 17, medium | 1.608 / 1.348 | 1.158 / 1.253 | 18.4% |
| VideoToolbox H.264, 20,000 kbps | 1.179 / 1.141 | 0.966 / 0.964 | 16.8% |

All outputs contained 120 frames. These short runs establish a benefit for this workload, not a universal percentage. A separate CLI check with the original ZRP and hardware encoder produced 1920 × 1080 H.264 at 60 fps, exactly 120 decoded frames and 2.000 seconds.

`VideoExportOptions.PipelineEncoding` therefore defaults to `true`; callers can set it to `false` for comparison. It keeps one private pooled BGRA frame so callbacks may safely reuse their own memory. At 1080p that frame's payload is 7.91 MiB and the current shared-pool bucket is 8 MiB; at 4K the corresponding allocation is 32 MiB. Existing render buffers, optional mask storage, and encoder memory are additional costs. The completion callback is captured immediately after rendering, before advancing Scripted state. The last write is awaited before encoder completion, and cancellation settles outstanding writes before returning pooled memory. Progress can lead pipe delivery by one frame while a write is pending.

## Correctness checks and reproduction

The core suite passed **44 assertions** with `--audio --ffmpeg --videotoolbox --stress`. Export checks include a reused callback buffer, a dynamic completion condition that extends the duration estimate to 17 frames, identical decoded video **and mask** frame hashes between serial and overlap, cancellation while both pipes have pending data, and an actual hardware video/mask encode. Pixel equality here compares the two software export schedules; it does not claim equivalence between codecs or with Windows rendering.

Run after preparing the original assets as described in the main README:

```sh
dotnet run -c Release --project tools/Zenith.Export.Benchmarks -- \
  tests/fixtures/demo.mid artifacts/performance/export-paired 120 2 both \
  x264-medium,videotoolbox-20Mbps

dotnet run --project tests/Zenith.Core.SelfTest -- \
  --audio --ffmpeg --videotoolbox --stress

dotnet run -c Release --project src/Zenith.Cli -- render \
  tests/fixtures/demo.mid artifacts/hardware.mp4 \
  'assets/windows/Zenith/Plugins/Assets/Scripted/Resources/Synthesia X.zrp' \
  --width 1920 --height 1080 --fps 60 --duration 2 \
  --codec h264_videotoolbox --bitrate 20000
```

The benchmark writes individual MP4s and `results.json`, including render callback timing, callback gaps, encoder drain, managed allocations, and device identification. Local evidence was saved under `artifacts/performance/export-baseline/`, `export-paired/`, `export-correctness.txt`, and `cli-hardware/`. Generated evidence and original ZRP assets are not included in Git; run the commands to create your own results. See the [benchmark tool](../tools/Zenith.Export.Benchmarks/README.md) for its arguments.

## Remaining work: memory, GPU transfer, and Metal

The existing `ReadBgra` allocates a frame and flips rows on the CPU. The 120-frame render-only test allocated about 1.016 GB of managed memory in total, of which frame payloads alone account for 995 MB. Raw 1080p60 BGRA is about 498 MB/s before additional copies. A render-into-buffer or explicit frame-lease API could reduce these allocations, but needs clear ownership until both encoder pipes have finished.

OpenGL pixel buffer objects can defer readback, but immediately mapping the same buffer simply waits for completion. A useful implementation needs multiple buffers and delayed consumption, while preserving frame order, Scripted state, cancellation, and the final frame. Apple's guide and the Khronos extension specification describe this overlap requirement. No PBO optimization was implemented or benchmarked in this change. [Apple pixel transfers](https://developer.apple.com/library/archive/documentation/GraphicsImaging/Conceptual/OpenGL-MacProgGuide/opengl_texturedata/opengl_texturedata.html), [Khronos ARB_pixel_buffer_object](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_pixel_buffer_object.txt).

A native Metal backend is a larger migration: shaders, blending, SSAA, buffer ownership, and synchronization must preserve the original plugin results. Apple's guidance uses multiple in-flight buffers to avoid CPU/GPU stalls. It is a direction for investigation, not a measured speedup or completed backend in this repository. [Apple Metal migration](https://developer.apple.com/videos/play/wwdc2019/611/), [Apple Metal buffering](https://developer.apple.com/library/archive/documentation/3DDrawing/Conceptual/MTLBestPracticesGuide/TripleBuffering.html).

## MPI

MPI defines communication between processes; adding it alone would not remove this application's readback, copies, or encoder waits. Existing scripts retain particles, random state, and per-note metadata across frames. Splitting one animation into independently rendered chunks would require reproducible state snapshots or potentially expensive preroll before every chunk. Independent jobs are a simpler candidate for multi-process scheduling, subject to shared GPU and hardware-encoder limits. No MPI renderer, distributed speedup, or single-Mac benefit has been demonstrated here. [MPI Forum overview](https://www.mpi-forum.org/docs/mpi-4.1/mpi41-report/node10.htm).
