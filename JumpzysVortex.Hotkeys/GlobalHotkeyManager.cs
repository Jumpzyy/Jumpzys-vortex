using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JumpzysVortex.Hotkeys;

public class GlobalHotkeyManager : IDisposable
{
    // Win32
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CTRL  = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const int  WM_HOTKEY = 0x0312;

    // Virtual key codes
    private const uint VK_B = 0x42;
    private const uint VK_R = 0x52;
    private const uint VK_O = 0x4F;
    private const uint VK_D = 0x44;

    private const int ID_BOOST    = 1;
    private const int ID_RESTORE  = 2;
    private const int ID_OVERLAY  = 3;
    private const int ID_DASH     = 4;

    public event Action? BoostTriggered;
    public event Action? RestoreTriggered;
    public event Action? OverlayToggled;
    public event Action? DashboardToggled;

    private HwndSource?  _source;
    private IntPtr       _handle;
    private bool         _disposed;

    public void Register(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        RegisterHotKey(_handle, ID_BOOST,   MOD_CTRL | MOD_SHIFT, VK_B);
        RegisterHotKey(_handle, ID_RESTORE, MOD_CTRL | MOD_SHIFT, VK_R);
        RegisterHotKey(_handle, ID_OVERLAY, MOD_CTRL | MOD_SHIFT, VK_O);
        RegisterHotKey(_handle, ID_DASH,    MOD_CTRL | MOD_SHIFT, VK_D);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case ID_BOOST:   BoostTriggered?.Invoke();   handled = true; break;
            case ID_RESTORE: RestoreTriggered?.Invoke();  handled = true; break;
            case ID_OVERLAY: OverlayToggled?.Invoke();    handled = true; break;
            case ID_DASH:    DashboardToggled?.Invoke();  handled = true; break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_handle, ID_BOOST);
        UnregisterHotKey(_handle, ID_RESTORE);
        UnregisterHotKey(_handle, ID_OVERLAY);
        UnregisterHotKey(_handle, ID_DASH);
        _source?.RemoveHook(WndProc);
    }
}
