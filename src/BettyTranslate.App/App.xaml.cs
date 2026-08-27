using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BettyTranslate.App.Views;
using BettyTranslate.Core.Auth;
using BettyTranslate.Core.Capture;
using BettyTranslate.Core.Ocr;
using BettyTranslate.Core.Settings;
using BettyTranslate.Core.Translation;
using BettyTranslate.Core.Update;

namespace BettyTranslate.App;

/// <summary>
/// 应用入口：读取配置 → 恢复本地会话 → 进入主界面或登录页
/// </summary>
public partial class App : Application
{
    /// <summary>全局认证服务实例（窗口/ViewModel 通过此访问）</summary>
    public static IAuthService AuthService { get; private set; } = null!;

    /// <summary>屏幕翻译编排服务（框选区域 → 截图 → OCR → 翻译 → 悬浮文字框覆盖）</summary>
    public static ScreenTranslateService TranslateService { get; private set; } = null!;

    /// <summary>图片翻译编排服务（框选区域 → 截图 → OCR → 翻译 → 合成贴合选区的新图片）</summary>
    public static ImageTranslateService ImageTranslateService { get; private set; } = null!;

    /// <summary>检查更新服务（GitHub Releases，版本检查 + 下载安装包）</summary>
    public static UpdateService UpdateService { get; private set; } = null!;

    /// <summary>配置文件路径（应用目录 Config/appsettings.json）</summary>
    public static string ConfigPath { get; private set; } = string.Empty;

    /// <summary>图片翻译服务端代理的 Edge Function 完整地址（由 Supabase Url + 函数名拼接）</summary>
    public static string VisionFunctionUrl { get; private set; } = string.Empty;

    /// <summary>每个账号可免费体验的截图翻译次数（超出后需用户自备 DeepSeek Key）</summary>
    public const int FreeImageTranslateLimit = 15;

    private static ScreenCaptureService _capture = null!;
    private static WindowsOcrEngine _ocr = null!;
    private static ITranslateProvider _translator = null!;

    /// <summary>错误日志路径（应用目录 logs/error.log）</summary>
    private static string ErrorLogPath => Path.Combine(
        AppContext.BaseDirectory, "logs", "error.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：记录日志并尽量阻止闪退
        DispatcherUnhandledException += (_, args) =>
        {
            LogError("Dispatcher", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogError("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError("Task", args.Exception);
            args.SetObserved();
        };

        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
        ConfigPath = configPath;
        var settings = AppSettings.Load(configPath);
        ApplyTheme(settings.Theme);
        VisionFunctionUrl =
            $"{settings.Supabase.Url.TrimEnd('/')}/functions/v1/{settings.Supabase.VisionFunctionName}";
        AuthService = new SupabaseAuthService(settings.Supabase.Url, settings.Supabase.AnonKey);
        var capture = new ScreenCaptureService();
        var ocr = new WindowsOcrEngine();
        var translator = new FailingOverTranslateProvider(
            new BaiduTranslateProvider(settings.BaiduTranslate.AppId, settings.BaiduTranslate.SecretKey),
            new GoogleTranslateProvider());
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
        TranslateService = new ScreenTranslateService(capture, ocr, translator);
        ImageTranslateService = CreateImageTranslateService();
        UpdateService = new UpdateService(settings.Update);

        // 恢复本地会话：有效则直接进入主界面，否则进入登录页
        // 开机自启（带 --autostart）时主界面静默隐藏到系统托盘，不弹出窗口
        var startInTray = e.Args.Any(a =>
            string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));
        if (await AuthService.EnsureSessionAsync())
        {
            var mainWindow = new MainWindow(startInTray);
            mainWindow.Show();
            // Show() 会在窗口句柄创建后（OnSourceInitialized）再设为可见，
            // 会覆盖其中对开机自启的 Hide()。因此在这里再次隐藏，确保自启时静默进托盘。
            if (startInTray)
                mainWindow.Hide();
        }
        else
        {
            new LoginWindow().Show();
        }
    }

    /// <summary>构建图片翻译服务。视觉通道不再在此固定装配，由调用方按免费额度/用户 Key 逐次传入</summary>
    private static ImageTranslateService CreateImageTranslateService()
        => new(_capture, _ocr, _translator);

    /// <summary>设置页保存后调用，重建图片翻译服务（当前服务不再依赖 Key，保留接口以兼容旧调用）</summary>
    public static void RefreshImageTranslateService()
    {
        ImageTranslateService = CreateImageTranslateService();
    }

    /// <summary>划词翻译文本入口：把选中的英文翻译成中文，复用文本翻译引擎（百度 + Google 自动回退）</summary>
    public static Task<string> TranslateTextAsync(string text)
        => _translator.TranslateAsync(text, "zh");

    /// <summary>应用主题（light / dark / warm）：通过修改色板 Brush 的颜色实时生效</summary>
    public static void ApplyTheme(string theme)
    {
        (Color Accent, Color AccentSoft, Color AccentBorder, Color AccentPressed,
         Color TextPrimary, Color TextSecondary, Color Border, Color Surface, Color Bg,
         Color NavHover, Color NavPressed, Color BottomHover, Color BottomPressed) colors =
            theme switch
            {
                "dark" => (
                    Color.FromRgb(0x4C, 0xC2, 0xFF), // Accent 亮蓝
                    Color.FromRgb(0x1E, 0x3A, 0x5F), // AccentSoft 深蓝底
                    Color.FromRgb(0x2D, 0x5A, 0x8C), // AccentBorder
                    Color.FromRgb(0x2A, 0x4A, 0x70), // AccentPressed
                    Color.FromRgb(0xF3, 0xF3, 0xF3), // TextPrimary
                    Color.FromRgb(0x9D, 0x9D, 0x9D), // TextSecondary
                    Color.FromRgb(0x3F, 0x3F, 0x46), // Border
                    Color.FromRgb(0x25, 0x25, 0x26), // Surface
                    Color.FromRgb(0x1B, 0x1B, 0x1B), // Bg
                    Color.FromRgb(0x33, 0x33, 0x37), // NavHover
                    Color.FromRgb(0x3A, 0x3A, 0x3E), // NavPressed
                    Color.FromRgb(0x33, 0x33, 0x37), // BottomHover
                    Color.FromRgb(0x3A, 0x3A, 0x3E)),// BottomPressed
                "warm" => (
                    Color.FromRgb(0xC0, 0x5B, 0x2E), // Accent 暖橙
                    Color.FromRgb(0xFA, 0xE9, 0xD9), // AccentSoft
                    Color.FromRgb(0xF0, 0xD4, 0xB8), // AccentBorder
                    Color.FromRgb(0xF0, 0xD4, 0xB8), // AccentPressed
                    Color.FromRgb(0x3D, 0x2E, 0x1E), // TextPrimary
                    Color.FromRgb(0x8A, 0x7A, 0x66), // TextSecondary
                    Color.FromRgb(0xE8, 0xDC, 0xCB), // Border
                    Color.FromRgb(0xFF, 0xFF, 0xFF), // Surface
                    Color.FromRgb(0xFA, 0xF6, 0xF0), // Bg
                    Color.FromRgb(0xF0, 0xE8, 0xDC), // NavHover
                    Color.FromRgb(0xE8, 0xDC, 0xCB), // NavPressed
                    Color.FromRgb(0xF0, 0xE8, 0xDC), // BottomHover
                    Color.FromRgb(0xE8, 0xDC, 0xCB)),// BottomPressed
                _ => (
                    Color.FromRgb(0x0F, 0x6C, 0xBD), // 默认浅色（Fluent）
                    Color.FromRgb(0xE8, 0xF0, 0xFA),
                    Color.FromRgb(0xD0, 0xE2, 0xF5),
                    Color.FromRgb(0xD8, 0xE6, 0xF5),
                    Color.FromRgb(0x1B, 0x1B, 0x1B),
                    Color.FromRgb(0x61, 0x61, 0x61),
                    Color.FromRgb(0xE0, 0xE0, 0xE0),
                    Color.FromRgb(0xFF, 0xFF, 0xFF),
                    Color.FromRgb(0xF3, 0xF3, 0xF3),
                    Color.FromRgb(0xF0, 0xF0, 0xF0),
                    Color.FromRgb(0xE5, 0xE5, 0xE5),
                    Color.FromRgb(0xE9, 0xE9, 0xE9),
                    Color.FromRgb(0xE0, 0xE0, 0xE0)),
            };

        Set("AccentBrush", colors.Accent);
        Set("AccentHoverBrush", Color.FromRgb(
            (byte)(colors.Accent.R * 0.85), (byte)(colors.Accent.G * 0.85), (byte)(colors.Accent.B * 0.85)));
        Set("AccentSoftBrush", colors.AccentSoft);
        Set("AccentBorderBrush", colors.AccentBorder);
        Set("AccentPressedBrush", colors.AccentPressed);
        Set("TextPrimaryBrush", colors.TextPrimary);
        Set("TextSecondaryBrush", colors.TextSecondary);
        Set("BorderBrushColor", colors.Border);
        Set("SurfaceBrush", colors.Surface);
        Set("BgBrush", colors.Bg);
        Set("NavHoverBrush", colors.NavHover);
        Set("NavPressedBrush", colors.NavPressed);
        Set("BottomHoverBrush", colors.BottomHover);
        Set("BottomPressedBrush", colors.BottomPressed);

        // 替换为新的 Brush 实例（App.xaml 中声明的 Brush 会被 WPF 冻结为只读，不能直接改 Color；
        // 引用色板的地方使用 DynamicResource，替换字典条目后会自动刷新）。
        // 先移除旧条目再添加，避免在已冻结的 Brush 上执行 SetValue 抛出只读异常。
        static void Set(string key, Color color)
        {
            if (Current == null)
                return;
            var dict = Current.Resources;
            if (dict.Contains(key))
                dict.Remove(key);
            dict.Add(key, new SolidColorBrush(color));
        }
    }

    /// <summary>将异常写入 %AppData%/BettyTranslate/error.log</summary>
    private static void LogError(string source, Exception? ex)
    {
        if (ex == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败时静默，避免二次异常
        }
    }
}
