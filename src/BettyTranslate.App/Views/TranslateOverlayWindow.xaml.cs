using System.Windows;
using BettyTranslate.Core.Translation;

namespace BettyTranslate.App.Views;

/// <summary>
/// 翻译结果悬浮窗：置顶显示逐行原文+译文，可整体拖拽，右上角关闭
/// </summary>
public partial class TranslateOverlayWindow : Window
{
    public TranslateOverlayWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    /// <summary>显示"正在翻译"状态</summary>
    public void ShowLoading()
    {
        LineList.ItemsSource = null;
        StatusText.Text = "正在扫描屏幕并翻译，请稍候…";
    }

    /// <summary>显示翻译结果</summary>
    public void ShowResult(IReadOnlyList<TranslatedLine> lines)
    {
        if (lines.Count == 0)
        {
            LineList.ItemsSource = null;
            StatusText.Text = "未在屏幕上识别到文字";
            return;
        }

        StatusText.Text = $"屏幕翻译 · 共 {lines.Count} 行";
        LineList.ItemsSource = lines;
    }

    /// <summary>显示错误信息</summary>
    public void ShowError(string message)
    {
        LineList.ItemsSource = null;
        StatusText.Text = "翻译失败：" + message;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
