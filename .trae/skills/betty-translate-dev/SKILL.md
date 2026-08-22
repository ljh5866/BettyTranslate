---
name: "betty-translate-dev"
description: "Guides development of the Betty Translate WPF desktop screen-translation app (OCR + Supabase login). Invoke when writing code, adding features, or fixing bugs in this WPF/.NET 8 project."
---

# Betty Translate 开发技能包

指导「Betty Translate」桌面屏幕翻译应用的开发。技术栈：**WPF + C# + .NET 8 + Supabase Auth**，仅 Windows 平台。产品/架构/计划文档见 `docs/` 目录，开发前先阅读 `docs/04_系统架构设计.md`。

## 项目铁律

1. **只引入实际用到的库，不整包导入**（用户规则）。新增 NuGet 依赖前先确认是否已有内置/自研方案（如全局热键用 Win32 P/Invoke，不引重型库）。
2. 代码注释使用中文。
3. 默认离线优先：OCR 与可选离线翻译不发网络请求；截图与识别文本默认不落盘。
4. 通用模块抽象接口（`IOcrEngine`、`ITranslateProvider`、`IAuthService`），便于替换实现。
5. 遵守 MIT 许可，不引入 GPL 代码。

## 1. 项目结构

```
Betty_Translate/
├── src/BettyTranslate.App/          # WPF 主程序（Views + ViewModels）
├── src/BettyTranslate.Core/         # 领域服务（Auth/Capture/Ocr/Translation/Overlay/Settings）
└── src/BettyTranslate.Tests/
```

## 2. MVVM 基础（CommunityToolkit.MVVM）

- 使用 `CommunityToolkit.Mvvm` 的 `ObservableObject` + `RelayCommand`，禁止在 Code-Behind 写业务逻辑。
- 命令用 `ICommand`，支持 `CanExecute` 控制可用态与快捷键绑定。

```csharp
public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    public IAsyncRelayCommand SignInCommand { get; }

    public LoginViewModel(IAuthService auth)
    {
        SignInCommand = new AsyncRelayCommand(SignInAsync, () => !string.IsNullOrWhiteSpace(Email));
    }

    private async Task SignInAsync()
    {
        var ok = await _auth.SignInAsync(Email, Password);
        // 成功后切换到主界面
    }
}
```

## 3. 全局热键（Win32 RegisterHotKey，零依赖）

不要引第三方热键库，直接用 P/Invoke。

```csharp
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;

    public void Register(Window window, int id, uint modifiers, uint vk)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        source.AddHook(WndProc);
        RegisterHotKey(handle, id, modifiers, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY) { HotKeyPressed?.Invoke(wParam.ToInt32()); handled = true; }
        return IntPtr.Zero;
    }
    public event Action<int>? HotKeyPressed;
    public void Dispose() { /* 释放时 UnregisterHotKey 全部热键 */ }
}
```

注意：`RegisterHotKey` 返回 false 表示快捷键被其他程序占用，需提示用户更换。

## 4. 屏幕截图（多显示器 + DPI）

- 多显示器：用**虚拟桌面坐标** `SystemParameters.VirtualScreenLeft/Top/Width/Height`，不要用主屏坐标。
- 高性能优先 `Windows.Graphics.Capture`（Win10 1903+）；兼容兜底 `Graphics.CopyFromScreen`。
- WPF 需声明 Per-Monitor DPI 感知（app.manifest 中 `PerMonitorV2`），否则多屏缩放会偏移/模糊。
- 根元素设 `UseLayoutRounding="True"`，避免细线发虚。

```csharp
// 全屏截图（虚拟屏幕范围）
using var bmp = new Bitmap(SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
using var g = Graphics.FromImage(bmp);
g.CopyFromScreen(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, 0, 0, bmp.Size);
```

## 5. OCR（PaddleOCR 离线为主 + Windows OCR 兜底）

优先 **PaddleOCRSharp**（NuGet，封装 PaddleOCR，返回文本与坐标）：

```csharp
var param = new OCRParameter { numThread = 6, Enable_mkldnn = 1, cls = 1, det = 1 };
using var engine = new PaddleOCREngine(null, param);   // 只初始化一次，复用实例
var result = engine.DetectText(bitmap);                 // OCRResult.Text 为全文
```

兜底 **Windows.Media.Ocr**（系统内置，快，无需模型），识别失败时自动降级。

## 6. Supabase Auth 登录（supabase-csharp）

- 初始化 `supabase-csharp`（NuGet），URL/Key 从配置读取，不要硬编码。

```csharp
var options = new SupabaseOptions
{
    AutoRefreshToken = true,
    AutoConnectRealtime = false,
    // SessionHandler = new DpapiSessionHandler()   // 会话持久化需自实现
};
var supabase = new Supabase.Client(url, key, options);
await supabase.InitializeAsync();
```

- 登录：`await supabase.Auth.SignInWithPassword(email, password)`；注册：`SignUp`；退出：`SignOut`。
- **会话持久化**：实现 `ISessionHandler`，用 Windows DPAPI（`ProtectedData`）加密存储 refreshToken 到 `%AppData%/BettyTranslate/`，启动时 `LoadSession` 恢复登录态。
- 未登录时禁用翻译快捷键；登录后进入主界面。

## 7. 翻译引擎抽象

```csharp
public interface ITranslateProvider
{
    string Name { get; }
    Task<string> TranslateAsync(string text, string from, string to);
}
```

默认实现「百度翻译」（免费直连，走 HttpClient）；可扩展 DeepL / Google / OpenAI。批量翻译时控制并发（如 5），失败自动重试一次。

## 8. 悬浮窗（Overlay）

- 置顶半透明无边框窗口：`WindowStyle=None`、`AllowsTransparency=True`、`Topmost=True`。
- 截屏前排除本应用窗口区域，避免「翻译结果被自己再翻译」（Translumo「Exclude from Capture」思路）。

## 9. 核心流程速查

```
按 F1 → HotkeyService 触发 → 确认弹窗「是否进行屏幕翻译？」[是/否]
→ 是 → CaptureService 全屏截图 → OcrService 识别(带坐标) → TranslateService 批量翻译
→ OverlayService 悬浮窗展示原文/译文对照
```

## 10. 常用命令

```powershell
dotnet new wpf -n BettyTranslate.App
dotnet add package supabase-csharp
dotnet add package CommunityToolkit.Mvvm
dotnet add package PaddleOCRSharp
dotnet build
dotnet run --project src/BettyTranslate.App
```
