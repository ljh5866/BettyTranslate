using System.Linq;
using System.Text.Json;

namespace BettyTranslate.Core.Settings;

/// <summary>
/// 应用配置：从 appsettings.json 读取（不引入额外配置库，仅用 System.Text.Json）
/// </summary>
public sealed class AppSettings
{
    public SupabaseSettings Supabase { get; set; } = new();

    /// <summary>百度翻译开放平台配置（未配置时翻译功能提示申请）</summary>
    public BaiduTranslateSettings BaiduTranslate { get; set; } = new();

    /// <summary>屏幕翻译快捷键配置（悬浮文字框覆盖显示）</summary>
    public HotkeySettings Hotkey { get; set; } = new();

    /// <summary>图片翻译快捷键配置（生成翻译图片）</summary>
    public HotkeySettings ImageHotkey { get; set; } = new() { Modifiers = new() { "Control" }, Key = "F11" };

    /// <summary>划词翻译快捷键配置（选中文本后按快捷键翻译）</summary>
    public HotkeySettings SelectionHotkey { get; set; } = new() { Modifiers = new() { "Control" }, Key = "F12" };

    /// <summary>应用主题：light（默认浅色）/ dark（黑色）/ warm（暖色）</summary>
    public string Theme { get; set; } = "light";

    /// <summary>用户自定义的 DeepSeek API Key（免费体验用尽后由用户在设置页填写）。开发者预置 Key 已移至服务端 Edge Function，不再存放在客户端。</summary>
    public string UserDeepSeekKey { get; set; } = string.Empty;

    /// <summary>上次退出时是否正在运行屏幕翻译（下次启动自动恢复开启）</summary>
    public bool ScreenTranslateActive { get; set; }

    /// <summary>上次退出时是否正在运行图片翻译（下次启动自动恢复开启）</summary>
    public bool ImageTranslateActive { get; set; }

    /// <summary>上次退出时是否正在运行划词翻译（下次启动自动恢复开启）</summary>
    public bool SelectionTranslateActive { get; set; }

    /// <summary>检查更新配置（GitHub Releases，未配置仓库时跳过检查）</summary>
    public UpdateSettings Update { get; set; } = new();

    /// <summary>从 JSON 文件加载配置；文件缺失或解析失败时返回默认配置</summary>
    public static AppSettings Load(string jsonPath)
    {
        try
        {
            if (File.Exists(jsonPath))
            {
                var json = File.ReadAllText(jsonPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (JsonException)
        {
            // 配置损坏时回退默认值
        }
        return new AppSettings();
    }

    /// <summary>保存配置到 JSON 文件</summary>
    public void Save(string jsonPath)
    {
        var dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class HotkeySettings
{
    /// <summary>修饰键列表（可多选）：Control / Alt / Shift / Win，默认 Ctrl</summary>
    public List<string> Modifiers { get; set; } = new() { "Control" };

    /// <summary>按键：字母 A~Z / 数字 0~9 / F1~F12，默认 F10</summary>
    public string Key { get; set; } = "F10";

    /// <summary>转换为 Win32 修饰键位（MOD_*，多修饰键按位或）</summary>
    public int ModifierVk => Modifiers.Aggregate(0,
        (acc, m) => acc | m switch
        {
            "Alt" => 0x0001,
            "Shift" => 0x0004,
            "Win" => 0x0008,
            _ => 0x0002, // Control
        });

    /// <summary>转换为 Win32 虚拟键码（字母/数字/F1~F12）</summary>
    public int KeyVk => ParseKeyVk(Key);

    /// <summary>显示名，如 Ctrl + Alt + F5</summary>
    public string DisplayName => string.Join(" + ", Modifiers.Concat(new[] { Key }));

    private static int ParseKeyVk(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return 0x79; // 默认 F10
        var k = key.Trim().ToUpperInvariant();
        if (k.Length == 1)
        {
            var c = k[0];
            if (c is >= 'A' and <= 'Z')
                return 0x41 + (c - 'A');
            if (c is >= '0' and <= '9')
                return 0x30 + (c - '0');
        }
        if (k.StartsWith('F') && int.TryParse(k[1..], out var n) && n is >= 1 and <= 12)
            return 0x70 + n - 1;
        return 0x79;
    }
}

public sealed class SupabaseSettings
{
    public string Url { get; set; } = string.Empty;
    public string AnonKey { get; set; } = string.Empty;

    /// <summary>图片翻译服务端代理的 Edge Function 名称（开发者预置 DeepSeek Key 存放在服务端 secrets）</summary>
    public string VisionFunctionName { get; set; } = "vision-translate";
}

public sealed class BaiduTranslateSettings
{
    public string AppId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}

public sealed class UpdateSettings
{
    /// <summary>GitHub 仓库所有者（如 zhangsan），留空则跳过检查</summary>
    public string RepoOwner { get; set; } = string.Empty;

    /// <summary>GitHub 仓库名称（如 BettyTranslate）</summary>
    public string RepoName { get; set; } = string.Empty;

    /// <summary>私有仓库时填 Personal Access Token；公开仓库留空</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>安装包资源名匹配子串（如 "setup"），空则按 exe/msi/zip 扩展名选取</summary>
    public string AssetPattern { get; set; } = string.Empty;
}
