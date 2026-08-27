using System;
using System.Windows;
using System.Windows.Controls;
using BettyTranslate.Core.Capture;

namespace BettyTranslate.App.Views;

/// <summary>
/// 轻量进度提示窗：翻译过程中显示在屏幕上（置顶、无边框），告知用户正在处理，
/// 避免主窗口隐藏时应用看起来「消失」。
/// </summary>
public partial class ProgressWindow : Window
{
    public ProgressWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;

        // 放在虚拟屏幕下方居中，尽量避开用户框选的正文区域
        Loaded += (_, _) => CenterBottom();
    }

    /// <summary>更新进度提示文字（在主窗口线程调用）</summary>
    public void UpdateMessage(string message) => MessageText.Text = message;

    private void CenterBottom()
    {
        var v = ScreenCaptureService.GetVirtualScreenBounds();
        // 距屏幕底部约 10% 处居中
        Left = v.X + (v.Width - ActualWidth) / 2;
        Top = v.Y + v.Height * 0.85 - ActualHeight;
    }
}
