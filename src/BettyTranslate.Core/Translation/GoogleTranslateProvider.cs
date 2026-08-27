using System.Net;
using System.Text;
using System.Text.Json;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 谷歌翻译网页接口实现：免密钥、免费，作为百度翻译不可达时的备用通道。
/// 注意：文本会发送到 Google 服务器。
/// </summary>
public sealed class GoogleTranslateProvider : ITranslateProvider
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";

    private readonly HttpClient _http;

    public GoogleTranslateProvider()
    {
        // 走系统代理：诊断验证谷歌在系统代理下可达（HTTP 200），直连会超时
        var proxy = WebRequest.GetSystemWebProxy();
        var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = proxy != null,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<string> TranslateAsync(string text, string toLanguage)
    {
        var url = $"{Endpoint}?client=gtx&sl=auto&tl={Uri.EscapeDataString(toLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";

        using var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new InvalidOperationException("谷歌翻译返回了无法解析的结果");

        var segments = root[0];
        if (segments.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("谷歌翻译返回了无法解析的结果");

        var sb = new StringBuilder();
        foreach (var seg in segments.EnumerateArray())
        {
            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0)
                sb.Append(seg[0].GetString());
        }

        var result = sb.ToString();
        if (string.IsNullOrEmpty(result))
            throw new InvalidOperationException("谷歌翻译返回空结果");
        return result;
    }
}
