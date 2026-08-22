using System.Security.Cryptography;
using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace BettyTranslate.Core.Auth;

/// <summary>
/// 使用 Windows DPAPI 加密持久化 GoTrue 会话。
/// 会话文件存放于 %AppData%/BettyTranslate/session.bin，仅当前 Windows 用户可解密。
/// </summary>
public sealed class DpapiSessionHandler : IGotrueSessionPersistence<Session>
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BettyTranslate", "session.bin");

    public Session? LoadSession()
    {
        if (!File.Exists(SessionPath))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(SessionPath);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonConvert.DeserializeObject<Session>(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            // 解密/反序列化失败视为无会话
            return null;
        }
    }

    public void SaveSession(Session session)
    {
        try
        {
            var dir = Path.GetDirectoryName(SessionPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(session);
            var bytes = ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(SessionPath, bytes);
        }
        catch
        {
            // 保存失败不阻断登录流程
        }
    }

    public void DestroySession()
    {
        try
        {
            if (File.Exists(SessionPath))
                File.Delete(SessionPath);
        }
        catch
        {
            // 删除失败不阻断
        }
    }
}
