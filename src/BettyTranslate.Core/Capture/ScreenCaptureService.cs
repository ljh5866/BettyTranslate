using System.Drawing;

namespace BettyTranslate.Core.Capture;

/// <summary>
/// 屏幕截图服务：捕获多显示器虚拟桌面（全屏）
/// </summary>
public interface ICaptureService
{
    /// <summary>捕获整个虚拟屏幕（覆盖所有显示器），返回位图</summary>
    Bitmap CaptureFullScreen();
}

/// <summary>
/// 基于 GDI（Graphics.CopyFromScreen）的截图实现。
/// 使用虚拟屏幕坐标，保证多显示器场景下覆盖全部桌面。
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
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(left, top, 0, 0, bmp.Size);
        return bmp;
    }
}
