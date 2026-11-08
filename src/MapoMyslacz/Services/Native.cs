using System.Runtime.InteropServices;

namespace MapoMyslacz.Services;

internal static partial class Native
{
    public const int SHCNE_ASSOCCHANGED = 0x08000000;

    [LibraryImport("shell32.dll")]
    public static partial void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    private static uint _doubleClickTime;

    /// <summary>Czas dwukliku z ustawień Windows (ms).</summary>
    public static uint DoubleClickTime => _doubleClickTime > 0 ? _doubleClickTime : _doubleClickTime = Math.Max(200, GetDoubleClickTime());
}
