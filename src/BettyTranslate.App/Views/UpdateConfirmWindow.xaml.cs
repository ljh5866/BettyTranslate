using System.Windows;
using System.Windows.Controls;

namespace BettyTranslate.App.Views;

/// <summary>
/// 更新确认弹窗：提示用户已下载新版本，选择「立即更新并重启」或「暂不更新」。
/// 无边框自定义窗口（替代原生 MessageBox 的「是/否」）。
/// </summary>
public partial class UpdateConfirmWindow : Window
{
    /// <summary>是否选择「立即更新并重启」；false 表示「暂不更新」（默认）。</summary>
    public bool Confirmed { get; private set; }

    public UpdateConfirmWindow(string latestVersion, string assetName)
    {
        InitializeComponent();
        VersionText.Text = latestVersion;
        AssetText.Text = $"安装包：{assetName}";
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
