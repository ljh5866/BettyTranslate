namespace BettyTranslate.Core.Translation;

/// <summary>
/// 翻译服务抽象：不同引擎（百度/其他）可替换
/// </summary>
public interface ITranslateProvider
{
    /// <summary>将单段文本翻译为目标语言（如 zh），返回译文</summary>
    Task<string> TranslateAsync(string text, string toLanguage);
}
