namespace BettyTranslate.Core.Translation;

/// <summary>
/// 免费截图翻译额度已用尽（服务端 Edge Function 返回 403）。
/// 抛出本异常时客户端不得回退到其他翻译通道，避免绕过免费额度。
/// </summary>
public sealed class FreeQuotaExceededException : Exception
{
    public FreeQuotaExceededException(string message) : base(message) { }
}
