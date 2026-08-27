using System.Drawing;
using System.IO;
using BettyTranslate.Core.Capture;
using BettyTranslate.Core.Ocr;
using BettyTranslate.Core.Translation;

namespace BettyTranslate.Core.Translation;

/// <summary>一行原文与对应译文（含其在屏幕上的位置，用于覆盖显示）</summary>
public sealed class TranslatedLine
{
    public TranslatedLine(string source, string translation, Rectangle screenBounds)
    {
        Source = source;
        Translation = translation;
        ScreenBounds = screenBounds;
    }

    /// <summary>识别出的原文（一行）</summary>
    public string Source { get; }

    /// <summary>译文</summary>
    public string Translation { get; }

    /// <summary>该行在屏幕上的位置（物理像素坐标）</summary>
    public Rectangle ScreenBounds { get; }
}

/// <summary>
/// 屏幕翻译编排：截图 → OCR 识别 → 逐行翻译，返回结果列表。
/// 调用方负责显示悬浮窗。
/// </summary>
public sealed class ScreenTranslateService
{
    private readonly ICaptureService _capture;
    private readonly IOcrEngine _ocr;
    private readonly ITranslateProvider _translator;

    /// <summary>跟踪日志路径（应用目录 logs/trace.log），用于排查闪退位置</summary>
    private static string TracePath => Path.Combine(
        AppContext.BaseDirectory, "logs", "trace.log");

    public ScreenTranslateService(ICaptureService capture, IOcrEngine ocr, ITranslateProvider translator)
    {
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
    }

    private static void Trace(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TracePath)!);
            File.AppendAllText(TracePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }

    /// <summary>
    /// 对屏幕指定区域执行翻译：截图 → OCR → 逐行翻译，
    /// 返回带屏幕坐标的原文+译文（用于在原位置覆盖显示）。
    /// </summary>
    public async Task<IReadOnlyList<TranslatedLine>> TranslateRegionAsync(Rectangle screenBounds)
    {
        Trace("开始区域翻译");
        using var bitmap = _capture.CaptureRegion(screenBounds);
        Trace($"截图完成 {bitmap.Width}x{bitmap.Height}");

        var result = await _ocr.RecognizeAsync(bitmap);
        Trace($"OCR 完成，识别 {result.Lines.Count} 行");

        // 筛选需要翻译的行（保留原始顺序，记录序号）
        var toTranslate = new List<(int Index, Rectangle Bounds, string Text)>();
        foreach (var line in result.Lines)
        {
            var text = line.Text.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            // 只翻译含英文字母的内容，跳过纯数字/日期/符号等无意义文本
            if (!text.Any(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
                continue;

            toTranslate.Add((toTranslate.Count + 1, line.BoundingRect, text));
        }
        Trace($"筛选出 {toTranslate.Count} 行待翻译");

        // 并行翻译（限流并发 6），单行失败降级为原文，不中断整体流程
        using var gate = new SemaphoreSlim(6);
        var translated = new TranslatedLine[toTranslate.Count];
        await Task.WhenAll(toTranslate.Select(async item =>
        {
            await gate.WaitAsync();
            try
            {
                Trace($"翻译第 {item.Index} 行: {item.Text}");
                var translation = await _translator.TranslateAsync(item.Text, "zh");
                Trace($"第 {item.Index} 行译文: {translation}");
                translated[item.Index - 1] = new TranslatedLine(
                    item.Text, translation, ToScreenBounds(screenBounds, item.Bounds));
            }
            catch (Exception ex)
            {
                Trace($"第 {item.Index} 行翻译失败: {ex.Message}");
                translated[item.Index - 1] = new TranslatedLine(
                    item.Text, item.Text, ToScreenBounds(screenBounds, item.Bounds));
            }
            finally
            {
                gate.Release();
            }
        }));

        Trace($"翻译完成，共 {translated.Length} 行");
        return translated;
    }

    /// <summary>将 OCR 相对截图的坐标换算为屏幕绝对坐标</summary>
    private static Rectangle ToScreenBounds(Rectangle region, Rectangle local)
    {
        return new Rectangle(region.X + local.X, region.Y + local.Y, local.Width, local.Height);
    }
}
