using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace BettyTranslate.Core.Ocr;

/// <summary>
/// 基于系统内置 Windows.Media.Ocr 的识别实现（离线、零模型下载）。
/// 语言选择：优先英文包（识别英文界面），无则回退用户语言包。
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private OcrEngine? _engine;

    private OcrEngine GetEngine()
    {
        if (_engine != null)
            return _engine;

        var langs = OcrEngine.AvailableRecognizerLanguages.ToList();
        if (langs.Count == 0)
            throw new InvalidOperationException("系统未安装任何 OCR 语言包，无法识别屏幕文字");

        // 优先英文（英文软件界面最常见），否则用第一个可用语言
        var lang = langs.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                   ?? langs[0];
        _engine = OcrEngine.TryCreateFromLanguage(lang)
                  ?? OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? throw new InvalidOperationException("无法创建 OCR 引擎");
        return _engine;
    }

    public async Task<OcrResult> RecognizeAsync(Bitmap bitmap)
    {
        var engine = GetEngine();
        var softwareBitmap = ToSoftwareBitmap(bitmap);
        var result = await engine.RecognizeAsync(softwareBitmap);

        var lines = result.Lines.Select(l =>
        {
            // OcrLine.BoundingRect 为相对整图坐标（RectInt32）
            var r = l.Words.Count > 0
                ? UnionWords(l.Words)
                : new Rectangle(0, 0, 0, 0);
            return new OcrLine(l.Text, r);
        }).ToList();

        return new OcrResult(lines);
    }

    private static Rectangle UnionWords(IEnumerable<OcrWord> words)
    {
        var rects = words.Select(w => w.BoundingRect);
        var x = (int)rects.Min(r => r.X);
        var y = (int)rects.Min(r => r.Y);
        var right = (int)rects.Max(r => r.X + r.Width);
        var bottom = (int)rects.Max(r => r.Y + r.Height);
        return new Rectangle(x, y, right - x, bottom - y);
    }

    /// <summary>将 System.Drawing.Bitmap 转为 WinRT SoftwareBitmap（BGRA8）</summary>
    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var src = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, src, 0, src.Length);

            // SoftwareBitmap 要求每行 4 字节对齐；stride 可能大于 width*4，需逐行拷贝
            var widthBytes = bmp.Width * 4;
            var dst = new byte[widthBytes * bmp.Height];
            for (var row = 0; row < bmp.Height; row++)
                Buffer.BlockCopy(src, row * stride, dst, row * widthBytes, widthBytes);

            var buffer = dst.AsBuffer();
            return SoftwareBitmap.CreateCopyFromBuffer(
                buffer, BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
