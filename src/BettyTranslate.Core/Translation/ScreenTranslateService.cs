using BettyTranslate.Core.Capture;
using BettyTranslate.Core.Ocr;
using BettyTranslate.Core.Translation;

namespace BettyTranslate.Core.Translation;

/// <summary>一行原文与对应译文</summary>
public sealed class TranslatedLine
{
    public TranslatedLine(string source, string translation)
    {
        Source = source;
        Translation = translation;
    }

    /// <summary>识别出的原文（一行）</summary>
    public string Source { get; }

    /// <summary>译文</summary>
    public string Translation { get; }
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

    public ScreenTranslateService(ICaptureService capture, IOcrEngine ocr, ITranslateProvider translator)
    {
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
    }

    /// <summary>执行一次全屏翻译，返回逐行原文+译文</summary>
    public async Task<IReadOnlyList<TranslatedLine>> TranslateScreenAsync()
    {
        using var bitmap = _capture.CaptureFullScreen();
        var result = await _ocr.RecognizeAsync(bitmap);

        var lines = new List<TranslatedLine>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            var text = line.Text.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            var translation = await _translator.TranslateAsync(text, "zh");
            lines.Add(new TranslatedLine(text, translation));
        }
        return lines;
    }
}
