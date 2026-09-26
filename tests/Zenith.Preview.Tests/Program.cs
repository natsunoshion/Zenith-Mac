using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Zenith.Core.Midi;
using Zenith.Mac;

// These checks deliberately use the real desktop dispatcher, RenderWorker,
// native CGL renderer and PlaybackController. User settings are isolated.
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Preview integration checks require macOS and its native CGL backend.");
        AppBuilder.Configure<PreviewTestApp>().UsePlatformDetect().WithInterFont()
            .StartWithClassicDesktopLifetime(args);
        return Environment.ExitCode;
    }
}

internal sealed class PreviewTestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Zenith.Mac/"))
        {
            Source = new Uri("avares://Zenith.Mac/Styles/ZenithTheme.axaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow();
        desktop.MainWindow = window;
        window.Opened += (_, _) => DispatcherTimer.RunOnce(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "Zenith-preview-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            AppController? controller = null;
            try
            {
                window.Hide();
                controller = new AppController(window, Path.Combine(directory, "settings.json"));
                await new PreviewChecks(window, controller).Run(directory);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
            }
            finally
            {
                if (controller != null)
                {
                    await PreviewChecks.InvokeTask(controller, "Stop");
                    controller.Dispose();
                    PreviewChecks.Field<PreviewWindow?>(controller, "preview")?.Close();
                }
                window.Close();
                desktop.Shutdown(Environment.ExitCode);
                Directory.Delete(directory, true);
            }
        }, TimeSpan.FromMilliseconds(100));
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class PreviewChecks(MainWindow main, AppController controller)
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    int count;
    MainWindowState State => main.State;
    PreviewWindow Preview => Field<PreviewWindow?>(controller, "preview") ?? throw new Exception("Preview window missing");
    PlaybackController? Player => Field<PlaybackController?>(controller, "player");
    static bool Near(double value, double expected) => Math.Abs(value - expected) < .00001;
    static T Control<T>(Avalonia.Controls.Control root, string name) where T : Avalonia.Controls.Control =>
        root.GetVisualDescendants().OfType<T>().Single(x => x.Name == name);
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Seek(PreviewWindow preview, double seconds) => Field<Action<double>>(preview, "SeekRequested")(seconds);
    void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
        count++;
    }
    static async Task Until(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(message);
            await Task.Delay(15);
        }
    }

    public async Task Run(string directory)
    {
        // One note lasting one second (960 ticks at PPQN 480, default tempo).
        var midi = Path.Combine(directory, "transport.mid");
        File.WriteAllBytes(midi, [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
            77, 84, 114, 107, 0, 0, 0, 13, 0, 0x90, 60, 100, 0x87, 0x40, 0x80, 60, 0, 0, 0xff, 0x2f, 0]);
        State.AudioEnabled = false;
        State.Width = 64; State.Height = 36; State.Ssaa = 1; State.Fps = 10;
        State.Vsync = false; State.RealtimePlayback = true;
        State.MidiPath = midi;
        State.Progress = .89;
        var loading = InvokeTask(controller, "Handle", "load");
        Check(State.IsBusy && State.Status == "Loading MIDI…" && State.Progress == 0,
            "Load must clear old progress and publish a loading state before reading MIDI");
        await loading;
        await InvokeTask(controller, "SelectModule", "flat");
        Check(State.MidiLoaded && !State.IsBusy, "MIDI load must complete before starting preview");

        State.Progress = .37;
        State.Paused = true;
        await InvokeTask(controller, "Handle", "start-preview");
        var preview = Preview;
        preview.Hide();
        Check(State.IsPreviewing && !State.Paused, "Fresh Start must reset pause and mark preview active");
        State.Paused = true;
        await Until(() => Player != null && Field<double>(preview, "position") < 0, "Initial preroll frame was not presented");
        Check(Field<bool>(preview, "active") && Field<bool>(preview, "paused"), "Main pause state did not reach transport");
        Check(Field<double>(preview, "duration") == 1, "Timeline duration must equal MIDI duration, excluding preroll/tail");
        await Task.Delay(30);
        Check(main.FindControl<Border>("StatusBar")!.IsVisible && !main.FindControl<ProgressBar>("TaskProgress")!.IsVisible,
            "Preview footer must be visible without stale task progress");
        Check(main.FindControl<TextBlock>("StatusText")!.Text == "Preview · Paused", "Footer did not follow paused preview");
        Check(main.FindControl<Button>("ShowPreviewButton")!.IsVisible, "Show Preview should be available during preview");
        var task = Field<Task?>(controller, "runningTask");
        Click(main.FindControl<Button>("ShowPreviewButton")!);
        await Until(() => preview.IsVisible, "Show Preview button did not restore the existing window");
        Check(ReferenceEquals(Preview, preview) && ReferenceEquals(Field<Task?>(controller, "runningTask"), task),
            "Show Preview must not create a new window or restart playback");
        preview.Hide();

        Click(Control<PlaybackButton>(preview, "PlaybackPlay"));
        Check(!State.Paused && !Field<bool>(preview, "paused"), "Transport resume did not synchronize main pause state");
        Click(Control<PlaybackButton>(preview, "PlaybackPlay"));
        Check(State.Paused && Field<bool>(preview, "paused"), "Transport pause did not synchronize main pause state");
        Click(Control<PlaybackButton>(preview, "PlaybackAudio"));
        Check(State.AudioEnabled && State.AudioToggleLabel == "Disable Audio"
            && AutomationProperties.GetName(Control<PlaybackButton>(preview, "PlaybackAudio")) == "Mute audio",
            "Transport audio button did not update both windows");
        State.AudioEnabled = false;
        Check(AutomationProperties.GetName(Control<PlaybackButton>(preview, "PlaybackAudio")) == "Enable audio",
            "Main audio state did not update preview controls");

        Seek(preview, .7);
        await Until(() => Near(Field<double>(preview, "position"), .7), "Paused forward seek was not rendered");
        Check(State.Paused && Near(Player!.PositionSeconds, .7), "Paused seek must restore audio position and remain paused");
        Seek(preview, .2);
        await Until(() => Near(Field<double>(preview, "position"), .2), "Backward seek did not rebuild and render");
        Check(State.IsPreviewing && Near(Player!.PositionSeconds, .2), "Backward seek broke the render session");
        Seek(preview, double.NaN);
        await Task.Delay(40);
        Check(Near(Field<double>(preview, "position"), .2), "Invalid seek changed the timeline");
        Seek(preview, 200);
        await Until(() => Near(Field<double>(preview, "position"), 1), "Out-of-range seek was not clamped to MIDI duration");
        Check(State.Progress == .37, "Preview must not repurpose load/export progress");

        await InvokeTask(controller, "Stop");
        Check(!State.IsPreviewing && !State.IsBusy && ReferenceEquals(preview, Preview), "Stop must retain the last frame for Replay");
        Check(!Field<bool>(preview, "active") && !Control<PlaybackSeekSlider>(preview, "PlaybackTimeline").IsEnabled,
            "Stopped transport must disable seek");
        Seek(preview, .4);
        Check(Field<double>(controller, "seek") == -1, "Stopped preview must ignore stale seek requests");
        Click(Control<PlaybackButton>(preview, "PlaybackPlay"));
        await Until(() => State.IsPreviewing, "Replay after Stop did not restart");
        Check(!State.Paused, "Replay must use the original fresh-start pause reset");
        State.Paused = true;
        await Until(() => Player != null && Field<double>(preview, "position") < 0, "Replay did not restart original preroll");
        Seek(preview, 1);
        await Until(() => Near(Field<double>(preview, "position"), 1), "Seek before tail test did not finish");
        State.RealtimePlayback = false;
        State.Paused = false;
        await Until(() => !State.IsPreviewing, "Natural tail completion did not finish preview");
        Check(State.Status == "Preview finished" && ReferenceEquals(preview, Preview), "Natural completion must retain Replay and last frame");
        Check(Field<double>(preview, "position") > 1 && !Field<bool>(preview, "active"), "Natural completion lost visible tail or active state");
        State.RealtimePlayback = true;
        Click(Control<PlaybackButton>(preview, "PlaybackPlay"));
        await Until(() => State.IsPreviewing, "Replay after natural completion did not restart");
        State.Paused = true;
        await Until(() => Player != null, "Replayed worker did not initialize");
        await InvokeTask(controller, "Stop");

        // A stopped transport may still be visible while a separate operation
        // runs. Exercise its real handlers without starting an unrelated export.
        using var unrelatedRun = new CancellationTokenSource();
        SetField(controller, "running", unrelatedRun);
        SetField(controller, "exporting", true);
        State.IsRendering = true; State.IsBusy = true;
        Click(Control<PlaybackButton>(preview, "PlaybackPlay"));
        await Task.Delay(40);
        Check(!State.IsPreviewing && !unrelatedRun.IsCancellationRequested, "Replay must not cancel an independent export");
        preview.Close();
        Check(Field<PreviewWindow?>(controller, "preview") == null && !unrelatedRun.IsCancellationRequested,
            "Closing a stopped preview must not cancel export");
        SetField(controller, "running", null);
        SetField(controller, "exporting", false);
        State.IsRendering = false; State.IsBusy = false;
        Console.WriteLine($"PASS {count} native preview integration checks: pause/audio synchronization, preroll, seek/reset, footer, Stop/finish Replay and export isolation.");
    }

    internal static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Private)!.GetValue(source)!;
    static void SetField(object source, string name, object? value) => source.GetType().GetField(name, Private)!.SetValue(source, value);
    internal static Task InvokeTask(object source, string name, params object[] arguments) =>
        (Task)source.GetType().GetMethod(name, Private)!.Invoke(source, arguments)!;
}
