using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 图片翻译合成：把识别出的英文区域用相邻背景色填充，并在原位置绘制中文译文，
/// 生成一张贴合选区大小、英文被中文替换的新图片。
/// </summary>
public static class ImageComposer
{
    /// <summary>单个待合成的译文区域（坐标相对截图的局部坐标）</summary>
    public sealed record TranslatedRegion(Rectangle Bounds, string Translation);

    /// <summary>中文字体（系统自带），用于覆盖英文</summary>
    private const string FontName = "Microsoft YaHei";

    public static Bitmap Compose(Bitmap source, IReadOnlyList<TranslatedRegion> regions)
    {
        var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        // 先铺底图
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));

        using var fontFamily = new FontFamily(FontName);
        foreach (var region in regions)
        {
            if (region.Bounds.Width < 4 || region.Bounds.Height < 4)
                continue;

            var bg = SampleEdgeColor(source, region.Bounds);
            var rect = CoverTextRegion(source, region.Bounds);

            // 用背景色块盖住原英文，再绘制中文
            using (var bgBrush = new SolidBrush(bg))
                g.FillRectangle(bgBrush, rect);

            var fontSize = FitFontSize(g, region.Translation, fontFamily, rect);
            if (fontSize < 8)
                fontSize = 8f; // 兜底：字号很小时也照常绘制，避免只填充背景却没文字（灰色块）

            using var font = new Font(fontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(ContrastColor(bg));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.LineLimit,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            g.DrawString(region.Translation, font, textBrush, rect, format);
        }

        return result;
    }

    /// <summary>
    /// 在模型包围盒附近“找出真实文字行/列”，从而生成一个真正盖住英文的覆盖矩形。
    /// 模型返回的包围盒存在系统性偏右、偏上的坐标误差（英文原点往往落在盒子之外），
    /// 固定几何扩容无法补齐，因此改为：① 在盒子附近做行投影找到真实文字行；
    /// ② 在该行内做列投影切出单词，再从离盒子中心最近的单词起向两侧吞并相邻单词，
    ///    得到整句文本范围；③ 以文字范围外加边距构成覆盖矩形。
    /// </summary>
    private static Rectangle CoverTextRegion(Bitmap source, Rectangle bounds)
    {
        var w = source.Width;
        var h = source.Height;
        var basePad = Math.Max(3, (int)(bounds.Height * 0.2f));
        if (bounds.Width < 4 || bounds.Height < 4)
            return Pad(bounds, basePad);

        // 多行段落：模型返回的包围盒高度远超单行（如整段正文），
        // 此时应把整段英文一次性盖住，并在原位换行绘制整段中文；
        // 而不是像单行文本那样只挑一条文字行，把整段译文挤成一行。
        if (bounds.Height > 55)
            return CoverParagraphRegion(source, bounds, w, h, basePad);

        // ① 在盒子上下 ±120px 内做行投影，找文字行
        var row0 = Math.Max(0, bounds.Y - 120);
        var row1 = Math.Min(h - 1, bounds.Y + bounds.Height + 120);
        if (row1 < row0)
            return Pad(bounds, basePad);

        // 模型返回的包围盒常系统性偏右/偏上，真实英文可能落在盒子左/右侧的空白带里。
        // 行投影的横向搜索范围向左右各拓宽 width，确保扫得到盒子附近的真实文字行。
        var sx0 = Math.Max(0, bounds.X - bounds.Width);
        var sx1 = Math.Min(w - 1, bounds.X + bounds.Width);
        var sWidth = sx1 - sx0 + 1;

        var rows = new List<int>();
        for (var y = row0; y <= row1; y++)
            if (RowHasContrast(source, y, sx0, sWidth)) rows.Add(y);
        var lines = Segment(rows, 4);
        if (lines.Count == 0)
            return Pad(bounds, basePad);

        // 选一条离盒子中心最近、且高度合理的行（剔除被整块海报糊成一列的巨块）。
        // 模型返回的包围盒系统性偏上（真实英文行通常落在盒中心下方），
        // 故优先向下选：只看中心 ≥ 盒中心的行，取最近；若无再看盒中心上方最近的。
        var boxCY = bounds.Y + bounds.Height / 2;
        (int a, int b)? best = null;
        var bestD = double.MaxValue;
        foreach (var line in lines)
        {
            var lineH = line.b - line.a + 1;
            if (lineH > Math.Max(80, bounds.Height * 2.5)) continue;
            var center = (line.a + line.b) / 2;
            if (center < boxCY) continue;
            var d = center - boxCY;
            if (d < bestD) { bestD = d; best = line; }
        }
        if (best is null)
        {
            foreach (var line in lines)
            {
                var lineH = line.b - line.a + 1;
                if (lineH > Math.Max(80, bounds.Height * 2.5)) continue;
                var center = (line.a + line.b) / 2;
                var d = Math.Abs(center - boxCY);
                if (d < bestD) { bestD = d; best = line; }
            }
        }
        if (best is null)
            return Pad(bounds, basePad);
        var (yT, yB) = best!.Value;

        // ② 在该行 y 带内做列投影切出单词（小 gap），再合并成整句文本
        var cx0 = Math.Max(0, bounds.X - 200);
        var cx1 = Math.Min(w - 1, bounds.X + bounds.Width + 200);
        var cols = new List<int>();
        for (var x = cx0; x <= cx1; x++)
            if (ColumnHasContrast(source, x, yT, yB - yT + 1)) cols.Add(x);
        var runs = Segment(cols, 8);
        if (runs.Count == 0)
            return Pad(bounds, basePad);

        var boxCX = bounds.X + bounds.Width / 2;
        var startIdx = 0;
        var bestDc = double.MaxValue;
        for (var i = 0; i < runs.Count; i++)
        {
            var center = (runs[i].a + runs[i].b) / 2;
            var d = Math.Abs(center - boxCX);
            if (d < bestDc) { bestDc = d; startIdx = i; }
        }

        // 从离盒子中心最近的单词起步，向左右吞并 gap ≤ 42px 的相邻单词；
        // 单词间距小会被吞并，而元素间距大（如与右上角星星相距 >100px）会停下。
        const int MergeGap = 42;
        var a = runs[startIdx].a;
        var b = runs[startIdx].b;
        while (startIdx > 0 && a - runs[startIdx - 1].b <= MergeGap)
        {
            a = runs[startIdx - 1].a;
            startIdx--;
        }
        var endIdx = startIdx;
        while (endIdx + 1 < runs.Count && runs[endIdx + 1].a - b <= MergeGap)
        {
            b = runs[endIdx + 1].b;
            endIdx++;
        }

        // ③ 以文字范围外加边距构成覆盖矩形
        var pad = Math.Max(2, (int)((yB - yT + 1) * 0.20f));
        return new Rectangle(a - pad, yT - pad, (b - a + 1) + pad * 2, (yB - yT + 1) + pad * 2);
    }

    /// <summary>
    /// 处理多行段落（模型把整段正文作为一个高矩形返回）：把整段英文一次性盖住，
    /// 给出一个能容纳整段中文、可换行铺满的区域，而不是像单行文本那样只挑一条文字行、
    /// 把整段译文挤成一条小色带。
    /// 行投影只在包围盒内扫描（不向上超出，避免盖掉渲染顺序上更早画的上一行文字区域）。
    /// </summary>
    private static Rectangle CoverParagraphRegion(Bitmap source, Rectangle bounds, int w, int h, int basePad)
    {
        var row0 = bounds.Y;
        var row1 = Math.Min(h - 1, bounds.Y + bounds.Height);
        if (row1 < row0)
            return Pad(bounds, basePad);

        // 横向搜索范围向左右各拓宽 width，避免模型 box 系统性偏移扫不到左右文字
        var sx0 = Math.Max(0, bounds.X - bounds.Width);
        var sx1 = Math.Min(w - 1, bounds.X + bounds.Width);
        var sWidth = sx1 - sx0 + 1;

        var rows = new List<int>();
        for (var y = row0; y <= row1; y++)
            if (RowHasContrast(source, y, sx0, sWidth)) rows.Add(y);
        if (rows.Count == 0)
            return Pad(bounds, basePad);

        // 段落的首尾两条真实文字行，构成整个段落的上下边界
        var yTop = rows[0];
        var yBottom = rows[^1];

        // 在整段高度内做列投影，找出左右文字边界
        var cx0 = Math.Max(0, bounds.X - 100);
        var cx1 = Math.Min(w - 1, bounds.X + bounds.Width + 100);
        var cols = new List<int>();
        for (var x = cx0; x <= cx1; x++)
            if (ColumnHasContrast(source, x, yTop, yBottom - yTop + 1)) cols.Add(x);
        if (cols.Count == 0)
            return Pad(bounds, basePad);

        var xLeft = cols[0];
        var xRight = cols[^1];

        // 覆盖矩形 = 整段文字范围 + 边距，让中文能在原位换行铺满整段
        const int pad = 4;
        return new Rectangle(
            xLeft - pad, yTop - 2,
            (xRight - xLeft + 1) + pad * 2,
            (yBottom - yTop + 1) + 4);
    }

    /// <summary>把 bounds 各向外扩 pad 一圈</summary>
    private static Rectangle Pad(Rectangle r, int pad) =>
        new(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2);

    /// <summary>把连续点按间距 gap 划分成若干段 [a, b]</summary>
    private static List<(int a, int b)> Segment(List<int> pts, int gap)
    {
        var segs = new List<(int a, int b)>();
        int? sa = null, sp = null;
        foreach (var p in pts)
        {
            if (sa is null) { sa = p; sp = p; continue; }
            if (p - sp > gap) { segs.Add((sa.Value, sp!.Value)); sa = p; }
            sp = p;
        }
        if (sa is not null) segs.Add((sa.Value, sp!.Value));
        return segs;
    }

    /// <summary>某一列在 y..y+h 范围内是否为文本列（含明显亮暗反差）</summary>
    private static bool ColumnHasContrast(Bitmap source, int x, int y, int height)
    {
        var y0 = Math.Max(0, y);
        var y1 = Math.Min(source.Height - 1, y + height - 1);
        var minLum = double.MaxValue;
        var maxLum = double.MinValue;
        for (var yy = y0; yy <= y1; yy++)
        {
            var lum = Luminance(source.GetPixel(x, yy));
            if (lum < minLum) minLum = lum;
            if (lum > maxLum) maxLum = lum;
        }
        // 对比度法：只要存在明显亮暗反差且有较亮像素即视为文字列
        return maxLum - minLum > 50 && maxLum > 130;
    }

    /// <summary>某一行在 x..x+w 范围内是否为文本行（含明显亮暗反差）</summary>
    private static bool RowHasContrast(Bitmap source, int y, int x, int width)
    {
        var x0 = Math.Max(0, x);
        var x1 = Math.Min(source.Width - 1, x + width - 1);
        var minLum = double.MaxValue;
        var maxLum = double.MinValue;
        for (var xx = x0; xx <= x1; xx++)
        {
            var lum = Luminance(source.GetPixel(xx, y));
            if (lum < minLum) minLum = lum;
            if (lum > maxLum) maxLum = lum;
        }
        return maxLum - minLum > 50 && maxLum > 130;
    }

    private static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    /// <summary>
    /// 采样矩形外部边缘颜色作为背景色：取上下左右紧贴矩形外侧一圈像素，
    /// 再去亮度中位数对应的颜色。中位数能抗轻微污染——即便边缘混入少量文字亮像素，
    /// 只要主体是背景色，取到的背景色依然准确。
    /// 越界部分回退到矩形内侧对应位置采样。
    /// </summary>
    private static Color SampleEdgeColor(Bitmap bmp, Rectangle r)
    {
        var samples = new List<Color>();

        SampleLine(r.X, r.Y - 3, r.Width, 1, 1);   // 上
        SampleLine(r.X, r.Y + r.Height + 2, r.Width, 1, 1); // 下
        SampleLine(r.X - 3, r.Y, 1, r.Height, 1);  // 左
        SampleLine(r.X + r.Width + 2, r.Y, 1, r.Height, 1); // 右

        if (samples.Count == 0)
            return Color.White;

        samples.Sort((a, b) => Luminance(a).CompareTo(Luminance(b)));
        return samples[samples.Count / 2];

        void SampleLine(int x, int y, int w, int h, int step)
        {
            for (var yy = y; yy < y + h; yy += step)
            {
                for (var xx = x; xx < x + w; xx += step)
                {
                    samples.Add(bmp.GetPixel(
                        Math.Clamp(xx, 0, bmp.Width - 1),
                        Math.Clamp(yy, 0, bmp.Height - 1)));
                }
            }
        }
    }

    /// <summary>根据背景亮度返回可读的文字颜色（黑/白）</summary>
    private static Color ContrastColor(Color bg)
    {
        var luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return luminance > 0.55 ? Color.FromArgb(0x21, 0x22, 0x29) : Color.FromArgb(0xF5, 0xF5, 0xF5);
    }

    /// <summary>
    /// 自适应字号：从矩形高度起步，逐级减小直到文本能放入矩形（含换行），
    /// 保证中文贴合原英文排版。
    /// 注意：测量高度时必须用不发限高的 SizeF(rect.Width, 0)（0 表示不限高），
    /// 否则若带 LineLimit 且传入过小的 maxHeight，MeasureString 会把高度钳制到
    /// maxHeight，误判“放得下”而返回一个实际画不下的大字号，DrawString 时被垂直裁剪。
    /// </summary>
    private static float FitFontSize(Graphics g, string text, FontFamily family, Rectangle rect)
    {
        var size = Math.Max(8f, rect.Height * 0.8f);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        while (size >= 8f)
        {
            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            // 传 rect.Width 限制宽度（保证不超宽导致溢出），高度传 0 表示不限制，取自然高度
            var m = g.MeasureString(text, font, new SizeF(rect.Width, 0), format);
            // 允许文本略超宽（最多 1.15 倍），自然高度不超过矩形
            if (m.Width <= rect.Width * 1.15f && m.Height <= rect.Height)
                return size;
            size -= 1f;
        }
        return size;
    }
}
