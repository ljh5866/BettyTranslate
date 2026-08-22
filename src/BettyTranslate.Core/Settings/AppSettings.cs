using System.Text.Json;

namespace BettyTranslate.Core.Settings;

/// <summary>
/// 应用配置：从 appsettings.json 读取（不引入额外配置库，仅用 System.Text.Json）
/// </summary>
public sealed class AppSettings
{
    public SupabaseSettings Supabase { get; set; } = new();

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
}

public sealed class SupabaseSettings
{
    public string Url { get; set; } = string.Empty;
    public string AnonKey { get; set; } = string.Empty;
}
