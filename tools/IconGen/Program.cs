using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// 生成 App 图标：
// 1) 若 Assets\icon.png 存在，则以其为源图缩放为 256x256
// 2) 否则绘制默认样式（蓝色圆角方块 + 白色 B）
// 输出：Assets\AppIcon.ico（真 ICO，PNG 压缩条目）
var assetsDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "BettyTranslate.App", "Assets"));

byte[] png;
var srcPng = Path.Combine(assetsDir, "icon.png");
if (File.Exists(srcPng))
{
    using var src = new Bitmap(srcPng);
    using var bmp = new Bitmap(256, 256);
    using var g = Graphics.FromImage(bmp);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);
    g.DrawImage(src, 0, 0, 256, 256);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    png = ms.ToArray();
    Console.WriteLine($"使用源图: {srcPng} ({src.Width}x{src.Height})");
}
else
{
    using var bmp = new Bitmap(256, 256);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    using var path = new GraphicsPath();
    var rect = new Rectangle(10, 10, 236, 236);
    const int d = 112; // 圆角直径
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    using var brush = new SolidBrush(Color.FromArgb(255, 15, 108, 189));
    g.FillPath(brush, path);

    using var font = new Font("Segoe UI", 150, FontStyle.Bold, GraphicsUnit.Pixel);
    using var sf = new StringFormat
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };
    g.DrawString("B", font, Brushes.White, rect, sf);

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    png = ms.ToArray();
    Console.WriteLine("无源图，使用默认样式");
}

var outPath = Path.Combine(assetsDir, "AppIcon.ico");
using var fs = File.Create(outPath);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0);    // reserved
bw.Write((ushort)1);    // type: icon
bw.Write((ushort)1);    // count
bw.Write((byte)0);      // width  (0 = 256)
bw.Write((byte)0);      // height (0 = 256)
bw.Write((byte)0);      // colors
bw.Write((byte)0);      // reserved
bw.Write((ushort)1);    // planes
bw.Write((ushort)32);   // bitcount
bw.Write((uint)png.Length);
bw.Write((uint)22);     // offset: 6 + 16
bw.Write(png);

Console.WriteLine($"OK: {outPath} ({png.Length} bytes)");
