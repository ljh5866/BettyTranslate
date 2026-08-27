namespace BettyTranslate.Core.Translation;

/// <summary>
/// 组合翻译器：首选通道失败时自动回退到备用通道，保证翻译可用性。
/// </summary>
public sealed class FailingOverTranslateProvider : ITranslateProvider
{
    private readonly ITranslateProvider _primary;
    private readonly ITranslateProvider _fallback;

    public FailingOverTranslateProvider(ITranslateProvider primary, ITranslateProvider fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public async Task<string> TranslateAsync(string text, string toLanguage)
    {
        try
        {
            return await _primary.TranslateAsync(text, toLanguage);
        }
        catch
        {
            return await _fallback.TranslateAsync(text, toLanguage);
        }
    }
}
