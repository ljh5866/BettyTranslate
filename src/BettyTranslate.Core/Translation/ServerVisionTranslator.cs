using System.Drawing;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 服务端代理视觉翻译器：调用 Supabase Edge Function（vision-translate）。
/// 开发者预置的 DeepSeek API Key 存放在服务端 secrets，客户端只携带登录态（JWT），
/// 由服务端校验免费额度、调用 DeepSeek、累加计数，并把 regions JSON 原样返回。
/// 相比客户端直连，客户端不再接触任何开发者 Key，防止泄漏/滥用。
/// </summary>
public sealed class ServerVisionTranslator : IVisionTranslator
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly string _functionUrl;
    private readonly string _accessToken;

    /// <summary>
    /// 创建服务端代理翻译器。
    /// </summary>
    /// <param name="functionUrl">Edge Function 完整地址，形如 https://ref.supabase.co/functions/v1/vision-translate</param>
    /// <param name="accessToken">当前登录用户的 Supabase 访问令牌（JWT）</param>
    public ServerVisionTranslator(string functionUrl, string accessToken)
    {
        _functionUrl = functionUrl;
        _accessToken = accessToken;
    }

    public async Task<IReadOnlyList<ImageComposer.TranslatedRegion>> TranslateAsync(Bitmap bitmap)
    {
        var b64 = DeepSeekVisionTranslator.ToJpegBase64(bitmap);

        using var msg = new HttpRequestMessage(HttpMethod.Post, _functionUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { image_base64 = b64 }),
                Encoding.UTF8, "application/json"),
        };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var resp = await _http.SendAsync(msg);
        var body = (await resp.Content.ReadAsStringAsync()).Trim();

        if (!resp.IsSuccessStatusCode)
        {
            var message = ExtractError(body) ?? $"服务端接口返回 {(int)resp.StatusCode}";
            // 额度用尽是硬性限制，抛专用异常让上层阻止回退
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new FreeQuotaExceededException(message);
            throw new InvalidOperationException(message);
        }

        // Edge Function 返回 { regions: [...] }，与客户端 ParseRegions 期望的 JSON 结构一致
        var regions = DeepSeekVisionTranslator.ParseRegions(body, bitmap);
        if (regions.Count == 0)
            throw new InvalidOperationException("图中未识别到可翻译的英文文本");

        return regions;
    }

    /// <summary>从 Edge Function 的错误响应里提取 error 字段（若存在）</summary>
    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) &&
                e.ValueKind == JsonValueKind.String)
                return e.GetString();
        }
        catch
        {
            // 解析失败时忽略，交由调用方用状态码提示
        }
        return null;
    }
}
