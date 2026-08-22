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

        var options = new SupabaseOptions
        {
            AutoConnectRealtime = false,
            AutoRefreshToken = true,
            SessionHandler = new DpapiSessionHandler(),
        };
        var client = new Supabase.Client(_url, _anonKey, options);
        await client.InitializeAsync();
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
            var session = client.Auth.CurrentSession;
            return session != null && !session.Expired();
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _client = null;
        return ValueTask.CompletedTask;
    }
}
