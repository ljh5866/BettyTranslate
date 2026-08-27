using System.Windows;
using System.Windows.Controls;

namespace BettyTranslate.App.Views;

/// <summary>
/// 退出确认弹窗：让用户选择「隐藏到电脑扩展栏」或「退出程序」。
/// 美观的无边框自定义窗口（替代原生 MessageBox 的「是/否」）。
/// </summary>
public partial class CloseConfirmWindow : Window
{
    /// <summary>是否选择「退出程序」；false 表示「隐藏到电脑扩展栏」（默认）。</summary>
    public bool ExitChosen { get; private set; }

    public CloseConfirmWindow()
    {
        InitializeComponent();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        ExitChosen = true;
        DialogResult = true;
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        ExitChosen = false;
        DialogResult = false;
    }
}
