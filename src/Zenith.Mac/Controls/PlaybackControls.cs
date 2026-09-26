using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Zenith.Mac;

// Separate style keys isolate playback from the original settings theme.
public sealed class PlaybackButton : Button
{
    readonly PathIcon icon;
    readonly bool primary;
    protected override Type StyleKeyOverride => typeof(PlaybackButton);
    public PlaybackButton(string label, string geometry, bool primary = false)
    {
        this.primary = primary;
        Width = 40; Height = 40; MinWidth = 40; MinHeight = 40;
        Padding = new Thickness(0); CornerRadius = new CornerRadius(12);
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Center;
        Foreground = Brushes.White;
        icon = new PathIcon { Data = Geometry.Parse(geometry), Width = 20, Height = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        Template = new FuncControlTemplate<PlaybackButton>((button, _) =>
        {
            var border = new Border { CornerRadius = new CornerRadius(12), Child = icon };
            border.Bind(Border.BackgroundProperty, new Binding(nameof(Background)) { Source = button });
            return border;
        });
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty || e.Property == IsPressedProperty || e.Property == IsEnabledProperty || e.Property == IsFocusedProperty) UpdateBackground(); };
        SetLabel(label); UpdateBackground();
    }
    public void SetIcon(string geometry) => icon.Data = Geometry.Parse(geometry);
    public void SetLabel(string label) { ToolTip.SetTip(this, label); AutomationProperties.SetName(this, label); }
    void UpdateBackground() => Background = new SolidColorBrush(Color.Parse(!IsEnabled ? "#222A25" : IsPressed ? "#66BB6A" : IsPointerOver || IsFocused ? primary ? "#4BA855" : "#35493C" : primary ? "#2E7D32" : "#26352C"));
}

/// <summary>One seek per pointer gesture; frame updates never emit seeks or
/// move the thumb during dragging. Inherits Slider's range automation peer.</summary>
public sealed class PlaybackSeekSlider : Slider
{
    bool updating, dragging;
    double latestPosition;
    IPointer? capturedPointer;
    protected override Type StyleKeyOverride => typeof(PlaybackSeekSlider);
    public new bool IsDragging => dragging;
    public event Action<double>? SeekCommitted;
    public event Action? PreviewChanged;
    public PlaybackSeekSlider()
    {
        Height = 28; MinHeight = 28; Minimum = 0; Maximum = 1;
        SmallChange = 5; LargeChange = 20; Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Template = new FuncControlTemplate<PlaybackSeekSlider>((_, _) => new Panel());
        AutomationProperties.SetName(this, "Playback position");
        ToolTip.SetTip(this, "Drag to seek · Left/Right 5 seconds · Ctrl 20 seconds · Shift 60 seconds");
        ValueChanged += (_, _) =>
        {
            InvalidateVisual();
            if (!updating && !dragging && IsEnabled) SeekCommitted?.Invoke(Clamped(Value));
            PreviewChanged?.Invoke();
        };
        PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty || e.Property == IsFocusedProperty || e.Property == IsPointerOverProperty) InvalidateVisual(); };
    }
    public void SetDuration(double duration)
    {
        updating = true;
        try { Maximum = Math.Max(0, double.IsFinite(duration) ? duration : 0); }
        finally { updating = false; }
    }
    public void SetPosition(double seconds)
    {
        latestPosition = Clamped(seconds);
        if (dragging) return;
        updating = true;
        try { Value = latestPosition; }
        finally { updating = false; }
    }
    double Clamped(double value) => double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Minimum;
    void MoveTo(Point p) => Value = Minimum + Math.Clamp((p.X - 7) / Math.Max(1, Bounds.Width - 14), 0, 1) * (Maximum - Minimum);
    public void CancelDrag()
    {
        if (!dragging) return;
        dragging = false;
        var pointer = capturedPointer; capturedPointer = null;
        pointer?.Capture(null); SetPosition(latestPosition); InvalidateVisual(); PreviewChanged?.Invoke();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); dragging = true; capturedPointer = e.Pointer; e.Pointer.Capture(this);
        MoveTo(e.GetPosition(this)); InvalidateVisual(); PreviewChanged?.Invoke(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!dragging || e.Pointer != capturedPointer) return;
        MoveTo(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!dragging || e.Pointer != capturedPointer || e.InitialPressMouseButton != MouseButton.Left) return;
        MoveTo(e.GetPosition(this)); dragging = false; capturedPointer = null; e.Pointer.Capture(null);
        if (IsEnabled) SeekCommitted?.Invoke(Clamped(Value));
        InvalidateVisual(); PreviewChanged?.Invoke(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Cancelled gestures must not seek.
        if (e.Pointer == capturedPointer) { dragging = false; capturedPointer = null; SetPosition(latestPosition); InvalidateVisual(); PreviewChanged?.Invoke(); }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsEnabled || e.Handled) return;
        switch (e.Key)
        {
            case Key.Home: Value = Minimum; e.Handled = true; break;
            case Key.End: Value = Maximum; e.Handled = true; break;
            case Key.PageUp: Value = Clamped(Value + LargeChange); e.Handled = true; break;
            case Key.PageDown: Value = Clamped(Value - LargeChange); e.Handled = true; break;
            case Key.Left: Value = Clamped(Value - SmallChange); e.Handled = true; break;
            case Key.Right: Value = Clamped(Value + SmallChange); e.Handled = true; break;
        }
    }
    public override void Render(DrawingContext context)
    {
        double length = Math.Max(0, Bounds.Width - 14), y = Bounds.Height / 2;
        double fraction = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
        double x = 7 + length * Math.Clamp(fraction, 0, 1);
        var track = new SolidColorBrush(Color.Parse("#3B4940"));
        var accent = new SolidColorBrush(Color.Parse(IsEnabled ? "#69C778" : "#58705E"));
        context.DrawRectangle(track, null, new Rect(7, y - 2, length, 4), 2, 2);
        if (x > 7) context.DrawRectangle(accent, null, new Rect(7, y - 2, x - 7, 4), 2, 2);
        if (IsEnabled && (IsFocused || IsPointerOver || dragging)) context.DrawEllipse(new SolidColorBrush(Color.Parse("#2869C778")), null, new Point(x, y), 13, 13);
        context.DrawEllipse(accent, null, new Point(x, y), 6, 6);
    }
}

static class PlayerIcons
{
    public const string Play = "M7,4 L21,12 L7,20 Z";
    public const string Pause = "M6,4 H10 V20 H6 Z M14,4 H18 V20 H14 Z";
    public const string Restart = "M5,4 H7 V20 H5 Z M20,4 V20 L8,12 Z";
    public const string Audio = "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,7 L17,5 C22,9 22,15 17,19 L15,17 C19,14 19,10 15,7 Z";
    public const string Muted = "M3,9 H7 L12,4 V20 L7,15 H3 Z M16,8 L22,14 L20,16 L14,10 Z M20,8 L22,10 L16,16 L14,14 Z";
    public const string Fullscreen = "M3,3 H10 V5 H5 V10 H3 Z M14,3 H21 V10 H19 V5 H14 Z M3,14 H5 V19 H10 V21 H3 Z M19,14 H21 V21 H14 V19 H19 Z";
    public const string ExitFullscreen = "M8,3 H10 V10 H3 V8 H8 Z M14,3 H16 V8 H21 V10 H14 Z M3,14 H10 V21 H8 V16 H3 Z M14,14 H21 V16 H16 V21 H14 Z";
}
