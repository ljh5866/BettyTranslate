using Supabase.Gotrue;

namespace BettyTranslate.Core.Auth;

/// <summary>
/// 认证服务抽象：登录/注册/退出/会话恢复
/// </summary>
public interface IAuthService
{
    /// <summary>当前登录用户（未登录为 null）</summary>
    User? CurrentUser { get; }

    /// <summary>邮箱密码登录，成功返回 true</summary>
    Task<bool> SignInAsync(string email, string password);

    /// <summary>发送注册邮箱验证码（6 位数字，发到邮箱）</summary>
    Task SendVerificationCodeAsync(string email);

    /// <summary>验证码校验 + 邮箱密码注册，成功返回 true</summary>
    Task<bool> RegisterWithCodeAsync(string email, string password, string code);

    /// <summary>退出登录并清除本地会话</summary>
    Task SignOutAsync();

    /// <summary>启动时恢复本地会话；存在有效会话返回 true</summary>
    Task<bool> EnsureSessionAsync();

    /// <summary>当前用户已使用的图片翻译次数（免费额度计数，按账号记录）</summary>
    Task<int> GetImageTranslateCountAsync();

    /// <summary>当前用户是否为图片翻译特权用户（user_usage.is_unlimited 为 true，可无限使用，由管理后台维护）</summary>
    Task<bool> IsImageTranslateUnlimitedAsync();

    /// <summary>把当前用户的图片翻译次数累加 1（免费体验计数）</summary>
    Task IncrementImageTranslateCountAsync();

    /// <summary>当前登录用户的访问令牌（JWT），用于调用需要登录态的 Edge Function；未登录返回 null</summary>
    Task<string?> GetAccessTokenAsync();
}
