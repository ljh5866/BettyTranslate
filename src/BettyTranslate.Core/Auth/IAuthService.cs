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

    /// <summary>邮箱密码注册，成功返回 true</summary>
    Task<bool> SignUpAsync(string email, string password);

    /// <summary>退出登录并清除本地会话</summary>
    Task SignOutAsync();

    /// <summary>启动时恢复本地会话；存在有效会话返回 true</summary>
    Task<bool> EnsureSessionAsync();
}
