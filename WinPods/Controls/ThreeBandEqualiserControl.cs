using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FlowDirection = System.Windows.FlowDirection;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using WinPods.Models;

namespace WinPods.Controls;

public sealed class ThreeBandEqualiserControl : FrameworkElement
{
    public static readonly DependencyProperty LowGainProperty = RegisterGain(nameof(LowGain));
    public static readonly DependencyProperty MidGainProperty = RegisterGain(nameof(MidGain));
    public static readonly DependencyProperty HighGainProperty = RegisterGain(nameof(HighGain));
    public static readonly DependencyProperty SpectrumProperty = DependencyProperty.Register(
        nameof(Spectrum), typeof(IReadOnlyList<double>), typeof(ThreeBandEqualiserControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    private int _draggedBand = -1;

    public ThreeBandEqualiserControl()
    {
        Cursor = Cursors.SizeNS;
        Focusable = true;
    }

    public double LowGain
    {
        get => (double)GetValue(LowGainProperty);
        set => SetValue(LowGainProperty, value);
    }

    public double MidGain
    {
        get => (double)GetValue(MidGainProperty);
        set => SetValue(MidGainProperty, value);
    }

    public double HighGain
    {
        get => (double)GetValue(HighGainProperty);
        set => SetValue(HighGainProperty, value);
    }

    public IReadOnlyList<double> Spectrum
    {
        get => (IReadOnlyList<double>)GetValue(SpectrumProperty);
        set => SetValue(SpectrumProperty, value);
    }

    public event EventHandler? EditCompleted;

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var graphTop = 20d;
        var graphBottom = Math.Max(graphTop + 20, height - 44);
        var center = (graphTop + graphBottom) / 2;
        var usableHeight = graphBottom - graphTop;
        var lowPoint = PointFor(0, LowGain, width, center, usableHeight);
        var midPoint = PointFor(1, MidGain, width, center, usableHeight);
        var highPoint = PointFor(2, HighGain, width, center, usableHeight);

        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), 1);
        guidePen.Freeze();
        context.DrawLine(guidePen, new(8, center), new(width - 8, center));
        foreach (var point in new[] { lowPoint, midPoint, highPoint })
            context.DrawLine(guidePen, new(point.X, graphTop), new(point.X, graphBottom));

        DrawSpectrum(context, width, center, usableHeight);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(8, lowPoint.Y), false, false);
            sink.LineTo(lowPoint, true, false);
            AddSmoothSegment(sink, lowPoint, midPoint);
            AddSmoothSegment(sink, midPoint, highPoint);
            sink.LineTo(new Point(width - 8, highPoint.Y), true, false);
        }
        geometry.Freeze();

        var curvePen = new Pen(new SolidColorBrush(Color.FromRgb(244, 232, 72)), 4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        context.DrawGeometry(null, curvePen, geometry);

        DrawKnob(context, lowPoint, Color.FromRgb(246, 212, 63));
        DrawKnob(context, midPoint, Color.FromRgb(74, 222, 203));
        DrawKnob(context, highPoint, Color.FromRgb(91, 181, 244));
        DrawLabel(context, $"LOW   {FormatGain(LowGain)}", width * 0.2, height - 20);
        DrawLabel(context, $"MID   {FormatGain(MidGain)}", width * 0.5, height - 20);
        DrawLabel(context, $"HIGH   {FormatGain(HighGain)}", width * 0.8, height - 20);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _draggedBand = FindNearestBand(e.GetPosition(this).X);
        CaptureMouse();
        UpdateDraggedGain(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggedBand >= 0 && e.LeftButton == MouseButtonState.Pressed)
            UpdateDraggedGain(e.GetPosition(this).Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_draggedBand < 0) return;
        UpdateDraggedGain(e.GetPosition(this).Y);
        _draggedBand = -1;
        ReleaseMouseCapture();
        EditCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Up or Key.Down)) return;
        var band = _draggedBand >= 0 ? _draggedBand : 1;
        var change = e.Key == Key.Up ? 1 : -1;
        if (band == 0) LowGain += change;
        else if (band == 1) MidGain += change;
        else HighGain += change;
        EditCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void DrawSpectrum(DrawingContext context, double width, double center, double usableHeight)
    {
        var values = Spectrum;
        const int fallbackBands = 52;
        var count = values.Count > 0 ? values.Count : fallbackBands;
        var gap = 2d;
        var barWidth = Math.Max(1.5, (width - 16 - gap * (count - 1)) / count);
        for (var index = 0; index < count; index++)
        {
            var value = values.Count > index ? Math.Clamp(values[index], 0, 1) : 0;
            var barHeight = 3 + value * usableHeight * 0.84;
            var x = 8 + index * (barWidth + gap);
            var colour = index < count * 0.34
                ? Color.FromArgb(180, 246, 212, 63)
                : index < count * 0.67
                    ? Color.FromArgb(180, 74, 222, 203)
                    : Color.FromArgb(180, 91, 181, 244);
            var brush = new SolidColorBrush(colour);
            context.DrawRoundedRectangle(brush, null,
                new Rect(x, center - barHeight / 2, barWidth, barHeight), barWidth / 2, barWidth / 2);
        }
    }

    private void UpdateDraggedGain(double y)
    {
        var top = 20d;
        var bottom = Math.Max(top + 20, ActualHeight - 44);
        var center = (top + bottom) / 2;
        var gain = Math.Round(Math.Clamp(
            (center - y) / ((bottom - top) / 2) * ThreeBandEqualiserProfile.MaximumGain,
            ThreeBandEqualiserProfile.MinimumGain,
            ThreeBandEqualiserProfile.MaximumGain));
        if (_draggedBand == 0) LowGain = gain;
        else if (_draggedBand == 1) MidGain = gain;
        else if (_draggedBand == 2) HighGain = gain;
    }

    private int FindNearestBand(double x)
    {
        var positions = new[] { ActualWidth * 0.2, ActualWidth * 0.5, ActualWidth * 0.8 };
        return Enumerable.Range(0, 3).MinBy(index => Math.Abs(positions[index] - x));
    }

    private static DependencyProperty RegisterGain(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(ThreeBandEqualiserControl),
        new FrameworkPropertyMetadata(0d,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null, (_, value) => Math.Round(Math.Clamp(
                (double)value,
                ThreeBandEqualiserProfile.MinimumGain,
                ThreeBandEqualiserProfile.MaximumGain))));

    private static Point PointFor(int index, double gain, double width, double center, double usableHeight) =>
        new(
            width * (0.2 + index * 0.3),
            center - Math.Clamp(
                gain,
                ThreeBandEqualiserProfile.MinimumGain,
                ThreeBandEqualiserProfile.MaximumGain) /
            (ThreeBandEqualiserProfile.MaximumGain - ThreeBandEqualiserProfile.MinimumGain) * usableHeight);

    private static void AddSmoothSegment(StreamGeometryContext sink, Point from, Point to)
    {
        var distance = (to.X - from.X) * 0.42;
        sink.BezierTo(new Point(from.X + distance, from.Y), new Point(to.X - distance, to.Y), to, true, false);
    }

    private static void DrawKnob(DrawingContext context, Point point, Color colour)
    {
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, colour.R, colour.G, colour.B)), null, point, 15, 15);
        context.DrawEllipse(new SolidColorBrush(colour), new Pen(Brushes.White, 2), point, 9, 9);
    }

    private void DrawLabel(DrawingContext context, string text, double x, double y)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 10,
            new SolidColorBrush(Color.FromRgb(178, 178, 184)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, new Point(x - formatted.Width / 2, y - formatted.Height / 2));
    }

    private static string FormatGain(double value) => $"{value:+0;-0;0} dB";
}
