using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Cvx.Rando.App;

/// <summary>A number with − and + buttons (the old NumericUpDown). The template is in Theme.axaml.</summary>
public sealed class Stepper : TemplatedControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<Stepper, int>(nameof(Value), defaultBindingMode: BindingMode.TwoWay, coerce: (o, v) => Coerce((Stepper)o, v));
    public static readonly StyledProperty<int> MinimumProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Minimum));
    public static readonly StyledProperty<int> MaximumProperty = AvaloniaProperty.Register<Stepper, int>(nameof(Maximum), 100);

    private Button? _dec, _inc;

    public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    private static int Coerce(Stepper s, int v) => Math.Clamp(v, s.Minimum, Math.Max(s.Minimum, s.Maximum));

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_dec != null) _dec.Click -= Dec;
        if (_inc != null) _inc.Click -= Inc;
        _dec = e.NameScope.Find<Button>("PART_Dec");
        _inc = e.NameScope.Find<Button>("PART_Inc");
        if (_dec != null) _dec.Click += Dec;
        if (_inc != null) _inc.Click += Inc;
        UpdateButtons();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MinimumProperty || change.Property == MaximumProperty)
            CoerceValue(ValueProperty);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
            UpdateButtons();
    }

    private void Dec(object? sender, RoutedEventArgs e) => Value--;
    private void Inc(object? sender, RoutedEventArgs e) => Value++;

    private void UpdateButtons()
    {
        if (_dec != null) _dec.IsEnabled = Value > Minimum;
        if (_inc != null) _inc.IsEnabled = Value < Maximum;
    }
}

/// <summary>The item mix: one bar split by the four weights, in their colours.</summary>
public sealed class MixBar : Control
{
    public static readonly StyledProperty<int[]> WeightsProperty = AvaloniaProperty.Register<MixBar, int[]>(nameof(Weights), []);

    private static readonly IBrush Track = Brush.Parse("#0f1012");
    private static readonly IBrush[] Colours = [Brush.Parse("#d9473b"), Brush.Parse("#4fae74"), Brush.Parse("#7c8cf0"), Brush.Parse("#d6a54a")];

    static MixBar() => AffectsRender<MixBar>(WeightsProperty);

    public int[] Weights { get => GetValue(WeightsProperty); set => SetValue(WeightsProperty, value); }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var shape = new RoundedRect(bounds, bounds.Height / 2);
        context.DrawRectangle(Track, null, shape);
        var weights = Weights;
        var total = weights.Sum();
        if (total <= 0)
            return;
        const double gap = 2;
        var shown = weights.Count(w => w > 0);
        var width = bounds.Width - gap * (shown - 1);
        using (context.PushClip(shape))
        {
            var x = 0.0;
            for (var i = 0; i < weights.Length && i < Colours.Length; i++)
            {
                if (weights[i] <= 0)
                    continue;
                var w = width * weights[i] / total;
                context.DrawRectangle(Colours[i], null, new Rect(x, 0, w, bounds.Height));
                x += w + gap;
            }
        }
    }
}
