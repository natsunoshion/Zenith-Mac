using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using Zenith.Core.Export;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;

namespace Zenith.Mac;
public sealed class AppController : IDisposable
{
    readonly MainWindow window;
    readonly RenderSettings settings = new();
    readonly Dictionary<string, IPluginRender> modules = new();
    readonly SemaphoreSlim actions = new(1, 1);
    readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith-Mac", "settings.json");
    MidiSequence? midi;
    ScriptedPack? pack;
    string module = "classic";
    readonly object scriptedPaletteOwner = new();
    CancellationTokenSource? running;
    Task? runningTask;
    PreviewWindow? preview;
    PlaybackController? player;
    double seek = -1;
    double resumePosition;
    double scriptedScreenTime;
    int version;
    bool exporting, disposed, closing;
    MainWindowState State => window.State;
    bool PreviewIsRunning => State.IsPreviewing && !exporting && runningTask is { IsCompleted: false };
    PaletteSelection CurrentPalette => PaletteService.For(
        module == "scripted" ? scriptedPaletteOwner : modules[module].SettingsControl,
        module == "pfa" ? .8f : 1f);

    void RefreshPaletteControls()
    {
        var selection = CurrentPalette;
        if (module != "scripted")
        {
            var control = modules[module].SettingsControl;
            var field = control.GetType().GetField("palette");
            selection.Select(field?.GetValue(control) as string ?? selection.SelectedImage);
            field?.SetValue(control, selection.SelectedImage);
        }
        window.SetPalettes(selection.GetPaletteNames(), selection.SelectedImage, selection.Randomized);
    }

    public AppController(MainWindow window, string? settingsFilePath = null)
    {
        this.window = window;
        if (settingsFilePath != null) settingsPath = Path.GetFullPath(settingsFilePath);
        window.InitializeAssets(AssetPaths.Root);
        LoadSettings();
        foreach (var id in ModuleCatalog.BuiltinIds)
            modules[id] = ModuleCatalog.Create(id, settings);
        window.SetModules(modules.Select(p => new ModuleEntry(p.Key, p.Value.Name, p.Value.Description, PreviewPath(p.Key))).Append(new("scripted", "Scripted", "Uses user-made C# scripts (compiled in runtime) to give almost full control over rendering.\nExtremely customizable, relatively easy to use.", Path.Combine(AssetPaths.Root, "Previews/scripted.png"))), module);
        window.SetBuiltinSettings(module, modules[module].SettingsControl);
        LoadSkins();
        RefreshPaletteControls();
        window.ActionRequested += action => Queue(() => Handle(action));
        window.ModuleChanged += id => Queue(() => SelectModule(id));
        window.SkinChanged += path => Queue(() => LoadSkin(path));
        window.PaletteChanged += name =>
        {
            CurrentPalette.Select(name);
            if (module != "scripted")
            {
                var control = modules[module].SettingsControl;
                control.GetType().GetField("palette")?.SetValue(control, CurrentPalette.SelectedImage);
            }
            Interlocked.Increment(ref version);
        };
        window.ScriptSettingChanged += () => Interlocked.Increment(ref version);
        State.PropertyChanged += StateChanged;
        State.AudioToggleLabel = State.AudioEnabled ? "Disable Audio" : "Enable Audio";
        window.Closing += (_, e) =>
        {
            if (disposed) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            running?.Cancel();
            Queue(async () =>
            {
                try { await Stop(); }
                finally
                {
                    SaveSettings();
                    try { pack?.Dispose(); }
                    catch (Exception error) { Console.Error.WriteLine(error); }
                    pack = null;
                    disposed = true;
                    preview?.Close();
                    window.Close();
                }
            });
        };
        var args = Environment.GetCommandLineArgs();
        int load = Array.IndexOf(args, "--midi");
        if (load >= 0 && load + 1 < args.Length)
        {
            State.MidiPath = args[load + 1];
            window.Opened += (_, _) => Queue(() => Handle("load"));
        }

        int skin = Array.IndexOf(args, "--skin");
        if (skin >= 0 && skin + 1 < args.Length)
            window.Opened += (_, _) => Queue(async () =>
            {
                await SelectModule("scripted");
                await LoadSkin(args[skin + 1]);
            });
    }

    string? PreviewPath(string id)
    {
        var local = Path.Combine(AssetPaths.Root, "Previews", id + ".png");
        return File.Exists(local) ? local : null;
    }

    async void Queue(Func<Task> action)
    {
        await actions.WaitAsync();
        try
        {
            if (!disposed)
                await action();
        }
        catch (OperationCanceledException)
        {
            State.Status = "Stopped";
        }
        catch (Exception e)
        {
            State.Status = e.Message;
            if (!closing) await window.ShowError(e.Message);
        }
        finally
        {
            actions.Release();
        }
    }

    void LoadSkins()
    {
        var root = AssetPaths.Resolve(module == "textured" ? "Plugins/Assets/Textured/Resources" : "Plugins/Assets/Scripted/Resources");
        string[] extensions = [".zrp", ".zip", ".rar", ".7z", ".tar"];
        var skins = Directory.EnumerateFileSystemEntries(root)
            .Where(path => Directory.Exists(path) || extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .Select(path => new SkinEntry(path, Path.GetFileName(path), File.Exists(path)));
        window.SetSkins(skins, pack?.Path);
    }

    async Task SelectModule(string id)
    {
        bool resume = PreviewIsRunning;
        await Stop();
        module = id;
        LoadSkins();
        if (id == "scripted")
        {
            if (pack == null)
            {
                var path = AssetPaths.Resolve("Plugins/Assets/Scripted/Resources/Synthesia X.zrp");
                await LoadSkin(path);
            }
            else
                window.SetScriptPack(pack);
        }
        else
        {
            window.SetBuiltinSettings(id, modules[id].SettingsControl);
            if (id == "textured" && ((TexturedRender.Settings)modules[id].SettingsControl).currPack == null)
                await LoadSkin(AssetPaths.Resolve("Plugins/Assets/Textured/Resources/Default"));
        }

        RefreshPaletteControls();
        if (resume && midi != null)
            StartPreview(true);
    }

    async Task LoadSkin(string path)
    {
        bool resume = PreviewIsRunning;
        await Stop();
        State.Status = "Loading resource…";
        State.Progress = 0;
        State.IsBusy = true;
        try
        {
            if (module == "textured")
            {
                var type = Directory.Exists(path) ? TexturedRender.PackType.Folder : Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".zrp" => TexturedRender.PackType.Zrp,
                    ".zip" => TexturedRender.PackType.Zip,
                    ".7z" => TexturedRender.PackType.SevenZip,
                    ".rar" => TexturedRender.PackType.Rar,
                    _ => TexturedRender.PackType.Tar
                };
                var loaded = await Task.Run(() => new TexturedRender.PackLoader().LoadPack(path, type));
                if (loaded.error)
                    throw new InvalidDataException(loaded.description);
                var s = (TexturedRender.Settings)modules[module].SettingsControl;
                s.currPack = loaded;
                s.lastPackChangeTime = DateTime.UtcNow.Ticks;
                window.SetTexturedPack(loaded);
            }
            else
            {
                var loaded = await Task.Run(() => ScriptedPack.Load(path));
                pack?.Dispose();
                pack = loaded;
                window.SetScriptPack(pack);
                State.Status = $"{pack.Name}: {pack.Textures.Count} textures loaded";
            }
        }
        finally
        {
            State.IsBusy = false;
        }

        if (resume && midi != null)
            StartPreview(true);
    }

    async Task Handle(string action)
    {
        switch (action)
        {
            case "load":
                await Stop();
                State.Status = "Loading MIDI…";
                State.Progress = 0;
                State.IsBusy = true;
                try
                {
                    midi = await MidiSequence.LoadAsync(State.MidiPath, new() { Progress = new Progress<double>(p => State.Progress = p) });
                    resumePosition = 0;
                    State.MidiLoaded = true;
                    State.Status = $"{Path.GetFileName(State.MidiPath)} — {midi.NoteCount:N0} notes, {midi.Tracks.Length} tracks, {midi.DurationSeconds:0.00}s";
                    preview?.ConfigurePlayback(Path.GetFileName(State.MidiPath), midi.DurationSeconds);
                }
                finally
                {
                    State.IsBusy = false;
                }

                break;
            case "unload":
                await Stop();
                midi = null;
                resumePosition = 0;
                State.MidiLoaded = false;
                State.MidiPath = "";
                State.Status = "";
                preview?.ConfigurePlayback("Preview", 0);
                break;
            case "start-preview":
                await Stop();
                StartPreview();
                break;
            case "stop":
                await Stop();
                break;
            case "show-preview":
                if (preview != null)
                {
                    preview.Show();
                    preview.Activate();
                }
                break;
            case "start-render":
                await Stop();
                StartExport();
                break;
            case "reload-skins":
                LoadSkins();
                break;
            case "reload-script":
                if (pack != null)
                    await LoadSkin(pack.Path);
                break;
            case "reload-modules":
                await Stop();
                foreach (var p in ModuleCatalog.Discover(AssetPaths.Resolve("Plugins/Mac"), settings))
                    modules[p.LanguageDictName] = p;
                window.SetModules(modules.Select(p => new ModuleEntry(p.Key, p.Value.Name, p.Value.Description, PreviewPath(p.Key))).Append(new("scripted", "Scripted", "C# scripts, textures and particles")), module);
                break;
            case "randomize-palette":
                CurrentPalette.SetRandomized(State.RandomizePalette);
                Interlocked.Increment(ref version);
                break;
            case "reload-palettes":
                CurrentPalette.Reload();
                RefreshPaletteControls();
                Interlocked.Increment(ref version);
                break;
            case "new-palette":
            case "edit-palette":
                await EditPalette(action == "edit-palette");
                break;
            default:
                if (action.StartsWith("builtin:"))
                {
                    await HandleBuiltin(action);
                }

                break;
        }
    }

    async Task EditPalette(bool editExisting)
    {
        var selection = CurrentPalette;
        var name = await PaletteEditorWindow.ShowEditor(window, editExisting ? selection.SelectedImage : null);
        if (name == null) return;
        selection.Reload();
        selection.Select(name);
        if (module != "scripted")
        {
            var control = modules[module].SettingsControl;
            control.GetType().GetField("palette")?.SetValue(control, selection.SelectedImage);
        }
        RefreshPaletteControls();
        Interlocked.Increment(ref version);
    }

    async Task HandleBuiltin(string action)
    {
        var parts = action.Split(':', 3);
        if (parts.Length < 3)
            throw new ArgumentException("Invalid module action: " + action);
        var id = parts[1];
        var name = parts[2];
        if (name == "reload-palettes") { await Handle("reload-palettes"); return; }
        if (name is "new-palette" or "edit-palette") { await EditPalette(name == "edit-palette"); return; }
        if (id == "textured" && name.StartsWith("select-pack:", StringComparison.Ordinal))
        {
            await LoadSkin(name[12..]);
            return;
        }

        if (id == "textured" && name == "reloadPackButton")
        {
            var original = (TexturedRender.Settings)modules[id].SettingsControl;
            var old = original.currPack ?? throw new InvalidOperationException("Select a resource pack first.");
            bool resume = PreviewIsRunning;
            await Stop();
            var loaded = await Task.Run(() => new TexturedRender.PackLoader().LoadPack(old.filepath, old.filetype, new(old.switchValues), old.switchChoices));
            if (loaded.error)
                throw new InvalidDataException(loaded.description);
            original.currPack = loaded;
            original.lastPackChangeTime = DateTime.UtcNow.Ticks;
            window.SetTexturedPack(loaded);
            if (resume && midi != null)
                StartPreview(true);
            return;
        }

        if (name.StartsWith("randomize-palette:", StringComparison.Ordinal))
        {
            PaletteService.For(modules[id].SettingsControl, id == "pfa" ? .8f : 1f)
                .SetRandomized(bool.Parse(name[18..]));
            Interlocked.Increment(ref version);
            return;
        }

        if (name == "aura-changed")
        {
            bool resume = PreviewIsRunning;
            await Stop();
            PaletteService.ReloadAuras();
            if (resume && midi != null)
                StartPreview(true);
            return;
        }

        throw new NotSupportedException("Unsupported module action: " + name);
    }

    void ApplySettings()
    {
        int scale = (int)State.Ssaa;
        if (scale is < 1 or > 4 || State.Fps is < 1 or > 1000)
            throw new InvalidOperationException("SSAA must be 1–4 and FPS must be 1–1000.");
        if (!double.IsFinite(State.TempoMultiplier) || State.TempoMultiplier is <= 0 or > 1000)
            throw new InvalidOperationException("Tempo multiplier must be greater than zero and at most 1000.");
        if (State.FirstNote > State.LastNote)
            throw new InvalidOperationException("The first note must not be greater than the last note.");
        if (module == "scripted" && pack == null)
            throw new InvalidOperationException("Select a Scripted resource pack first.");
        settings.downscale = scale;
        settings.width = checked((int)State.Width * scale);
        settings.height = checked((int)State.Height * scale);
        settings.fps = (int)State.Fps;
        if (settings.width <= 0 || settings.height <= 0 || settings.width > 16384 || settings.height > 16384)
            throw new InvalidOperationException("Render dimensions exceed the GPU's 16384 pixel texture limit.");
        settings.timeBasedNotes = State.NoteSizeStyle == 1;
        settings.ignoreColorEvents = State.IgnoreColorEvents;
        settings.BGImage = State.UseBackground ? State.BackgroundPath : "";
        settings.Paused = State.Paused;
        settings.tempoMultiplier = State.TempoMultiplier;
    }

    SceneRenderer MakeRenderer() => new(midi!, settings, module == "scripted" ? null : modules[module], module == "scripted" ? pack : null,
        CurrentPalette.SelectedImage, paletteSelection: CurrentPalette)
    {
        ScreenTime = State.NoteScreenTime,
        FirstKey = (int)State.FirstNote,
        LastKey = (int)State.LastNote + 1
    };

    void SyncPreviewPlaybackState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(SyncPreviewPlaybackState);
            return;
        }
        preview?.SetPlaybackState(State.IsPreviewing && !exporting
            && running is { IsCancellationRequested: false }, State.Paused, State.AudioEnabled);
    }

    void RequestPreviewSeek(double position)
    {
        if (!State.IsPreviewing || exporting || running is not { IsCancellationRequested: false }
            || midi == null || !double.IsFinite(position)) return;
        // The UI commits dragging on release. Keep only the latest outstanding
        // request; the render loop performs the original renderer/audio reset.
        Interlocked.Exchange(ref seek, Math.Clamp(position, 0, midi.DurationSeconds));
    }

    async Task ReplayPreview()
    {
        // The stopped preview can remain visible while a separate export or
        // load runs. Its replay button must not cancel that other operation.
        if (midi == null || State.IsRendering || (State.IsBusy && !State.IsPreviewing)) return;
        await Stop();
        StartPreview();
    }

    void StartPreview(bool resume = false)
    {
        if (midi == null)
            throw new InvalidOperationException("Load a MIDI file first.");
        ApplySettings();
        if (!resume) State.Paused = false;
        settings.Paused = State.Paused;
        settings.ffRender = false;
        settings.ffRenderMask = false;
        settings.renderSecondsDelay = 0;
        settings.running = true;
        if (preview == null)
        {
            preview = new(settings.width, settings.height);
            var createdPreview = preview;
            preview.PauseRequested += () =>
            {
                if (State.IsPreviewing && !exporting) State.Paused = !State.Paused;
            };
            preview.SeekRequested += RequestPreviewSeek;
            preview.ToggleAudioRequested += () => State.AudioEnabled = !State.AudioEnabled;
            preview.RestartRequested += () => Queue(ReplayPreview);
            preview.Closed += (_, _) =>
            {
                if (!ReferenceEquals(preview, createdPreview)) return;
                preview = null;
                // A stopped preview may remain open while exporting. Closing
                // that window must not cancel the independent export session.
                if (!exporting && !disposed)
                {
                    var previewRun = running;
                    previewRun?.Cancel();
                    Queue(async () =>
                    {
                        if (ReferenceEquals(running, previewRun)) await Stop();
                    });
                }
            };
            preview.Show();
        }

        State.IsRendering = false;
        State.IsBusy = true;
        exporting = false;
        running = new();
        var token = running.Token;
        Interlocked.Exchange(ref seek, -1);
        var sequence = midi;
        preview.ConfigurePlayback(Path.GetFileName(State.MidiPath), sequence.DurationSeconds);
        State.IsPreviewing = true;
        SyncPreviewPlaybackState();
        // The Windows constructor reads NoteScreenTime before initializing the
        // renderer. Scripted's property starts at zero and caches later frames.
        double startTime = resume ? resumePosition : PreviewTiming.InitialSeconds(sequence.TempoMap,
            module == "scripted" ? scriptedScreenTime : modules[module].NoteScreenTime, settings.timeBasedNotes);
        State.Status = "Previewing";
        runningTask = Task.Run(async () =>
        {
            double timelineTime = startTime;
            bool finishedNaturally = false;
            try
            {
                using var worker = new RenderWorker(MakeRenderer);
                var completion = new RenderCompletion(sequence, settings.timeBasedNotes, settings.fps);
                IMidiOutput audio;
                try
                {
                    audio = new MacMidiSynth();
                }
                catch (Exception e)
                {
                    audio = new NullMidiOutput();
                    Dispatcher.UIThread.Post(() => State.Status = "Audio unavailable: " + e.Message);
                }

                using var playback = new PlaybackController(sequence, audio, realtime: false);
                player = playback;
                playback.Seek(Math.Max(0, timelineTime));
                playback.Muted = !State.AudioEnabled;
                var watch = Stopwatch.StartNew();
                double last = 0;
                int renderedVersion = -1;
                double renderedTime = double.NaN;
                double previousFrameMultiplier = 1;
                while (!token.IsCancellationRequested)
                {
                    double requested = Interlocked.Exchange(ref seek, -1);
                    if (requested >= 0)
                    {
                        playback.Pause();
                        playback.Seek(requested);
                        timelineTime = playback.PositionSeconds;
                        await worker.Reset(MakeRenderer);
                        completion = new RenderCompletion(sequence, settings.timeBasedNotes, settings.fps);
                        previousFrameMultiplier = 1;
                        renderedTime = double.NaN;
                    }

                    double now = watch.Elapsed.TotalSeconds;
                    // Show the requested frame exactly. GPU reconstruction time
                    // is not elapsed playback time after a seek or fresh start.
                    double dt = double.IsNaN(renderedTime) ? 0 : PreviewTiming.StepSeconds(now - last, State.RealtimePlayback, settings.fps);
                    last = now;
                    playback.Speed = State.TempoMultiplier;
                    playback.Muted = !State.AudioEnabled;
                    timelineTime = PreviewTiming.Advance(playback, timelineTime, dt, State.Paused);

                    double time = timelineTime;
                    int currentVersion = Volatile.Read(ref version);
                    bool finalFrame = false;
                    bool rendered = false;
                    if (!State.Paused || settings.forceReRender || time != renderedTime || currentVersion != renderedVersion)
                    {
                        settings.Paused = State.Paused;
                        settings.ignoreColorEvents = State.IgnoreColorEvents;
                        var data = await worker.Run(r =>
                        {
                            r.ScreenTime = State.NoteScreenTime;
                            r.FirstKey = (int)State.FirstNote;
                            r.LastKey = (int)State.LastNote + 1;
                            double screenTime = r.NoteScreenTime;
                            var pixels = r.Render(time);
                            if (module == "scripted") scriptedScreenTime = pack!.NoteScreenTime;
                            return (pixels, r.LastNoteCount, Complete: completion.Observe(time, screenTime,
                                r.LastNoteCount, playback.Speed, previousFrameMultiplier));
                        });
                        await Dispatcher.UIThread.InvokeAsync(() => preview?.Present(data.Item1, settings.width / settings.downscale, settings.height / settings.downscale, time, sequence.DurationSeconds, data.LastNoteCount));
                        renderedTime = time;
                        renderedVersion = currentVersion;
                        finalFrame = data.Complete;
                        rendered = true;
                    }
                    previousFrameMultiplier = State.RealtimePlayback ? dt * settings.fps : 1;

                    if (State.Vsync)
                    {
                        // CGL renders offscreen; Avalonia owns presentation.
                        // Match upstream's independent SwapBuffers/VSync gate
                        // using the window compositor's next animation frame.
                        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        using var cancelFrame = token.Register(() => presented.TrySetCanceled(token));
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (preview == null) presented.TrySetCanceled();
                            else preview.RequestAnimationFrame(_ => presented.TrySetResult());
                        });
                        await presented.Task;
                    }
                    else if (State.Paused && !rendered)
                        await Task.Delay(16, token);
                    if (finalFrame)
                    {
                        finishedNaturally = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    State.Status = e.Message;
                    if (!closing) await window.ShowError(e.Message);
                });
            }
            finally
            {
                // PlaybackController clamps audio to zero; retain the visual
                // position as well when restarting a preview during preroll.
                resumePosition = timelineTime;
                player = null;
                settings.running = false;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    State.IsPreviewing = false;
                    State.IsBusy = false;
                    if (finishedNaturally && !token.IsCancellationRequested)
                        State.Status = "Preview finished";
                    // Keep the final frame and expose Replay after a natural
                    // finish or Stop; a restart still runs the original start path.
                    SyncPreviewPlaybackState();
                });
            }
        });
    }

    void StartExport()
    {
        if (midi == null)
            throw new InvalidOperationException("Load a MIDI file first.");
        if (string.IsNullOrWhiteSpace(State.VideoPath))
            throw new InvalidOperationException("Select a video output path.");
        if (State.IncludeAlpha && string.IsNullOrWhiteSpace(State.AlphaPath))
            throw new InvalidOperationException("Select an alpha mask output path.");
        State.TempoMultiplier = 1;
        State.Paused = false;
        ApplySettings();
        settings.Paused = false;
        settings.tempoMultiplier = 1;
        settings.realtimePlayback = false;
        settings.ffRender = true;
        settings.ffRenderMask = State.IncludeAlpha;
        settings.renderSecondsDelay = (double)State.DelaySeconds;
        settings.running = true;
        var exportStart = PreviewTiming.ExportStart(midi.TempoMap,
            module == "scripted" ? scriptedScreenTime : modules[module].NoteScreenTime,
            settings.timeBasedNotes, settings.fps, settings.renderSecondsDelay);
        var completion = new RenderCompletion(midi, settings.timeBasedNotes, settings.fps);
        var options = new VideoExportOptions
        {
            OutputPath = State.VideoPath,
            MaskOutputPath = State.IncludeAlpha ? State.AlphaPath : null,
            Width = (int)State.Width,
            Height = (int)State.Height,
            FramesPerSecond = settings.fps,
            DurationSeconds = Math.Max(1d / settings.fps, midi.DurationSeconds - exportStart.StartSeconds + 5),
            StartSeconds = exportStart.StartSeconds,
            PlaybackSpeed = 1,
            StopAfterFrame = _ => completion.Complete,
            BitrateKbps = State.UseBitrate ? (int)State.Bitrate : null,
            Crf = (int)State.Crf,
            Preset = State.CrfPreset,
            AudioPath = State.IncludeAudio ? State.AudioPath : null,
            AudioOffsetSeconds = exportStart.AudioOffsetSeconds,
            FfmpegPath = FindFfmpeg(),
            AdditionalArguments = State.UseCustomFfmpeg ? FfmpegExporter.ParseArguments(State.FfmpegOptions) : []
        };
        exporting = true;
        State.IsPreviewing = false;
        SyncPreviewPlaybackState();
        State.IsRendering = true;
        State.IsBusy = true;
        State.Progress = 0;
        running = new();
        var token = running.Token;
        var progress = new Progress<ExportProgress>(p =>
        {
            if (token.IsCancellationRequested || !State.IsRendering) return;
            State.Progress = p.Fraction;
            State.Status = p.IsEstimate
                ? $"Rendering {p.CompletedFrames:N0} frames ({p.Fraction:P1} estimated)"
                : $"Rendering {p.CompletedFrames:N0} / {p.TotalFrames:N0} frames ({p.Fraction:P1})";
        });
        runningTask = Task.Run(async () =>
        {
            try
            {
                using var worker = new RenderWorker(MakeRenderer);
                var result = await FfmpegExporter.ExportAsync(options, async (frame, ct) => await worker.Run(r =>
                {
                    double screenTime = r.NoteScreenTime;
                    var pixels = r.Render(frame.TimelineSeconds);
                    if (module == "scripted") scriptedScreenTime = pack!.NoteScreenTime;
                    completion.Observe(frame.TimelineSeconds, screenTime, r.LastNoteCount);
                    return (ReadOnlyMemory<byte>)pixels;
                }), progress, token);
                await Dispatcher.UIThread.InvokeAsync(() => { State.Progress = 1; State.Status = $"Saved {result.OutputPath}"; });
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.UIThread.InvokeAsync(() => State.Status = "Render cancelled");
            }
            catch (Exception e)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    State.Status = e.Message;
                    if (!closing) await window.ShowError(e.Message);
                });
            }
            finally
            {
                settings.running = false;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    State.IsRendering = false;
                    State.IsBusy = false;
                });
            }
        });
    }

    static string FindFfmpeg()
    {
        string[] locations =
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            "/opt/homebrew/bin/ffmpeg",
            "/usr/local/bin/ffmpeg"
        };
        foreach (var p in locations)
            if (File.Exists(p))
                return p;
        return "ffmpeg";
    }

    async Task Stop()
    {
        bool wasPreviewing = State.IsPreviewing;
        State.IsPreviewing = false;
        if (!exporting && player != null)
            resumePosition = player.PositionSeconds;
        running?.Cancel();
        if (runningTask != null)
            await runningTask;
        runningTask = null;
        running?.Dispose();
        running = null;
        Interlocked.Exchange(ref seek, -1);
        State.IsRendering = false;
        State.IsBusy = false;
        exporting = false;
        if (wasPreviewing && State.Status == "Previewing") State.Status = "Stopped";
        SyncPreviewPlaybackState();
    }

    async Task RestartPreview()
    {
        bool resume = PreviewIsRunning;
        await Stop();
        if (resume && midi != null)
            StartPreview(true);
    }

    void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainWindowState.Status) or nameof(MainWindowState.Progress)))
            Interlocked.Increment(ref version);
        if (e.PropertyName is nameof(MainWindowState.UseBackground) or nameof(MainWindowState.BackgroundPath))
            Queue(RestartPreview);
        if (e.PropertyName == nameof(MainWindowState.AudioEnabled))
            State.AudioToggleLabel = State.AudioEnabled ? "Disable Audio" : "Enable Audio";
        if (e.PropertyName is nameof(MainWindowState.Paused) or nameof(MainWindowState.AudioEnabled)
            or nameof(MainWindowState.IsPreviewing))
            SyncPreviewPlaybackState();
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(State));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
        }
    }

    void LoadSettings()
    {
        if (!File.Exists(settingsPath))
            return;
        try
        {
            var saved = JsonSerializer.Deserialize<MainWindowState>(File.ReadAllText(settingsPath));
            if (saved != null)
                foreach (var p in typeof(MainWindowState).GetProperties().Where(p => p.CanWrite && p.Name is not ("IsBusy" or "IsRendering" or "IsPreviewing" or "MidiLoaded" or "MidiPath" or "Status" or "Progress")))
                    p.SetValue(State, p.GetValue(saved));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
        }
    }

    public void Dispose()
    {
        disposed = true;
        running?.Cancel();
        player?.Pause();
        SaveSettings();
    }
}
