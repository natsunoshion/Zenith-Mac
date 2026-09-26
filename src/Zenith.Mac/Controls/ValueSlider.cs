using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Zenith.Mac;

/// <summary>Zenith's slider plus editable numeric value, including its logarithmic mapping.</summary>
public sealed class ValueSlider : UserControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Value), 1, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> TrueMinimumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(TrueMinimum), 0);
    public static readonly StyledProperty<double> TrueMaximumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(TrueMaximum), 100);
    public static readonly StyledProperty<bool> LogarithmicProperty = AvaloniaProperty.Register<ValueSlider, bool>(nameof(Logarithmic));
    public static readonly StyledProperty<int> DecimalPlacesProperty = AvaloniaProperty.Register<ValueSlider, int>(nameof(DecimalPlaces), 2);
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Step), 1);
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double TrueMinimum { get => GetValue(TrueMinimumProperty); set => SetValue(TrueMinimumProperty, value); }
    public double TrueMaximum { get => GetValue(TrueMaximumProperty); set => SetValue(TrueMaximumProperty, value); }
    public bool Logarithmic { get => GetValue(LogarithmicProperty); set => SetValue(LogarithmicProperty, value); }
    public int DecimalPlaces { get => GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public event Action<double>? ValueChanged;
    private readonly Slider slider = new()
    {
        Height = 14, MinHeight = 14, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 3)
    };
    private readonly NumericUpDown number = new()
    {
        MinWidth = 80, Height = 26, HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(5, 0, 0, 0)
    };
    private bool updating;
    public ValueSlider()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(slider);
        Grid.SetColumn(number, 1);
        grid.Children.Add(number);
        Content = grid;
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !updating) Value = Math.Round(Logarithmic ? Math.Pow(2, slider.Value) : slider.Value, DecimalPlaces); };
        number.ValueChanged += (_, _) => { if (!updating && number.Value.HasValue) Value = (double)number.Value.Value; };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ValueProperty || e.Property == MinimumProperty || e.Property == MaximumProperty || e.Property == TrueMinimumProperty || e.Property == TrueMaximumProperty || e.Property == LogarithmicProperty || e.Property == DecimalPlacesProperty || e.Property == StepProperty)
            {
                Refresh();
                if (e.Property == ValueProperty) ValueChanged?.Invoke(Value);
            }
        };
        Refresh();
    }
    private void Refresh()
    {
        updating = true;
        try
        {
            var min = Logarithmic ? Math.Log2(Math.Max(Minimum, 0.000001)) : Minimum;
            var max = Logarithmic ? Math.Log2(Math.Max(Maximum, 0.000001)) : Maximum;
            slider.Maximum = Math.Max(min, max);
            slider.Minimum = min;
            number.Maximum = (decimal)Math.Max(TrueMinimum, TrueMaximum);
            number.Minimum = (decimal)TrueMinimum;
            number.Increment = (decimal)Step;
            number.FormatString = DecimalPlaces <= 0 ? "0" : "0." + new string('0', DecimalPlaces);
            slider.Value = Math.Clamp(Logarithmic ? Math.Log2(Math.Max(Value, 0.000001)) : Value, min, Math.Max(min, max));
            number.Value = (decimal)Math.Clamp(Value, TrueMinimum, Math.Max(TrueMinimum, TrueMaximum));
        }
        finally { updating = false; }
    }
}
