using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadiCorder.Logics.Models.NhkRadiru.JsonEntity;

public class RadiruProgramJsonEntity
{

    /// <summary>
    /// JSON文字列からRadiruProgramJsonEntityのリストを生成
    /// </summary>
    public static List<RadiruProgramJsonEntity> FromJson(string json, Action<(Exception ex, string raw)>? onError = null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };


        using var doc = JsonDocument.Parse(json, new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var result = new List<RadiruProgramJsonEntity>();

        foreach (var rootValue in doc.RootElement.EnumerateObject()
                     .Select(rootProp => rootProp.Value)
                     .Where(rootValue => rootValue.ValueKind == JsonValueKind.Object))
        {
            if (!rootValue.TryGetProperty("publication", out var publication) ||
                publication.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in publication.EnumerateArray())
            {
                try
                {
                    var entry = DeserializeProgram(item, options, onError);
                    if (entry != null)
                    {
                        result.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke((ex, item.GetRawText()));
                }
            }
        }

        return result;
    }

    private static RadiruProgramJsonEntity? DeserializeProgram(
        JsonElement item,
        JsonSerializerOptions options,
        Action<(Exception ex, string raw)>? onError)
    {
        try
        {
            return item.Deserialize<RadiruProgramJsonEntity>(options);
        }
        catch (Exception ex)
        {
            onError?.Invoke((ex, item.GetRawText()));
            return DeserializeProgramLenient(item);
        }
    }

    private static RadiruProgramJsonEntity DeserializeProgramLenient(JsonElement item)
    {
        var entity = new RadiruProgramJsonEntity
        {
            Id = GetString(item, "id"),
            Name = GetString(item, "name"),
            Description = GetString(item, "description"),
            Url = GetString(item, "url")
        };

        if (TryGetProperty(item, "startDate", out var startDateElement) &&
            DateTimeOffset.TryParse(startDateElement.ToString(), out var startDate))
        {
            entity.StartDate = startDate;
        }

        if (TryGetProperty(item, "endDate", out var endDateElement) &&
            DateTimeOffset.TryParse(endDateElement.ToString(), out var endDate))
        {
            entity.EndDate = endDate;
        }

        if (TryGetProperty(item, "identifierGroup", out var identifierGroupElement) &&
            identifierGroupElement.ValueKind == JsonValueKind.Object)
        {
            entity.IdentifierGroup = new IdentifierGroup
            {
                BroadcastEventId = GetString(identifierGroupElement, "broadcastEventId"),
                ServiceId = GetString(identifierGroupElement, "serviceId"),
                AreaId = GetString(identifierGroupElement, "areaId"),
                StationId = GetString(identifierGroupElement, "stationId"),
                EventId = GetString(identifierGroupElement, "eventId"),
                RadioSeriesName = GetString(identifierGroupElement, "radioSeriesName"),
                RadioEpisodeName = GetString(identifierGroupElement, "radioEpisodeName"),
                RadioEpisodeId = GetString(identifierGroupElement, "radioEpisodeId"),
                SiteId = GetString(identifierGroupElement, "siteId")
            };
        }

        if (TryGetProperty(item, "misc", out var miscElement) && miscElement.ValueKind == JsonValueKind.Object)
        {
            var acts = ParseArrayOrSingle(miscElement, "actList")
                .Select(x => new ActList
                {
                    Name = GetString(x, "name"),
                    Title = GetString(x, "title")
                })
                .ToArray();

            var musics = ParseArrayOrSingle(miscElement, "musicList")
                .Select(x => new MusicList
                {
                    Name = GetString(x, "name"),
                    ByArtist = ParseArrayOrSingle(x, "byArtist")
                        .Select(artist => new ByArtist { Name = GetString(artist, "name") })
                        .ToArray()
                })
                .ToArray();

            entity.Misc = new Misc
            {
                ActList = acts,
                MusicList = musics
            };
        }

        if (TryGetProperty(item, "detailedDescription", out var detailedDescriptionElement) &&
            detailedDescriptionElement.ValueKind == JsonValueKind.Object)
        {
            entity.DetailedDescription = new DetailedDescription
            {
                Epg40 = GetString(detailedDescriptionElement, "epg40"),
                Epg80 = GetString(detailedDescriptionElement, "epg80"),
                Epg200 = GetString(detailedDescriptionElement, "epg200")
            };
        }

        if (TryGetProperty(item, "about", out var aboutElement) && aboutElement.ValueKind == JsonValueKind.Object)
        {
            var about = new About
            {
                Id = GetString(aboutElement, "id"),
                Name = GetString(aboutElement, "name"),
                Description = GetString(aboutElement, "description"),
                Url = GetString(aboutElement, "url"),
                Canonical = GetString(aboutElement, "canonical"),
                Keyword = ParseStringArray(aboutElement, "keyword")
            };

            if (TryGetProperty(aboutElement, "partOfSeries", out var partOfSeriesElement) &&
                partOfSeriesElement.ValueKind == JsonValueKind.Object)
            {
                var partOfSeries = new PartOfSeries
                {
                    Name = GetString(partOfSeriesElement, "name"),
                    Description = GetString(partOfSeriesElement, "description"),
                    Keyword = ParseStringArray(partOfSeriesElement, "keyword")
                };

                if (TryGetProperty(partOfSeriesElement, "logo", out var logoElement) && logoElement.ValueKind == JsonValueKind.Object &&
                    TryGetProperty(logoElement, "medium", out var mediumElement) && mediumElement.ValueKind == JsonValueKind.Object)
                {
                    partOfSeries.Logo = new Logo
                    {
                        Medium = new Medium
                        {
                            Url = GetString(mediumElement, "url"),
                            Width = GetInt32(mediumElement, "width"),
                            Height = GetInt32(mediumElement, "height")
                        }
                    };
                }

                about.PartOfSeries = partOfSeries;
            }

            if (TryGetProperty(aboutElement, "audio", out var audioElement))
            {
                var audioSource = audioElement.ValueKind == JsonValueKind.Array
                    ? audioElement.EnumerateArray().FirstOrDefault()
                    : audioElement;

                if (audioSource.ValueKind == JsonValueKind.Object)
                {
                    var audio = new Audio
                    {
                        Id = GetString(audioSource, "id"),
                        Name = GetString(audioSource, "name"),
                        Description = GetString(audioSource, "description"),
                        DetailedContent = ParseArrayOrSingle(audioSource, "detailedContent")
                            .Select(content => new DetailedContent
                            {
                                Name = GetString(content, "name"),
                                ContentUrl = GetString(content, "contentUrl")
                            })
                            .ToArray()
                    };

                    if (TryGetProperty(audioSource, "expires", out var expiresElement) &&
                        DateTimeOffset.TryParse(expiresElement.ToString(), out var expires))
                    {
                        audio.Expires = expires;
                    }

                    about.Audio = audio;
                }
            }

            entity.About = about;
        }

        return entity;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue))
        {
            return intValue;
        }

        return 0;
    }

    private static string[] ParseStringArray(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }

        return [];
    }

    private static IEnumerable<JsonElement> ParseArrayOrSingle(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().ToArray();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return [value];
        }

        return [];
    }

    /// <summary>
    /// 放送ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 番組名
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 番組詳細
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 開始日時
    /// </summary>
    [JsonPropertyName("startDate")]
    public DateTimeOffset StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateTimeOffset EndDate { get; set; }

    /// <summary>
    /// 識別情報
    /// </summary>
    [JsonPropertyName("identifierGroup")]
    public IdentifierGroup IdentifierGroup { get; set; } = new();

    /// <summary>
    /// その他情報
    /// </summary>
    [JsonPropertyName("misc")]
    public Misc Misc { get; set; } = new();

    /// <summary>
    /// 番組詳細JSON URL
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    [JsonPropertyName("about")]
    public About About { get; set; } = new();

    /// <summary>
    /// EPG詳細説明
    /// </summary>
    [JsonPropertyName("detailedDescription")]
    public DetailedDescription DetailedDescription { get; set; } = new();
}

public class IdentifierGroup
{
    [JsonPropertyName("broadcastEventId")]
    public string BroadcastEventId { get; set; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; set; } = string.Empty;

    [JsonPropertyName("areaId")]
    public string AreaId { get; set; } = string.Empty;

    [JsonPropertyName("stationId")]
    public string StationId { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("radioSeriesName")]
    public string RadioSeriesName { get; set; } = string.Empty;

    [JsonPropertyName("radioEpisodeName")]
    public string RadioEpisodeName { get; set; } = string.Empty;

    [JsonPropertyName("radioEpisodeId")]
    public string RadioEpisodeId { get; set; } = string.Empty;

    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = string.Empty;
}

public class Misc
{
    /// <summary>
    /// 出演者
    /// </summary>
    [JsonPropertyName("actList")]
    public ActList[] ActList { get; set; } = [];

    /// <summary>
    /// 音楽情報
    /// </summary>
    [JsonPropertyName("musicList")]
    public MusicList[] MusicList { get; set; } = [];
}

/// <summary>
/// 出演者リスト
/// </summary>
public class ActList
{
    /// <summary>
    /// 氏名
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 肩書
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 音楽リスト
/// </summary>
public class MusicList
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("byArtist")]
    public ByArtist[] ByArtist { get; set; } = [];
}

/// <summary>
/// 演奏者
/// </summary>
public class ByArtist
{
    /// <summary>
    /// 演者名
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 内容
/// </summary>
public class About
{
    /// <summary>
    /// 番組ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 番組名
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// キーワード
    /// </summary>
    [JsonPropertyName("keyword")]
    public string[] Keyword { get; set; } = [];

    /// <summary>
    /// 詳細
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// シリーズ情報
    /// </summary>
    [JsonPropertyName("partOfSeries")]
    public PartOfSeries PartOfSeries { get; set; } = new();

    /// <summary>
    /// エピソードJSON
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 番組ページ（個別）
    /// </summary>
    [JsonPropertyName("canonical")]
    public string Canonical { get; set; } = string.Empty;

    /// <summary>
    /// 音声情報
    /// </summary>
    [JsonPropertyName("audio")]
    [JsonConverter(typeof(AudioJsonConverter))]
    public Audio Audio { get; set; } = new();
}


public class PartOfSeries
{
    /// <summary>
    /// 番組名
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 番組詳細
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("keyword")]
    public string[] Keyword { get; set; } = [];

    [JsonPropertyName("logo")]
    public Logo Logo { get; set; } = new();
}

public class Logo
{
    [JsonPropertyName("medium")]
    public Medium Medium { get; set; } = new();
}


public class Medium
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}



/// <summary>
/// EPG詳細説明
/// </summary>
public class DetailedDescription
{
    [JsonPropertyName("epg40")]
    public string Epg40 { get; set; } = string.Empty;

    [JsonPropertyName("epg80")]
    public string Epg80 { get; set; } = string.Empty;

    [JsonPropertyName("epg200")]
    public string Epg200 { get; set; } = string.Empty;
}

/// <summary>
/// 音声情報
/// </summary>
public class Audio
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("detailedContent")]
    [JsonConverter(typeof(DetailedContentArrayOrSingleConverter))]
    public DetailedContent[] DetailedContent { get; set; } = [];

    [JsonPropertyName("expires")]
    public DateTimeOffset Expires { get; set; }
}

/// <summary>
/// コンテンツ詳細（聞き逃し配信）
/// </summary>
public class DetailedContent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("contentUrl")]
    public string ContentUrl { get; set; } = string.Empty;
}

internal sealed class AudioJsonConverter : JsonConverter<Audio>
{
    public override Audio Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Audio();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var source = root.ValueKind switch
        {
            JsonValueKind.Object => root,
            JsonValueKind.Array => root.EnumerateArray().FirstOrDefault(),
            _ => default
        };

        if (source.ValueKind != JsonValueKind.Object)
        {
            return new Audio();
        }

        var audio = new Audio
        {
            Id = GetString(source, "id"),
            Name = GetString(source, "name"),
            Description = GetString(source, "description"),
            DetailedContent = ParseDetailedContent(source, "detailedContent")
        };

        if (TryGetProperty(source, "expires", out var expiresElement) &&
            DateTimeOffset.TryParse(expiresElement.ToString(), out var expires))
        {
            audio.Expires = expires;
        }

        return audio;
    }

    public override void Write(Utf8JsonWriter writer, Audio value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new
        {
            value.Id,
            value.Name,
            value.Description,
            detailedContent = value.DetailedContent,
            expires = value.Expires
        }, options);
    }

    private static DetailedContent[] ParseDetailedContent(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => new DetailedContent
                {
                    Name = GetString(x, "name"),
                    ContentUrl = GetString(x, "contentUrl")
                })
                .ToArray();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return
            [
                new DetailedContent
                {
                    Name = GetString(value, "name"),
                    ContentUrl = GetString(value, "contentUrl")
                }
            ];
        }

        return [];
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }
}

internal sealed class DetailedContentArrayOrSingleConverter : JsonConverter<DetailedContent[]>
{
    public override DetailedContent[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => new DetailedContent
                {
                    Name = GetString(x, "name"),
                    ContentUrl = GetString(x, "contentUrl")
                })
                .ToArray();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            return
            [
                new DetailedContent
                {
                    Name = GetString(root, "name"),
                    ContentUrl = GetString(root, "contentUrl")
                }
            ];
        }

        return [];
    }

    public override void Write(Utf8JsonWriter writer, DetailedContent[] value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }
}

