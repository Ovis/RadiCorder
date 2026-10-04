using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Providers.Radiru;

/// <summary>
/// らじる番組の必須項目を検証し、保存用データへ正規化する。
/// </summary>
public static class RadiruProgramNormalizer
{
    public static bool TryNormalize(
        string areaId,
        string serviceId,
        RadiruProgramJsonEntity programJsonEntity,
        out NhkRadiruProgram entry)
    {
        var hasRequiredFieldError = false;
        var title = programJsonEntity.GetTitle();

        if (string.IsNullOrWhiteSpace(programJsonEntity.Id))
        {
            hasRequiredFieldError = true;
        }

        if (programJsonEntity.StartDate == default)
        {
            hasRequiredFieldError = true;
        }

        if (programJsonEntity.EndDate == default)
        {
            hasRequiredFieldError = true;
        }

        if (programJsonEntity.StartDate != default &&
            programJsonEntity.EndDate != default &&
            programJsonEntity.EndDate <= programJsonEntity.StartDate)
        {
            hasRequiredFieldError = true;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            hasRequiredFieldError = true;
        }

        if (hasRequiredFieldError)
        {
            entry = new NhkRadiruProgram();
            return false;
        }

        var onDemandContentUrl = SelectOnDemandContentUrl(programJsonEntity.About.Audio);
        var onDemandExpiresAtUtc = programJsonEntity.About.Audio.Expires == default
            ? (DateTime?)null
            : programJsonEntity.About.Audio.Expires.UtcDateTime;

        entry = new NhkRadiruProgram
        {
            ProgramId = $"{programJsonEntity.Id}",
            StationId = serviceId,
            AreaId = areaId,
            RadioDate = programJsonEntity.StartDate.ToRadioDate(),
            DaysOfWeek = programJsonEntity.StartDate.ToRadioDayOfWeek().ToDaysOfWeek(),
            EventId = programJsonEntity.About.Id,
            StartTime = programJsonEntity.StartDate,
            EndTime = programJsonEntity.EndDate,
            Title = title,
            Subtitle = programJsonEntity.IdentifierGroup.RadioEpisodeName.ToSafeName().To半角英数字(),
            Description = programJsonEntity.GetCombinedDescription(),
            Performer = programJsonEntity.GetCombinedActorsAndArtists(),
            SiteId = programJsonEntity.IdentifierGroup.SiteId,
            ImageUrl = programJsonEntity.About.PartOfSeries.Logo.Medium.Url,
            ProgramUrl = programJsonEntity.About.Url,
            OnDemandContentUrl = onDemandContentUrl,
            OnDemandExpiresAtUtc = onDemandExpiresAtUtc
        };

        return true;
    }

    private static string? SelectOnDemandContentUrl(Audio audio)
    {
        var detailedContents = audio.DetailedContent
            .Where(d => !string.IsNullOrWhiteSpace(d.ContentUrl))
            .ToList();

        if (detailedContents.Count == 0)
        {
            return null;
        }

        var prioritized = detailedContents.FirstOrDefault(d =>
            string.Equals(d.Name, "hls_widevine", StringComparison.OrdinalIgnoreCase) &&
            IsM3u8Url(d.ContentUrl));
        if (prioritized is not null)
        {
            return prioritized.ContentUrl;
        }

        var fallback = detailedContents.FirstOrDefault(d => IsM3u8Url(d.ContentUrl));
        return fallback?.ContentUrl;
    }

    private static bool IsM3u8Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }


}
