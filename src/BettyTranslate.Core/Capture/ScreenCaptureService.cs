using System.Drawing;

namespace BettyTranslate.Core.Capture;

/// <summary>
/// 屏幕截图服务：捕获虚拟桌面任意区域（物理像素坐标）
/// </summary>
public interface ICaptureService
{
    /// <summary>捕获整个虚拟屏幕（覆盖所有显示器），返回位图</summary>
    Bitmap CaptureFullScreen();

    /// <summary>捕获指定屏幕区域，返回位图</summary>
    Bitmap CaptureRegion(Rectangle bounds);
}

/// <summary>
/// 基于 GDI（Graphics.CopyFromScreen）的截图实现。
/// 依赖进程 Per-Monitor DPI 感知（app.manifest），否则高 DPI 下坐标会偏移。
/// </summary>
public sealed class ScreenCaptureService : ICaptureService
{
    // 虚拟屏幕边界（GetSystemMetrics）
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public Bitmap CaptureFullScreen()
    {
        var b = GetVirtualScreenBounds();
        return CaptureRegion(b);
    }

    public Bitmap CaptureRegion(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size);
        return bmp;
    }

    /// <summary>虚拟屏幕边界（物理像素；多显示器场景 left/top 可能为负）</summary>
    public static Rectangle GetVirtualScreenBounds()
    {
        return new Rectangle(
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }
}
