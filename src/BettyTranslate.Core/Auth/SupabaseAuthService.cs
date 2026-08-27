using System;
using System.Linq;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace BettyTranslate.Core.Auth;

/// <summary>
/// 基于 supabase-csharp 的认证服务实现。
/// 客户端懒初始化：首次调用时创建 Supabase 客户端并恢复本地会话。
/// </summary>
public sealed class SupabaseAuthService : IAuthService, IAsyncDisposable
{
    private readonly string _url;
    private readonly string _anonKey;
    private Supabase.Client? _client;

    public SupabaseAuthService(string url, string anonKey)
    {
        _url = url;
        _anonKey = anonKey;
    }

    public User? CurrentUser => _client?.Auth.CurrentUser;

    /// <summary>懒初始化客户端：创建连接并恢复本地会话</summary>
    private async Task<Supabase.Client> GetClientAsync()
    {
        if (_client != null)
            return _client;

        var sessionHandler = new DpapiSessionHandler();
        var options = new SupabaseOptions
        {
            AutoConnectRealtime = false,
            AutoRefreshToken = true,
            SessionHandler = sessionHandler,
        };
        var client = new Supabase.Client(_url, _anonKey, options);
        await client.InitializeAsync();
        // gotrue-csharp 只有调用了 SetPersistence 后，才会在登录/令牌刷新时把会话写盘，
        // 也才会在 LoadSession 时从磁盘恢复会话；仅传入 SupabaseOptions.SessionHandler 未必接线。
        // 这里显式绑定同一实例，确保 session.bin 一定会被写入，重启后即可静默自动登录。
        client.Auth.SetPersistence(sessionHandler);
        // gotrue-csharp 不会在 InitializeAsync 时自动从 SessionHandler 恢复会话，
        // 必须显式调用 LoadSession() 才能把持久化的会话加载到 CurrentSession。
        client.Auth.LoadSession();
        _client = client;
        return client;
    }

    public async Task<bool> SignInAsync(string email, string password)
    {
        try
        {
            var client = await GetClientAsync();
            await client.Auth.SignInWithPassword(email, password);
            return client.Auth.CurrentSession != null;
        }
        catch (Exception ex)
        {
            throw new AuthException("登录失败，请检查邮箱与密码", ex);
        }
    }

    public async Task SendVerificationCodeAsync(string email)
    {
        try
        {
            var client = await GetClientAsync();
            await client.Auth.SignInWithOtp(new SignInWithPasswordlessEmailOptions(email));
        }
        catch (Exception ex)
        {
            throw new AuthException("验证码发送失败，请检查邮箱后重试", ex);
        }
    }

    public async Task<bool> RegisterWithCodeAsync(string email, string password, string code)
    {
        try
        {
            var client = await GetClientAsync();
            // 校验邮箱验证码（SignInWithOtp 发送的 token，验证通过即自动创建账号并登录）
            await client.Auth.VerifyOTP(email, code.Trim(), Constants.EmailOtpType.MagicLink);
            // 为刚验证的账号设置密码
            await client.Auth.Update(new UserAttributes { Password = password });
            return client.Auth.CurrentSession != null;
        }
        catch (AuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AuthException("注册失败：验证码错误或该邮箱已注册", ex);
        }
    }

    public async Task SignOutAsync()
    {
        var client = await GetClientAsync();
        await client.Auth.SignOut();
    }

    public async Task<bool> EnsureSessionAsync()
    {
        try
        {
            var client = await GetClientAsync();
            if (client.Auth.CurrentSession == null)
                return false;

            // 恢复本地会话后主动续期一次令牌：旧的 access token 过期时刷新，
            // 避免长时间未启动后误判为「未登录」而要求重新登录。
            // 刷新失败（如开机初期网络未就绪）时并不强制判定为「未登录」：
            // 只要本地已恢复出持久化会话，即视为已登录，交由后台自动续期定时器在网络恢复后继续刷新。
            try
            {
                await client.Auth.RetrieveSessionAsync();
            }
            catch
            {
                // 刷新失败忽略：仅凭持久化会话是否存在即可判定登录状态，
                // 令牌是否真正可用由后续请求按有效性处理。
            }

            return client.Auth.CurrentSession != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> GetImageTranslateCountAsync()
    {
        try
        {
            var client = await GetClientAsync();
            var user = client.Auth.CurrentUser;
            if (user == null)
                return 0;
            var uid = ParseUserId(user.Id);
            var response = await client.From<UserUsage>()
                .Where(x => x.UserId == uid)
                .Get();
            return response.Models.FirstOrDefault()?.ImageTranslateCount ?? 0;
        }
        catch
        {
            // 查询失败按 0 处理，不阻断免费体验
            return 0;
        }
    }

    /// <summary>当前用户是否为图片翻译特权用户：user_usage.is_unlimited 为 true 则无限使用（由管理后台维护）</summary>
    public async Task<bool> IsImageTranslateUnlimitedAsync()
    {
        try
        {
            var client = await GetClientAsync();
            var user = client.Auth.CurrentUser;
            if (user == null)
                return false;
            var uid = ParseUserId(user.Id);
            var response = await client.From<UserUsage>()
                .Where(x => x.UserId == uid)
                .Get();
            return response.Models.FirstOrDefault()?.IsUnlimited ?? false;
        }
        catch
        {
            // 查询失败按非特权处理，保守回归正常额度限制
            return false;
        }
    }

    public async Task IncrementImageTranslateCountAsync()
    {
        try
        {
            var client = await GetClientAsync();
            var user = client.Auth.CurrentUser;
            if (user == null)
                return;
            var uid = ParseUserId(user.Id);
            var current = await GetImageTranslateCountAsync();
            await client.From<UserUsage>().Upsert(new UserUsage
            {
                UserId = uid,
                ImageTranslateCount = current + 1,
            });
        }
        catch
        {
            // 计数失败仅影响免费额度，不阻断本次翻译
        }
    }

    /// <summary>把 Supabase 用户 ID（uuid 字符串）解析为 Guid，供 user_usage 表使用；传入 null 或非 uuid 时返回 Guid.Empty</summary>
    private static Guid ParseUserId(string? id) =>
        Guid.TryParse(id, out var g) ? g : Guid.Empty;

    public async Task<string?> GetAccessTokenAsync()
    {
        try
        {
            var client = await GetClientAsync();
            return client.Auth.CurrentSession?.AccessToken;
        }
        catch
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _client = null;
        return ValueTask.CompletedTask;
    }
}
