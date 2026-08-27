using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BettyTranslate.App.Views;

/// <summary>
/// 划词翻译结果悬浮窗：在鼠标附近展示原文与译文，置顶无边框。
/// 呼出时自动关闭已存在的窗口，保证屏幕上只存在一个翻译框；用右上角 × 或 ESC 关闭。
/// </summary>
public partial class SelectionTranslateWindow : Window
{
    private static SelectionTranslateWindow? _instance;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private SelectionTranslateWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PositionNearMouse();
    }

    /// <summary>展示划词翻译结果。若已有窗口，则先关闭旧窗口，保证同一时刻只存在一个。</summary>
    public static void ShowTranslation(string source, string translation)
    {
        _instance?.Close();

        var win = new SelectionTranslateWindow
        {
            SourceText = { Text = source },
            TranslationText = { Text = translation },
        };
        win.Closed += (_, _) => { if (_instance == win) _instance = null; };
        _instance = win;
        win.Show();
    }

    /// <summary>把窗口定位到鼠标附近，超出屏幕边界时回移到鼠标另一侧</summary>
    private void PositionNearMouse()
    {
        GetCursorPos(out var p);
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = p.X / dpi.DpiScaleX + 12;
        var top = p.Y / dpi.DpiScaleY + 16;

        var work = SystemParameters.WorkArea;
        if (left + Width > work.Right)
            left = p.X / dpi.DpiScaleX - Width - 12;
        if (top + Height > work.Bottom)
            top = p.Y / dpi.DpiScaleY - Height - 16;

        Left = Math.Max(work.Left, left);
        Top = Math.Max(work.Top, top);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>按住标题栏拖动窗口</summary>
    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
