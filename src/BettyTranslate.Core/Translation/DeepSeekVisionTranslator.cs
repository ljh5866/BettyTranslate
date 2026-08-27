using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 基于 DeepSeek 视觉大模型的图片翻译器：一次调用即可识别图中英文并翻译成中文，
/// 同时返回每段文本的包围盒（用原图像素坐标表示），供合成器覆盖绘制。
/// 相比「本地 OCR + 逐行翻译」，对复杂界面/艺术字/英文夹中文的识别与翻译质量更好。
/// </summary>
public sealed class DeepSeekVisionTranslator : IVisionTranslator
{
    private const string Endpoint = "https://api.deepseek.com/chat/completions";
    private const string Model = "deepseek-v4-flash-vision-exp";

    private readonly HttpClient _http;

    public DeepSeekVisionTranslator(string apiKey)
    {
        // DeepSeek 是国内可直接访问的服务，不再走系统代理（避免 GFW 代理误把
        // 国内请求转发到国外导致超时/失败）；Google 等被墙通道才需要代理。
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// 识别并翻译图中英文，返回带局部坐标（相对截图）的译文区域。
    /// 未识别到英文、或接口异常时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public async Task<IReadOnlyList<ImageComposer.TranslatedRegion>> TranslateAsync(Bitmap bitmap)
    {
        var b64 = ToJpegBase64(bitmap);

        var (statusCode, body) = await PostAsync(b64, useResponseFormat: true);

        // 有些模型/版本不接受 response_format（返回 400/422 等），去掉该字段重试一次
        if (statusCode is 400 or 422)
            (statusCode, body) = await PostAsync(b64, useResponseFormat: false);

        if (statusCode != 200)
            throw new InvalidOperationException($"DeepSeek 接口返回 {statusCode}：{body}");

        var content = ExtractContent(body);
        var regions = ParseRegions(content, bitmap);
        if (regions.Count == 0)
            throw new InvalidOperationException("图中未识别到可翻译的英文文本");

        return regions;
    }

    private async Task<(int StatusCode, string Body)> PostAsync(string b64, bool useResponseFormat)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["messages"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = new object[]
                    {
                        new { type = "text", text = BuildPrompt() },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{b64}" } },
                    },
                },
            },
            ["temperature"] = 0.2,
            ["max_tokens"] = 4000,
            // deepseek-v4-flash-vision-exp 默认开启思考模式（thinking），会把全部 token
            // 花在 reasoning_content 上、最终 content 为空。必须显式关闭思考模式，才能直接拿回 JSON。
            ["thinking"] = new { type = "disabled" },
        };
        // 只有需要时才带上 json_object，避免某些模型因该字段直接拒绝请求
        if (useResponseFormat)
            payload["response_format"] = new { type = "json_object" };

        using var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(Endpoint, req);
        var body = await resp.Content.ReadAsStringAsync();

        return ((int)resp.StatusCode, body.Trim());
    }

    private static string ExtractContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("DeepSeek 返回结果缺少 choices");

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("DeepSeek 未返回文本内容");

        return content.GetString() ?? string.Empty;
    }

    internal static List<ImageComposer.TranslatedRegion> ParseRegions(string json, Bitmap bitmap)
    {
        var regions = new List<ImageComposer.TranslatedRegion>();
        // 兼容模型可能把 JSON 包进 ```json ... ``` 代码块的情况
        var cleaned = StripFences(json);
        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;
        if (!root.TryGetProperty("regions", out var arr))
            return regions;

        foreach (var item in arr.EnumerateArray())
        {
            var text = item.TryGetProperty("text", out var te) ? te.GetString() : null;
            var translation = item.TryGetProperty("translation", out var tr) ? tr.GetString() : null;

            // 只保留真正翻译成了中文的区域，剔除垃圾（坐标/数字）、专有名词、以及本就
            // 是中文的重绘（如“358 个评价”），否则会在原图上重影/叠出蓝色乱码。
            if (!IsUsefulTranslation(text, translation))
                continue;

            var cx = GetDouble(item, "cx");
            var cy = GetDouble(item, "cy");
            var w = Math.Max(1, GetDouble(item, "w"));
            var h = Math.Max(1, GetDouble(item, "h"));

            // 百分比 → 原图像素坐标，并裁剪到图像内
            var x = Clamp((cx - w / 2) / 100.0 * bitmap.Width, 0, bitmap.Width - 1);
            var y = Clamp((cy - h / 2) / 100.0 * bitmap.Height, 0, bitmap.Height - 1);
            var rw = Math.Min(w / 100.0 * bitmap.Width, bitmap.Width - x);
            var rh = Math.Min(h / 100.0 * bitmap.Height, bitmap.Height - y);
            if (rw < 1 || rh < 1)
                continue;

            regions.Add(new ImageComposer.TranslatedRegion(
                new Rectangle((int)x, (int)y, (int)rw, (int)rh), translation!.Trim()));
        }
        return regions;
    }

    private static double GetDouble(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>去掉模型返回里可能出现的 ```json ... ``` 代码块标记，只保留 JSON 内容</summary>
    private static string StripFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            var firstNewline = t.IndexOf('\n');
            if (firstNewline >= 0)
                t = t[(firstNewline + 1)..];
            // 去掉结尾的 ``` 及后续
            var endFence = t.LastIndexOf("```");
            if (endFence >= 0)
                t = t[..endFence].Trim();
        }

        // 若内容不止一段 JSON，提取第一个以 { 开始、匹配到结尾 } 的对象
        var start = t.IndexOf('{');
        if (start > 0)
            t = t[start..];
        var end = t.LastIndexOf('}');
        if (end >= 0 && end < t.Length - 1)
            t = t[..(end + 1)];
        return t;
    }

    private static double Clamp(double v, double min, double max) => Math.Min(Math.Max(v, min), max);

    /// <summary>
    /// 是否值得绘制该区域：译文必须含中文，且与原文不同。
    /// 这能滤掉模型的垃圾输出（如把 (43,260) 当译文）、纯英文专有名词（MattBny）、
    /// 以及本就是中文无需翻译的文字（358 个评价），避免在原图上重影/叠出乱码。
    /// </summary>
    private static bool IsUsefulTranslation(string? text, string? translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return false;
        if (!ContainsCjk(translation))
            return false; // 译文没有中文 → 专有名词/坐标/数字等，无需重绘
        if (!string.IsNullOrWhiteSpace(text) &&
            string.Equals(text.Trim(), translation.Trim(), StringComparison.Ordinal))
            return false; // 译文与原文完全相同 → 原文已是中文或无需翻译，重绘会产生重影
        return true;
    }

    private static bool ContainsCjk(string s)
    {
        foreach (var ch in s)
        {
            // 基本汉字区（含常用字），覆盖简体中文
            if (ch >= 0x4E00 && ch <= 0x9FFF)
                return true;
        }
        return false;
    }

    private static string BuildPrompt() =>
        """
        你是图像文字翻译助手。请识别图片中的英文文本并翻译成简体中文，最终只输出一个 JSON 对象，不要输出任何其他文字或解释：
        {"regions":[{"text":"英文原文","translation":"简体中文译文","cx":中心X百分比,"cy":中心Y百分比,"w":宽百分比,"h":高百分比}]}

        严格要求：
        1. 只识别【英文】文本。本身就是中文的文字、纯数字、坐标（如 (43,260)）、纯数字统计（如 358 个评价）一律不要放进 regions。
        2. 若某段英文被翻译后结果仍全是英文（如人名/专有名词 MattBny、Konsta★Starlight、VSLib），该区域【不要】输出。
        3. 英文夹中文时，只翻译英文部分，中文保持原样，输出合并后的整段中文（例如 "创作者:MattBny" → translation 为 "创作者:MattBny"）。若该段除专有名词外没有英文，不要输出。
        4. 坐标用图片宽/高的百分比（0~100 的数，可带小数）。包围盒要【略宽松】，让盒子上下左右都比文字再多出约 4% 的空白边距，确保足够盖住英文。
        5. 同一行、同一句、同一按钮上相邻的英文合并成一个 region，不要把一句话拆成多块。
        6. regions 按图片从上到下、从左到右排列。不要遗漏任何需要翻译的英文。
        """;

    internal static string ToJpegBase64(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return Convert.ToBase64String(ms.ToArray());
    }
}
