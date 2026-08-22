using System.Drawing;

namespace BettyTranslate.Core.Ocr;

/// <summary>
/// OCR 引擎抽象：不同实现（Windows OCR / PaddleOCR 等）可替换
/// </summary>
public interface IOcrEngine
{
    /// <summary>识别位图中的文字，返回按行排列的结果</summary>
    Task<OcrResult> RecognizeAsync(Bitmap bitmap);
}
