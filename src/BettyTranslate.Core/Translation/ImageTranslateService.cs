using System.Drawing;
using System.Linq;
using BettyTranslate.Core.Capture;
using BettyTranslate.Core.Ocr;
using BettyTranslate.Core.Translation;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 图片翻译编排：截图 → 识别+翻译（视觉大模型优先，失败回退 OCR+逐行翻译）→ 合成一张贴合选区的新图片。
/// 与屏幕翻译（悬浮文字框覆盖）不同，本服务把译文直接绘入位图，生成可保存的图片。
/// </summary>
public sealed class ImageTranslateService
{
    private readonly ICaptureService _capture;
    private readonly IOcrEngine _ocr;
    private readonly ITranslateProvider _translator;

    public ImageTranslateService(ICaptureService capture, IOcrEngine ocr, ITranslateProvider translator)
    {
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
    }

    /// <summary>
    /// 对指定屏幕区域生成翻译图片：截图 → 识别翻译 → 合成新图。
    /// vision 为本次翻译使用的视觉通道（可为 null，null 时回退 OCR 通道），
    /// 由调用方按免费额度/用户自备 Key 决定。
    /// allowOcrFallback 为 false 时视觉通道失败会向上抛出，不回退到 OCR（用于免费路径，防止绕过额度）。
    /// </summary>
    public async Task<ImageTranslateResult> TranslateRegionAsync(Rectangle screenBounds,
        IVisionTranslator? vision = null, bool allowOcrFallback = true)
    {
        using var bitmap = _capture.CaptureRegion(screenBounds);
        return await TranslateBitmapAsync(bitmap, vision, allowOcrFallback);
    }

    /// <summary>捕获指定屏幕区域，返回位图（调用方负责释放）</summary>
    public Bitmap CaptureRegion(Rectangle screenBounds) => _capture.CaptureRegion(screenBounds);

    /// <summary>对已捕获的位图执行识别翻译并合成新图（视觉优先，失败回退 OCR）</summary>
    public async Task<ImageTranslateResult> TranslateBitmapAsync(Bitmap bitmap,
        IVisionTranslator? vision = null, bool allowOcrFallback = true)
    {
        Log($"enter TranslateBitmapAsync size={bitmap.Width}x{bitmap.Height} vision={(vision != null)}");
        List<ImageComposer.TranslatedRegion>? regions = null;
        if (vision != null)
        {
            try
            {
                regions = (await vision.TranslateAsync(bitmap)).ToList();
                Log($"vision ok count={regions.Count}");
            }
            catch (Exception ex)
            {
                // 免费额度用尽是硬性限制：直接向上抛出，避免回退到 OCR（OCR 走开发者百度 Key，会绕过额度）。
                if (ex is FreeQuotaExceededException)
                    throw;
                Log("vision failed: " + ex.Message);
                // 视觉通道失败时，是否回退 OCR 由调用方决定（免费路径传 false）
                if (!allowOcrFallback)
                    throw;
            }
        }
        if (regions == null)
        {
            regions = await TranslateViaOcrAsync(bitmap);
            Log($"ocr fallback count={regions.Count}");
        }

        var composed = ImageComposer.Compose(bitmap, regions);
        Log($"compose done size={composed.Width}x{composed.Height} count={regions.Count}");
        WriteDebugArtifacts(bitmap, composed, regions);
        return new ImageTranslateResult(composed, regions);
    }

    /// <summary>运行时阶段日志，写到 %TEMP%/BettyImgDebug/run.log</summary>
    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "BettyImgDebug");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "run.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败忽略
        }
    }

    /// <summary>OCR 识别 + 逐行翻译（离线兜底通道）</summary>
    private async Task<List<ImageComposer.TranslatedRegion>> TranslateViaOcrAsync(Bitmap bitmap)
    {
        var result = await _ocr.RecognizeAsync(bitmap);

        // 筛选需翻译的行（保留原始顺序，仅记录局部坐标）
        var regions = new List<(int Index, Rectangle Bounds, string Text)>();
        foreach (var line in result.Lines)
        {
            var text = line.Text.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            // 只翻译含英文字母的内容，跳过纯数字/日期/符号等
            if (!text.Any(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
                continue;

            regions.Add((regions.Count + 1, line.BoundingRect, text));
        }

        // 并行翻译（限流并发 6），单行失败降级为原文
        var translations = new string[regions.Count];
        using (var gate = new SemaphoreSlim(6))
        {
            await Task.WhenAll(regions.Select(async item =>
            {
                await gate.WaitAsync();
                try
                {
                    translations[item.Index - 1] = await _translator.TranslateAsync(item.Text, "zh");
                }
                catch
                {
                    translations[item.Index - 1] = item.Text;
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        // 组装译文区域，供合成器绘制
        var translated = new List<ImageComposer.TranslatedRegion>(regions.Count);
        for (var i = 0; i < regions.Count; i++)
            translated.Add(new ImageComposer.TranslatedRegion(regions[i].Bounds, translations[i]));

        return translated;
    }

    /// <summary>临时调试：把捕获图、合成图与识别结果写到 %TEMP%/BettyImgDebug，便于定位“预览空白”问题</summary>
    private static void WriteDebugArtifacts(Bitmap source, Bitmap composed,
        IReadOnlyList<ImageComposer.TranslatedRegion> regions)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "BettyImgDebug");
            Directory.CreateDirectory(dir);
            source.Save(Path.Combine(dir, "source.png"), System.Drawing.Imaging.ImageFormat.Png);
            composed.Save(Path.Combine(dir, "composed.png"), System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText(Path.Combine(dir, "regions.txt"),
                $"source={source.Width}x{source.Height} composed={composed.Width}x{composed.Height} count={regions.Count}" +
                Environment.NewLine +
                string.Join(Environment.NewLine, regions.Select(r =>
                    $"x={r.Bounds.X} y={r.Bounds.Y} w={r.Bounds.Width} h={r.Bounds.Height} text={r.Translation}")));
        }
        catch
        {
            // 调试写入失败忽略
        }
    }
}

/// <summary>图片翻译结果：合成后的新图 + 译文区域</summary>
public sealed class ImageTranslateResult
{
    public ImageTranslateResult(Bitmap image, IReadOnlyList<ImageComposer.TranslatedRegion> regions)
    {
        Image = image;
        Regions = regions;
    }

    /// <summary>合成完成的新图片（贴合选区大小）</summary>
    public Bitmap Image { get; }

    /// <summary>译文区域（坐标为相对截图的局部坐标）</summary>
    public IReadOnlyList<ImageComposer.TranslatedRegion> Regions { get; }
}
