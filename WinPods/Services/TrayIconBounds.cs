using System.Runtime.InteropServices;
using System.Windows;

namespace WinPods.Services;

public static class TrayIconBounds
{
    public static System.Windows.Point GetCursorPosition()
    {
        GetCursorPos(out var point);
        return new(point.X, point.Y);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
