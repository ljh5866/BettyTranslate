using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BettyTranslate.Core.Settings;

namespace BettyTranslate.Core.Update;

/// <summary>
/// 检查更新结果：最新版本及需下载的安装包资源信息。
/// </summary>
public sealed record UpdateInfo(
    Version LatestVersion,
    string TagName,
    string ReleaseName,
    string HtmlUrl,
    string AssetName,
    string DownloadUrl,
    long AssetSize);

/// <summary>
/// 检查更新服务：通过 GitHub Releases 获取最新版本并自动下载安装包。
/// 公开仓库无需 Token；私有仓库可在 <see cref="UpdateSettings.Token"/> 配置 Personal Access Token。
/// 不引入额外依赖，仅用内置 HttpClient + System.Text.Json。
/// </summary>
public sealed class UpdateService
{
    private const string ApiBase = "https://api.github.com/repos";

    private readonly HttpClient _http;
    private readonly UpdateSettings _settings;

    public UpdateService(UpdateSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BettyTranslate");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(settings.Token))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Token);
    }

    /// <summary>
    /// 检查是否有比 <paramref name="current"/> 更新的版本。
    /// 无更新或仓库尚未发布 release 时返回 null。
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(Version current, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.RepoOwner) ||
            string.IsNullOrWhiteSpace(_settings.RepoName))
            return null; // 尚未配置仓库

        var url = $"{ApiBase}/{_settings.RepoOwner}/{_settings.RepoName}/releases/latest";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // 尚无任何 release
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize<GithubRelease>(json);
        if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            return null;

        var latest = ParseTagVersion(release.TagName);
        if (latest == null || latest <= current)
            return null; // 已是最新

        var asset = PickAsset(release.Assets);
        if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
            return null; // 没有可下载的安装包资源

        return new UpdateInfo(
            latest,
            release.TagName,
            release.Name ?? release.TagName,
            release.HtmlUrl ?? string.Empty,
            asset.Name ?? string.Empty,
            asset.DownloadUrl,
            asset.Size);
    }

    /// <summary>
    /// 下载安装包到目标路径，并回报下载进度（0~1）。
    /// </summary>
    public async Task<long> DownloadAsync(string url, string destPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(destPath);

        var buffer = new byte[128 * 1024];
        long written = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;
            if (total > 0)
                progress?.Report((double)written / total);
        }
        return total;
    }

    /// <summary>根据配置的资源名模式挑选安装包；模式为空时优先 exe / msi / zip，否则取第一个。</summary>
    private GithubAsset? PickAsset(IReadOnlyList<GithubAsset>? assets)
    {
        if (assets == null || assets.Count == 0)
            return null;

        // 按配置的子串匹配（忽略大小写）
        if (!string.IsNullOrWhiteSpace(_settings.AssetPattern))
        {
            var pat = _settings.AssetPattern;
            var matched = assets.FirstOrDefault(a =>
                a.Name?.Contains(pat, StringComparison.OrdinalIgnoreCase) == true);
            if (matched != null)
                return matched;
        }

        // 默认优先级：exe / msi / zip
        foreach (var ext in new[] { ".exe", ".msi", ".zip" })
        {
            var found = assets.FirstOrDefault(a =>
                a.Name?.EndsWith(ext, StringComparison.OrdinalIgnoreCase) == true);
            if (found != null)
                return found;
        }

        return assets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.DownloadUrl));
    }

    /// <summary>把 tag（如 v0.5.2 / 0.5.2 或 0.5.2-beta）解析成 Version；无法解析返回 null。</summary>
    private static Version? ParseTagVersion(string tag)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        var m = Regex.Match(s, @"^\d+(?:\.\d+){1,3}");
        if (!m.Success)
            return null;
        var parts = m.Value.Split('.').Select(int.Parse).ToArray();
        return parts.Length switch
        {
            2 => new Version(parts[0], parts[1]),
            3 => new Version(parts[0], parts[1], parts[2]),
            _ => new Version(parts[0], parts[1], parts[2], parts[3]),
        };
    }

    // —— GitHub API 响应数据结构（只解析用到的字段）——
    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GithubAsset>? Assets { get; set; }
    }

    private sealed class GithubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
