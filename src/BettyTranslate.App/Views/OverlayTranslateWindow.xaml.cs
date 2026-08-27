using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BettyTranslate.Core.Capture;
using BettyTranslate.Core.Translation;

namespace BettyTranslate.App.Views;

/// <summary>
/// 翻译结果覆盖窗口：全屏透明，在每条原文所在位置显示中文（不透明底色盖住英文）。
/// 按 ESC 或点击空白处关闭。
/// </summary>
public partial class OverlayTranslateWindow : Window
{
    public OverlayTranslateWindow(IReadOnlyList<TranslatedLine> lines)
    {
        InitializeComponent();
        Loaded += (_, _) => Populate(lines);
        MouseLeftButtonDown += (_, _) => Close();
    }

    private void Populate(IReadOnlyList<TranslatedLine> lines)
    {
        var v = ScreenCaptureService.GetVirtualScreenBounds();
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = v.X / dpi.DpiScaleX;
        Top = v.Y / dpi.DpiScaleY;
        Width = v.Width / dpi.DpiScaleX;
        Height = v.Height / dpi.DpiScaleY;
        Canvas.SetLeft(HintBox, 16);
        Canvas.SetTop(HintBox, 16);

        foreach (var line in lines)
        {
            // 屏幕物理坐标 → 窗口内坐标（窗口原点对应虚拟屏幕左上角）
            var left = (line.ScreenBounds.X - v.X) / dpi.DpiScaleX;
            var top = (line.ScreenBounds.Y - v.Y) / dpi.DpiScaleY;
            var width = line.ScreenBounds.Width / dpi.DpiScaleX;
            var height = line.ScreenBounds.Height / dpi.DpiScaleY;

            // 字号与原文行高对齐，使译文与英文大小格式一致
            var fontSize = Math.Max(10, Math.Min(28, height * 0.8));

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 0),
                Height = height,
                MaxWidth = Math.Max(width * 1.5, 120),
                Child = new TextBlock
                {
                    Text = line.Translation,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x22, 0x29)),
                    FontSize = fontSize,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                }
            };
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, top);
            Canvas.SetZIndex(border, 10);
            RootCanvas.Children.Add(border);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
