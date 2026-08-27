using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BettyTranslate.App.Services;

/// <summary>
/// 系统托盘图标（Win32 Shell_NotifyIcon，零第三方依赖）。
/// 左键双击恢复窗口；右键弹出菜单（打开主界面 / 退出程序）。
/// 需在窗口句柄创建后（OnSourceInitialized）使用。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    // Shell_NotifyIcon 消息
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;

    // NOTIFYICONDATA 标志
    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;
    private const uint NIF_INFO = 0x0010;

    private const uint NIIF_INFO = 0x0001;

    // 托盘回调消息：使用 WM_APP + 1，避免与应用/热键消息冲突
    private const int WM_APP = 0x8000;
    private const int WM_APP_MSG = WM_APP + 1;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_NULL = 0x0000;

    // 右键菜单
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const int ID_OPEN = 1;
    private const int ID_EXIT = 2;

    // 图标加载
    private const uint IMAGE_ICON = 0x00000001;
    private const uint LR_LOADFROMFILE = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType,
        int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(IntPtr hmenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hwnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>用户要求打开主界面（双击托盘图标 / 右键菜单「打开主界面」）</summary>
    public event Action? OpenRequested;

    /// <summary>用户要求退出程序（右键菜单「退出程序」）</summary>
    public event Action? ExitRequested;

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly IntPtr _hIcon;
    private readonly uint _id = 1;
    private bool _disposed;

    public TrayIconService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        _hIcon = LoadAppIcon();

        var nid = BuildData();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_APP_MSG;
        nid.hIcon = _hIcon;
        nid.szTip = "Betty Translate";
        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    /// <summary>显示气泡提示（如：已隐藏到电脑扩展栏）</summary>
    public void ShowBalloon(string title, string text)
    {
        var nid = BuildData();
        nid.uFlags = NIF_INFO;
        nid.szInfoTitle = title;
        nid.szInfo = text;
        nid.dwInfoFlags = NIIF_INFO;
        nid.uTimeoutOrVersion = 2000;
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var nid = BuildData();
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        _source?.RemoveHook(WndProc);
        if (_hIcon != IntPtr.Zero)
            DestroyIcon(_hIcon);
    }

    private NOTIFYICONDATA BuildData()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
        };
        return nid;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_MSG && wParam.ToInt32() == _id)
        {
            var evt = lParam.ToInt32();
            switch (evt)
            {
                case WM_LBUTTONDBLCLK:
                    OpenRequested?.Invoke();
                    handled = true;
                    break;
                case WM_CONTEXTMENU:
                case WM_RBUTTONUP:
                    ShowContextMenu();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, ID_OPEN, "打开主界面");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, ID_EXIT, "退出程序");

        GetCursorPos(out var p);
        // TrackPopupMenu 前需把窗口置于前台，否则菜单无法正常消失
        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
            p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == ID_OPEN)
            OpenRequested?.Invoke();
        else if (cmd == ID_EXIT)
            ExitRequested?.Invoke();
    }

    /// <summary>从打包资源读取应用图标并转成 HICON（写临时文件后 LoadImage，避免额外依赖）</summary>
    private static IntPtr LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/AppIcon.ico");
            using var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream == null)
                return IntPtr.Zero;

            var tmp = Path.Combine(Path.GetTempPath(), "BettyTranslate_TrayIcon.ico");
            try
            {
                using (var fs = File.Create(tmp))
                    stream.CopyTo(fs);
                return LoadImage(IntPtr.Zero, tmp, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* 忽略清理失败 */ }
            }
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
