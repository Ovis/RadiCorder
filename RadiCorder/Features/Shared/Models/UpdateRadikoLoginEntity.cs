using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Features.Shared.Models;

/// <summary>
/// radikoログイン情報更新リクエスト。
/// </summary>
public class UpdateRadikoLoginEntity
{
    /// <summary>
    /// radikoログインユーザーID。
    /// </summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// radikoログインパスワード。
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}
