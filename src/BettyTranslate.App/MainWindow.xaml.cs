using System.Windows;
using BettyTranslate.App.Views;

namespace BettyTranslate.App;

/// <summary>
/// 主窗口（M2 里程碑将加入屏幕翻译功能）
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UserText.Text = $"当前用户：{App.AuthService.CurrentUser?.Email ?? "未知"}";
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        await App.AuthService.SignOutAsync();
        new LoginWindow().Show();
        Close();
    }
}
