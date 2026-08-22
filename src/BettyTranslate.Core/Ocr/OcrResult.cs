using System.Drawing;

namespace BettyTranslate.Core.Ocr;

/// <summary>OCR 识别出的单行文本</summary>
public sealed class OcrLine
{
    public OcrLine(string text, Rectangle boundingRect)
    {
        Text = text;
        BoundingRect = boundingRect;
    }

    /// <summary>行文本</summary>
    public string Text { get; }

    /// <summary>行在截图中的位置（像素）</summary>
    public Rectangle BoundingRect { get; }
}

/// <summary>OCR 识别结果</summary>
public sealed class OcrResult
{
    public OcrResult(IReadOnlyList<OcrLine> lines)
    {
        Lines = lines;
    }

    /// <summary>识别出的文本行（按阅读顺序）</summary>
    public IReadOnlyList<OcrLine> Lines { get; }

    /// <summary>拼接后的全文</summary>
    public string FullText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
}
