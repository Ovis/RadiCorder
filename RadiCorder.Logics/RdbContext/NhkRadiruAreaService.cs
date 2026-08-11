using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext;

/// <summary>
/// らじる★らじるのエリア別サービス情報
/// </summary>
public class NhkRadiruAreaService
{
    /// <summary>
    /// ID
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// エリアID
    /// </summary>
    [MaxLength(10)]
    public string AreaId { get; set; } = string.Empty;

    /// <summary>
    /// サービスID
    /// </summary>
    [MaxLength(32)]
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    /// サービス名
    /// </summary>
    [MaxLength(64)]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// HLS URL
    /// </summary>
    [MaxLength(500)]
    public string HlsUrl { get; set; } = string.Empty;

    /// <summary>
    /// 有効フラグ
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 取得元タグ(r1hlsなど)
    /// </summary>
    [MaxLength(32)]
    public string SourceTag { get; set; } = string.Empty;

    /// <summary>
    /// 最終同期時刻(UTC)
    /// </summary>
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
}

