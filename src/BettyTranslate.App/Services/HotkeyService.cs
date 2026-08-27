using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BettyTranslate.App.Services;

/// <summary>
/// 全局热键服务（Win32 RegisterHotKey，零依赖）。
/// 绑定到窗口消息循环，热键按下时触发 HotKeyPressed。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    /// <summary>不自动重复触发（按住不放只触发一次）</summary>
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint MOD_CONTROL = 0x0002;
    public const uint VK_F10 = 0x79;
    public const uint VK_F11 = 0x7A;
    public const uint VK_F12 = 0x7B;

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly List<int> _ids = [];

    /// <summary>热键按下事件，参数为热键 id</summary>
    public event Action<int>? HotKeyPressed;

    public HotkeyService(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(WndProc);
    }

    /// <summary>注册热键；返回 false 表示被其他程序占用</summary>
    public bool Register(int id, uint modifiers, uint vk)
    {
        if (!RegisterHotKey(_handle, id, modifiers, vk))
            return false;
        _ids.Add(id);
        return true;
    }

    /// <summary>注销单个热键 id（不影响其他已注册热键）</summary>
    public void Unregister(int id)
    {
        if (_ids.Remove(id))
            UnregisterHotKey(_handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotKeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        foreach (var id in _ids)
            UnregisterHotKey(_handle, id);
        _ids.Clear();
    }
}
