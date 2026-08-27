using System;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BettyTranslate.Core.Auth;

/// <summary>
/// 图片翻译免费额度记录：对应 Supabase 的 user_usage 表，
/// 按账号记录已使用的截图翻译次数（前 15 次免费，用尽后需用户自备 Key）。
/// </summary>
[Table("user_usage")]
public sealed class UserUsage : BaseModel
{
    /// <summary>用户 ID（auth.users 的 uuid）</summary>
    [PrimaryKey("user_id")]
    public Guid UserId { get; set; }

    /// <summary>已使用的图片翻译次数</summary>
    [Column("image_translate_count")]
    public int ImageTranslateCount { get; set; }

    /// <summary>是否为无限图片翻译特权账号（由管理后台在 user_usage 表维护，普通用户不可修改）</summary>
    [Column("is_unlimited")]
    public bool IsUnlimited { get; set; }
}
