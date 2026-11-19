using System.Globalization;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WinPods.Models;

namespace WinPods.Controls;

internal static class StatusBrushes
{
    public static readonly Brush Active = Create(0x69, 0xD3, 0x6F);     // WinPods green
    public static readonly Brush Connected = Create(0xE9, 0xB9, 0x49);  // amber: linked, but not playing here
    public static readonly Brush InCase = Create(0x48, 0xB9, 0xF2);     // WinPods accent blue
    public static readonly Brush Neutral = Create(0xD6, 0xD6, 0xDB);
    public static readonly Brush Muted = Create(0x8E, 0x8E, 0x95);

    private static SolidColorBrush Create(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Green when Windows audio is on the AirPods, amber when only connected, grey otherwise.</summary>
public sealed class ConnectionStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AirPodsConnectionStatus.Active => StatusBrushes.Active,
        AirPodsConnectionStatus.Ready or AirPodsConnectionStatus.AudioUnavailable => StatusBrushes.Connected,
        _ => StatusBrushes.Muted
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BudLocationBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        BudLocation.InEar => StatusBrushes.Active,
        BudLocation.InCase => StatusBrushes.InCase,
        BudLocation.OutOfEar => StatusBrushes.Neutral,
        _ => StatusBrushes.Muted
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
