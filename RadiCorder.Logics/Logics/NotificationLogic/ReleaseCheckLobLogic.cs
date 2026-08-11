using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.NotificationLogic;

/// <summary>
/// GitHub Releases を定期確認し、新しいリリースを通知する。
/// </summary>
public class ReleaseCheckLobLogic(
    ILogger<ReleaseCheckLobLogic> logger,
    IAppConfigurationService appConfigurationService,
    NotificationLobLogic notificationLobLogic,
    IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private HttpClient HttpClient => httpClientFactory.CreateClient(HttpClientNames.GitHub);

    public async ValueTask CheckForNewReleaseAsync()
    {
        var intervalDays = appConfigurationService.ReleaseCheckIntervalDays;
        if (intervalDays <= 0)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var lastCheckedAtUtc = await appConfigurationService.GetReleaseCheckLastCheckedAtAsync();
        if (lastCheckedAtUtc.HasValue &&
            nowUtc < lastCheckedAtUtc.Value.AddDays(intervalDays))
        {
            return;
        }

        try
        {
            var (owner, repository) = ResolveTargetRepository();
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            {
                logger.ZLogWarning($"リリース確認対象のGitHub owner/repositoryが未設定のため、チェックをスキップします。");
                return;
            }

            var latestRelease = await GetLatestReleaseAsync(owner, repository);
            if (latestRelease == null || string.IsNullOrWhiteSpace(latestRelease.TagName))
            {
                await appConfigurationService.UpdateReleaseCheckLastCheckedAtAsync(nowUtc);
                return;
            }

            var latestVersionRaw = NormalizeVersionText(latestRelease.TagName);
            var currentVersionRaw = NormalizeVersionText(GetCurrentVersion());

            if (!TryParseSemVer(latestVersionRaw, out var latestVersion) ||
                !TryParseSemVer(currentVersionRaw, out var currentVersion))
            {
                logger.ZLogWarning(
                    $"リリースバージョン比較をスキップします。latest={latestVersionRaw}, current={currentVersionRaw}");
                await appConfigurationService.UpdateReleaseCheckLastCheckedAtAsync(nowUtc);
                return;
            }

            if (CompareSemVer(latestVersion, currentVersion) <= 0)
            {
                await appConfigurationService.UpdateReleaseCheckLastCheckedAtAsync(nowUtc);
                return;
            }

            var notifiedVersion = await appConfigurationService.GetReleaseLastNotifiedVersionAsync();
            if (string.Equals(NormalizeVersionText(notifiedVersion), latestVersionRaw, StringComparison.OrdinalIgnoreCase))
            {
                await appConfigurationService.UpdateReleaseCheckLastCheckedAtAsync(nowUtc);
                return;
            }

            var publishedAtText = latestRelease.PublishedAt.HasValue
                ? latestRelease.PublishedAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz")
                : "不明";
            var releaseUrl = string.IsNullOrWhiteSpace(latestRelease.HtmlUrl)
                ? $"https://github.com/{owner}/{repository}/releases"
                : latestRelease.HtmlUrl;

            await notificationLobLogic.SetNotificationAsync(
                logLevel: LogLevel.Information,
                category: NoticeCategory.NewRelease,
                message:
                $"新しいリリース {latestVersionRaw} が公開されています（公開日: {publishedAtText}）。リリースノート: {releaseUrl}");

            await appConfigurationService.UpdateReleaseLastNotifiedVersionAsync(latestVersionRaw);
            await appConfigurationService.UpdateReleaseCheckLastCheckedAtAsync(nowUtc);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning($"新しいリリースの確認に失敗しました。{ex}");
        }
    }

    private async ValueTask<GitHubReleaseEntity?> GetLatestReleaseAsync(string owner, string repository)
    {
        var url = $"https://api.github.com/repos/{owner}/{repository}/releases/latest";

        using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
            logger,
            HttpClient,
            "GitHub最新リリース確認",
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                return request;
            },
            appConfigurationService.ExternalServiceUserAgent);

        if (!response.IsSuccessStatusCode)
        {
            logger.ZLogWarning($"GitHub最新リリース確認に失敗しました。status={response.StatusCode}");
            return null;
        }

        var raw = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubReleaseEntity>(raw, JsonOptions);
    }

    private (string Owner, string Repository) ResolveTargetRepository()
    {
        var owner = appConfigurationService.ReleaseCheckGitHubOwner;
        var repository = appConfigurationService.ReleaseCheckGitHubRepository;
        return (owner?.Trim() ?? string.Empty, repository?.Trim() ?? string.Empty);
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string NormalizeVersionText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value[1..];
        }

        var plusIndex = value.IndexOf('+');
        if (plusIndex >= 0)
        {
            value = value[..plusIndex];
        }

        return value;
    }

    private static bool TryParseSemVer(string raw, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split('-', 2, StringSplitOptions.TrimEntries);
        var coreParts = parts[0].Split('.', StringSplitOptions.TrimEntries);
        if (coreParts.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(coreParts[0], out var major) ||
            !int.TryParse(coreParts[1], out var minor) ||
            !int.TryParse(coreParts[2], out var patch))
        {
            return false;
        }

        var prerelease = parts.Length == 2
            ? parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        version = new SemVer(major, minor, patch, prerelease);
        return true;
    }

    private static int CompareSemVer(SemVer left, SemVer right)
    {
        var byMajor = left.Major.CompareTo(right.Major);
        if (byMajor != 0) return byMajor;

        var byMinor = left.Minor.CompareTo(right.Minor);
        if (byMinor != 0) return byMinor;

        var byPatch = left.Patch.CompareTo(right.Patch);
        if (byPatch != 0) return byPatch;

        if (left.PreRelease.Length == 0 && right.PreRelease.Length == 0) return 0;
        if (left.PreRelease.Length == 0) return 1;
        if (right.PreRelease.Length == 0) return -1;

        var max = Math.Max(left.PreRelease.Length, right.PreRelease.Length);
        for (var i = 0; i < max; i++)
        {
            if (i >= left.PreRelease.Length) return -1;
            if (i >= right.PreRelease.Length) return 1;

            var leftId = left.PreRelease[i];
            var rightId = right.PreRelease[i];

            var leftIsNumeric = int.TryParse(leftId, out var leftNumber);
            var rightIsNumeric = int.TryParse(rightId, out var rightNumber);

            if (leftIsNumeric && rightIsNumeric)
            {
                var compare = leftNumber.CompareTo(rightNumber);
                if (compare != 0) return compare;
                continue;
            }

            if (leftIsNumeric && !rightIsNumeric) return -1;
            if (!leftIsNumeric && rightIsNumeric) return 1;

            var textCompare = string.Compare(leftId, rightId, StringComparison.Ordinal);
            if (textCompare != 0) return textCompare;
        }

        return 0;
    }

    private readonly record struct SemVer(
        int Major,
        int Minor,
        int Patch,
        string[] PreRelease);

    private sealed class GitHubReleaseEntity
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
