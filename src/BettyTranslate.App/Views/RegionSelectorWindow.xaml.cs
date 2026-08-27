using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BettyTranslate.Core.Capture;

namespace BettyTranslate.App.Views;

/// <summary>
/// 区域框选窗口：全屏遮罩 + 鼠标拖拽选择区域。
/// 以 ShowDialog 方式使用，关闭后通过 <see cref="SelectedRegion"/> 获取选区（屏幕物理像素坐标）。
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private System.Windows.Point _start;
    private bool _dragging;

    /// <summary>用户框选的屏幕区域（物理像素坐标）；取消/无效选区时为 null</summary>
    public Rectangle? SelectedRegion { get; private set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var v = ScreenCaptureService.GetVirtualScreenBounds();
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = v.X / dpi.DpiScaleX;
        Top = v.Y / dpi.DpiScaleY;
        Width = v.Width / dpi.DpiScaleX;
        Height = v.Height / dpi.DpiScaleY;
        DimLayer.Width = Width;
        DimLayer.Height = Height;
        Canvas.SetLeft(HintBox, 16);
        Canvas.SetTop(HintBox, 16);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;

        var cur = e.GetPosition(RootCanvas);
        Canvas.SetLeft(SelectionRect, Math.Min(_start.X, cur.X));
        Canvas.SetTop(SelectionRect, Math.Min(_start.Y, cur.Y));
        SelectionRect.Width = Math.Abs(cur.X - _start.X);
        SelectionRect.Height = Math.Abs(cur.Y - _start.Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
            return;
        _dragging = false;
        ReleaseMouseCapture();

        var cur = e.GetPosition(RootCanvas);
        var x = Math.Min(_start.X, cur.X);
        var y = Math.Min(_start.Y, cur.Y);
        var w = Math.Abs(cur.X - _start.X);
        var h = Math.Abs(cur.Y - _start.Y);
        if (w < 8 || h < 8)
            return; // 选区过小视为取消

        var dpi = VisualTreeHelper.GetDpi(this);
        var v = ScreenCaptureService.GetVirtualScreenBounds();
        SelectedRegion = new Rectangle(
            v.X + (int)Math.Round(x * dpi.DpiScaleX),
            v.Y + (int)Math.Round(y * dpi.DpiScaleY),
            (int)Math.Round(w * dpi.DpiScaleX),
            (int)Math.Round(h * dpi.DpiScaleY));
        DialogResult = true;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
