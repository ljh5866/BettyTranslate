using System.Windows;
using BettyTranslate.App.Services;
using BettyTranslate.App.Views;

namespace BettyTranslate.App;

/// <summary>
/// 主窗口：注册 F1 全局热键，触发"确认 → 截图 → OCR → 翻译 → 悬浮窗"流程
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyTranslateId = 1;

    private readonly HotkeyService _hotkeys;
    private TranslateOverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        UserText.Text = $"当前用户：{App.AuthService.CurrentUser?.Email ?? "未知"}";

        _hotkeys = new HotkeyService(this);
        if (!_hotkeys.Register(HotkeyTranslateId, HotkeyService.MOD_NOREPEAT, HotkeyService.VK_F1))
        {
            MessageBox.Show("F1 快捷键注册失败，可能已被其他程序占用",
                "Betty Translate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _hotkeys.HotKeyPressed += OnHotKeyPressed;
    }

    private async void OnHotKeyPressed(int id)
    {
        if (id != HotkeyTranslateId)
            return;

        // 需求：翻译前询问用户是否进行屏幕翻译
        var choice = MessageBox.Show("是否进行屏幕翻译？", "Betty Translate",
            MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);
        if (choice != MessageBoxResult.Yes)
            return;

        await TranslateScreenAsync();
    }

    private async Task TranslateScreenAsync()
    {
        _overlay ??= new TranslateOverlayWindow();
        _overlay.ShowLoading();
        if (!_overlay.IsVisible)
            _overlay.Show();

        try
        {
            var lines = await App.TranslateService.TranslateScreenAsync();
            _overlay.ShowResult(lines);
        }
        catch (Exception ex)
        {
            _overlay.ShowError(ex.Message);
        }
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        await App.AuthService.SignOutAsync();
        new LoginWindow().Show();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
