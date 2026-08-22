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

    public async Task<bool> SignUpAsync(string email, string password)
    {
        try
        {
            var client = await GetClientAsync();
            var session = await client.Auth.SignUp(email, password);
            // 若开启了邮箱确认，注册后无有效会话，需提示用户前往邮箱确认
            return session?.User != null || client.Auth.CurrentSession != null;
        }
        catch (Exception ex)
        {
            throw new AuthException("注册失败，请检查邮箱格式或是否已注册", ex);
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
