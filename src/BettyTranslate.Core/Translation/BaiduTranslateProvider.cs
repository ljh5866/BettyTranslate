using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 百度翻译开放平台（标准版）实现：免费、无需模型。
/// 需在 https://fanyi-api.baidu.com 申请 AppId 与密钥，填入 appsettings.json。
/// </summary>
public sealed class BaiduTranslateProvider : ITranslateProvider
{
    private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";

    private readonly string _appId;
    private readonly string _secretKey;
    private readonly HttpClient _http;

    public BaiduTranslateProvider(string appId, string secretKey)
    {
        _appId = appId;
        _secretKey = secretKey;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<string> TranslateAsync(string text, string toLanguage)
    {
        if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_secretKey))
            throw new InvalidOperationException("未配置百度翻译 AppId/密钥，请前往 fanyi-api.baidu.com 免费申请后填入配置");

        var salt = RandomNumberGenerator.GetInt32(int.MaxValue).ToString();
        var sign = Md5(_appId + text + salt + _secretKey);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = text,
            ["from"] = "auto",
            ["to"] = toLanguage,
            ["appid"] = _appId,
            ["salt"] = salt,
            ["sign"] = sign,
        });

        using var resp = await _http.PostAsync(Endpoint, form);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error_code", out var errorCode))
        {
            var msg = root.TryGetProperty("error_msg", out var m) ? m.GetString() : "未知错误";
            throw new InvalidOperationException($"百度翻译失败（{errorCode.GetString()}）：{msg}");
        }

        if (root.TryGetProperty("trans_result", out var results))
        {
            var dst = results.EnumerateArray()
                .Select(r => r.GetProperty("dst").GetString())
                .Where(s => !string.IsNullOrEmpty(s));
            return string.Join(string.Empty, dst);
        }

        throw new InvalidOperationException("百度翻译返回了无法解析的结果");
    }

    private static string Md5(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
