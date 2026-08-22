namespace BettyTranslate.Core.Auth;

/// <summary>
/// 认证相关异常（登录失败、注册失败、会话无效等）
/// </summary>
public sealed class AuthException : Exception
{
    public AuthException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
