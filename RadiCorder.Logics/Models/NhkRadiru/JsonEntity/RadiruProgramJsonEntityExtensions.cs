using System.Text;
using RadiCorder.Logics.Extensions;

namespace RadiCorder.Logics.Models.NhkRadiru.JsonEntity;

/// <summary>
/// RadiruProgramJsonEntity クラスの拡張メソッド
/// </summary>
public static class RadiruProgramJsonEntityExtensions
{
    /// <summary>
    /// 番組名取得
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public static string GetTitle(this RadiruProgramJsonEntity entity)
    {
        return !string.IsNullOrEmpty(entity.IdentifierGroup.RadioSeriesName)
            ? entity.IdentifierGroup.RadioSeriesName.ToSafeName().To半角英数字()
            : entity.Name.ToSafeName().To半角英数字();
    }


    /// <summary>
    /// 詳細情報取得
    /// </summary>
    /// <param name="entity">ラジオ番組のエントリ</param>
    /// <param name="separator">区切り文字（デフォルトは改行）</param>
    /// <returns>連結された説明文</returns>
    public static string GetCombinedDescription(this RadiruProgramJsonEntity entity, string separator = "\n")
    {
        var sb = new StringBuilder();

        // メイン Description を追加
        if (!string.IsNullOrWhiteSpace(entity.Description))
        {
            sb.AppendLine(entity.Description);
        }

        // PartOfSeries の Description を追加（メイン Description が存在する場合は区切り文字を追加）
        if (!string.IsNullOrWhiteSpace(entity.About?.PartOfSeries?.Description))
        {
            if (sb.Length > 0)
            {
                sb.AppendLine(separator);
            }
            sb.AppendLine(entity.About.PartOfSeries.Description);
        }

        // DetailedDescription の値を追加
        // Epg40 を追加
        if (!string.IsNullOrWhiteSpace(entity.DetailedDescription.Epg40))
        {
            if (sb.Length > 0)
            {
                sb.AppendLine(separator);
            }
            sb.AppendLine(entity.DetailedDescription.Epg40);
        }

        // Epg200 を追加
        var epg200 = entity.DetailedDescription.Epg200;
        if (!string.IsNullOrWhiteSpace(epg200))
        {
            if (sb.Length > 0)
            {
                sb.AppendLine(separator);
            }
            sb.AppendLine(epg200);
        }

        // Epg80 を追加（Epg200に含まれていない場合のみ）
        var epg80 = entity.DetailedDescription.Epg80;
        if (!string.IsNullOrWhiteSpace(epg80) && (string.IsNullOrWhiteSpace(epg200) || !epg200.Contains(epg80)))
        {
            if (sb.Length > 0)
            {
                sb.Append(separator);
            }
            sb.AppendLine(epg80);
        }

        // PartOfSeries の Keyword を追加
        if (entity.About?.PartOfSeries?.Keyword != null && entity.About.PartOfSeries.Keyword.Length > 0)
        {
            sb.AppendLine(separator);

            string keywordText = string.Join("、", entity.About.PartOfSeries.Keyword);
            if (!string.IsNullOrWhiteSpace(keywordText))
            {
                if (sb.Length > 0)
                {
                    sb.Append(separator);
                }
                sb.Append($"キーワード：{keywordText}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 出演者、演奏者情報
    /// </summary>
    /// <param name="entity">ラジオ番組のエントリ</param>
    /// <returns>連結された出演者・アーティスト名</returns>
    public static string GetCombinedActorsAndArtists(this RadiruProgramJsonEntity entity)
    {
        var sb = new StringBuilder();

        if (entity.Misc.ActList is { Length: > 0 })
        {
            foreach (var act in entity.Misc.ActList)
            {
                if (!string.IsNullOrWhiteSpace(act.Name))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(act.Name);
                }
            }
        }

        if (entity.Misc.MusicList is { Length: > 0 })
        {
            foreach (var music in entity.Misc.MusicList)
            {
                if (music.ByArtist is { Length: > 0 })
                {
                    foreach (var artist in music.ByArtist)
                    {
                        if (!string.IsNullOrWhiteSpace(artist.Name))
                        {
                            if (sb.Length > 0)
                            {
                                sb.Append(' ');
                            }
                            sb.Append(artist.Name);
                        }
                    }
                }
            }
        }

        return sb.ToString();
    }
}