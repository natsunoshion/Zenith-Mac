using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace Zenith.Mac;
public sealed class PreviewWindow : Window
{
    const double ControlsHeight = 100;
    readonly Image image = new() { Name = "PreviewImage", Stretch = Stretch.Uniform };
    readonly PlaybackSeekSlider timeline = new() { Name = "PlaybackTimeline" };
    readonly TextBlock currentTime = TimeLabel("0:00"), totalTime = TimeLabel("0:00");
    readonly TextBlock playbackStatus = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#ADB8B1")), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly PlaybackButton play = new("Play", PlayerIcons.Play, true) { Name = "PlaybackPlay" };
    readonly PlaybackButton restart = new("Restart from beginning", PlayerIcons.Restart) { Name = "PlaybackRestart" };
    readonly PlaybackButton audio = new("Mute audio", PlayerIcons.Audio) { Name = "PlaybackAudio" };
    readonly PlaybackButton fullscreen = new("Enter fullscreen (Enter)", PlayerIcons.Fullscreen) { Name = "PlaybackFullscreen" };
    WriteableBitmap? bitmap;
    bool closed, active, paused, spaceHeld, enterHeld;
    double position, duration;
    public event Action<double>? SeekRequested;
    public event Action? PauseRequested;
    public event Action? ToggleAudioRequested;
    public event Action? RestartRequested;

    public PreviewWindow(int renderWidth = 1920, int renderHeight = 1080)
    {
        Title = "Render";
        Width = (Screens.Primary?.Bounds.Width ?? 1500) / (Screens.Primary?.Scaling ?? 1) / 1.5;
        Height = Width * renderHeight / Math.Max(1, renderWidth) + ControlsHeight;
        MinWidth = 480; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.Full;
        TooltipFocusGuard.Attach(this);
        Background = Brushes.Black;
        Content = BuildContent();
        timeline.SeekCommitted += seconds => { if (active && !closed) SeekRequested?.Invoke(ClampTime(seconds)); };
        timeline.PreviewChanged += UpdateTimeLabels;
        play.Click += (_, _) => PlayOrPause();
        restart.Click += (_, _) => { if (!closed) RestartRequested?.Invoke(); };
        audio.Click += (_, _) => { if (!closed) ToggleAudioRequested?.Invoke(); };
        fullscreen.Click += (_, _) => ToggleFullscreen();
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space) spaceHeld = false; if (e.Key == Key.Enter) enterHeld = false; }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => { spaceHeld = enterHeld = false; timeline.CancelDrag(); };
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateFullscreenButton(); };
        Closed += (_, _) => { closed = true; timeline.CancelDrag(); image.Source = null; bitmap?.Dispose(); bitmap = null; };
        SetPlaybackState(false, false, true);
    }
    Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new($"*,{ControlsHeight}") };
        root.Children.Add(new Border { Background = Brushes.Black, Child = image, ClipToBounds = true });
        var panel = new Grid { RowDefinitions = new("28,8,40"), Margin = new Thickness(16, 12) };
        var seekRow = new Grid { ColumnDefinitions = new("68,*,68"), ColumnSpacing = 10 };
        currentTime.Name = "PlaybackCurrentTime"; totalTime.Name = "PlaybackDuration";
        currentTime.HorizontalAlignment = HorizontalAlignment.Left; totalTime.HorizontalAlignment = HorizontalAlignment.Right;
        seekRow.Children.Add(currentTime); Grid.SetColumn(timeline, 1); seekRow.Children.Add(timeline); Grid.SetColumn(totalTime, 2); seekRow.Children.Add(totalTime); panel.Children.Add(seekRow);
        var buttons = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 14 };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(play); left.Children.Add(restart); left.Children.Add(audio); buttons.Children.Add(left);
        playbackStatus.Name = "PlaybackStatus"; Grid.SetColumn(playbackStatus, 1); buttons.Children.Add(playbackStatus);
        Grid.SetColumn(fullscreen, 2); buttons.Children.Add(fullscreen); Grid.SetRow(buttons, 2); panel.Children.Add(buttons);
        var footer = new Border { Name = "PlaybackControls", Background = new SolidColorBrush(Color.Parse("#19211D")), BorderBrush = new SolidColorBrush(Color.Parse("#303A34")), BorderThickness = new Thickness(0, 1, 0, 0), Child = panel };
        Grid.SetRow(footer, 1); root.Children.Add(footer); return root;
    }
    public void ConfigurePlayback(string title, double duration)
    {
        VerifyUiThread(); if (closed) return;
        Title = string.IsNullOrWhiteSpace(title) ? "Render" : "Render — " + title;
        SetDuration(duration); UpdateTimeLabels();
    }
    public void SetPlaybackState(bool active, bool paused, bool audioEnabled)
    {
        VerifyUiThread(); if (closed) return;
        this.active = active; this.paused = paused;
        if (!active) timeline.CancelDrag();
        timeline.IsEnabled = active && duration > 0;
        play.SetIcon(active && !paused ? PlayerIcons.Pause : PlayerIcons.Play);
        play.SetLabel(!active ? "Replay from beginning (Space)" : paused ? "Resume playback (Space)" : "Pause playback (Space)");
        audio.SetIcon(audioEnabled ? PlayerIcons.Audio : PlayerIcons.Muted); audio.SetLabel(audioEnabled ? "Mute audio" : "Enable audio");
        UpdateTimeLabels();
    }
    void PlayOrPause() { if (closed) return; if (active) PauseRequested?.Invoke(); else RestartRequested?.Invoke(); }
    void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        // Tunnel once, before a focused button or slider can also interpret
        // Space/Enter. Held keys must not toggle the state repeatedly.
        if (e.Handled || closed) return;
        switch (e.Key)
        {
            case Key.Space: if (!spaceHeld) PlayOrPause(); spaceHeld = true; e.Handled = true; break;
            case Key.Enter: if (!enterHeld) ToggleFullscreen(); enterHeld = true; e.Handled = true; break;
            case Key.Escape:
                if (timeline.IsDragging) { timeline.CancelDrag(); e.Handled = true; }
                else if (WindowState == WindowState.FullScreen) { ToggleFullscreen(); e.Handled = true; }
                break;
            case Key.Left:
            case Key.Right:
                if (active && !timeline.IsDragging)
                {
                    var skip = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 20 : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 60 : 5;
                    SeekRequested?.Invoke(ClampTime(position + (e.Key == Key.Left ? -skip : skip)));
                }
                e.Handled = true; break;
        }
    }
    void ToggleFullscreen() { WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; UpdateFullscreenButton(); }
    void UpdateFullscreenButton()
    {
        bool full = WindowState == WindowState.FullScreen;
        fullscreen.SetIcon(full ? PlayerIcons.ExitFullscreen : PlayerIcons.Fullscreen); fullscreen.SetLabel(full ? "Exit fullscreen (Enter)" : "Enter fullscreen (Enter)");
    }
    void SetDuration(double value)
    {
        duration = double.IsFinite(value) ? Math.Max(0, value) : 0;
        timeline.SetDuration(duration); timeline.IsEnabled = active && duration > 0;
    }
    double ClampTime(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, duration) : 0;
    void UpdateTimeLabels()
    {
        currentTime.Text = FormatTime(timeline.IsDragging ? timeline.Value : position); totalTime.Text = FormatTime(duration);
        playbackStatus.Text = timeline.IsDragging ? "Seek to " + FormatTime(timeline.Value)
            : !active ? position >= duration && duration > 0 ? "Finished · Replay available" : "Stopped · Replay available"
            : (position < 0 ? "Lead-in · " : position > duration ? "Tail · " : "") + (paused ? "Paused" : "Playing");
    }
    static TextBlock TimeLabel(string text) => new() { Text = text, FontFamily = new FontFamily("SF Mono, Menlo, Consolas"), FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds)) seconds = 0;
        var time = TimeSpan.FromSeconds(Math.Min(Math.Abs(seconds), TimeSpan.MaxValue.TotalSeconds - 1));
        string formatted = time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(long)time.TotalMinutes}:{time.Seconds:00}";
        return seconds < 0 ? "−" + formatted : formatted;
    }
    static void VerifyUiThread() { if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Preview controls must be updated on the UI thread."); }
    public void Present(byte[] data, int width, int height, double seconds, double duration, long notes)
    {
        VerifyUiThread(); if (closed) return;
        if (data.Length != checked(width * height * 4)) throw new ArgumentException("Preview frame dimensions do not match the BGRA buffer.", nameof(data));
        if (bitmap == null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap?.Dispose(); bitmap = new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque); image.Source = bitmap;
        }
        using (var locked = bitmap.Lock())
            for (int y = 0; y < height; y++) Marshal.Copy(data, y * width * 4, locked.Address + y * locked.RowBytes, width * 4);
        image.InvalidateVisual(); position = double.IsFinite(seconds) ? seconds : 0;
        SetDuration(duration); timeline.SetPosition(ClampTime(position)); UpdateTimeLabels();
    }
}
