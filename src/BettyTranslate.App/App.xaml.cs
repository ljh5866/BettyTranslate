using System.IO;
using System.Windows;
using BettyTranslate.App.Views;
using BettyTranslate.Core.Auth;
using BettyTranslate.Core.Settings;

namespace BettyTranslate.App;

/// <summary>
/// 应用入口：读取配置 → 恢复本地会话 → 进入主界面或登录页
/// </summary>
public partial class App : Application
{
    /// <summary>全局认证服务实例（窗口/ViewModel 通过此访问）</summary>
    public static IAuthService AuthService { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
        var settings = AppSettings.Load(configPath);
        AuthService = new SupabaseAuthService(settings.Supabase.Url, settings.Supabase.AnonKey);

        // 恢复本地会话：有效则直接进入主界面，否则进入登录页
        if (await AuthService.EnsureSessionAsync())
            new MainWindow().Show();
        else
            new LoginWindow().Show();
    }
}
