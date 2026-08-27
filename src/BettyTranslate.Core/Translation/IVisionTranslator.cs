using System.Drawing;

namespace BettyTranslate.Core.Translation;

/// <summary>
/// 视觉翻译器抽象：识别图中英文并翻译成中文，返回带局部坐标（相对截图）的译文区域。
/// 客户端直连与服务端代理两条通道都实现本接口，调用方无需关心底层实现。
/// </summary>
public interface IVisionTranslator
{
    /// <summary>识别并翻译图中英文，返回译文区域（坐标为相对截图的局部坐标）。未识别到英文或接口异常时抛出 <see cref="InvalidOperationException"/>。</summary>
    Task<IReadOnlyList<ImageComposer.TranslatedRegion>> TranslateAsync(Bitmap bitmap);
}
