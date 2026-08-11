using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RadiruProgramJsonEntityTests
{
    [Test]
    public void FromJson_型揺れを含んでもpublicationを取り込める()
    {
        var json = """
                   {
                     "r2": {
                       "publication": [
                         {
                           "id": "ok-1",
                           "name": "Program A",
                           "description": "desc-a",
                           "startDate": "2026-02-15T10:00:00+09:00",
                           "endDate": "2026-02-15T10:30:00+09:00",
                           "identifierGroup": {
                             "serviceId": "r2",
                             "areaId": "130",
                             "stationId": "700",
                             "eventId": "1001",
                             "radioSeriesName": "Series A",
                             "radioEpisodeName": "Episode A",
                             "siteId": "5707"
                           },
                           "misc": {
                             "actList": [],
                             "musicList": []
                           },
                           "about": {
                             "id": "about-1",
                             "name": "About A",
                             "keyword": [],
                             "description": "about-desc-a",
                             "partOfSeries": {
                               "name": "Series A",
                               "description": "series-desc-a",
                               "keyword": [],
                               "logo": {
                                 "medium": {
                                   "url": "https://example.com/a.jpg",
                                   "width": 640,
                                   "height": 640
                                 }
                               }
                             }
                           },
                           "detailedDescription": {
                             "epg40": "epg40-a",
                             "epg80": "epg80-a",
                             "epg200": "epg200-a"
                           }
                         },
                         {
                           "id": "lenient-2",
                           "name": "Program B",
                           "description": "desc-b",
                           "startDate": "2026-02-15T11:00:00+09:00",
                           "endDate": "2026-02-15T11:30:00+09:00",
                           "identifierGroup": {
                             "serviceId": "r2",
                             "areaId": "130",
                             "stationId": "700",
                             "eventId": "1002",
                             "radioSeriesName": "Series B",
                             "radioEpisodeName": "Episode B",
                             "siteId": "5707"
                           },
                           "misc": {
                             "actList": {
                               "name": "Actor B",
                               "title": "Host"
                             },
                             "musicList": {
                               "name": "Song B",
                               "byArtist": {
                                 "name": "Artist B"
                               }
                             }
                           },
                           "about": {
                             "id": "about-2",
                             "name": "About B",
                             "keyword": "single-keyword",
                             "description": "about-desc-b",
                             "partOfSeries": {
                               "name": "Series B",
                               "description": "series-desc-b",
                               "keyword": "series-keyword",
                               "logo": {
                                 "medium": {
                                   "url": "https://example.com/b.jpg",
                                   "width": "640",
                                   "height": "640"
                                 }
                               }
                             },
                             "audio": {
                               "id": "audio-b",
                               "name": "Audio B",
                               "description": "audio-desc-b",
                               "detailedContent": {
                                 "name": "Content B",
                                 "contentUrl": "https://example.com/content-b.m3u8"
                               },
                               "expires": "2026-02-20T00:00:00+09:00"
                             }
                           },
                           "detailedDescription": {
                             "epg40": "epg40-b",
                             "epg80": "epg80-b",
                             "epg200": "epg200-b"
                           }
                         }
                       ]
                     }
                   }
                   """;

        var list = RadiruProgramJsonEntity.FromJson(json);

        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].Id, Is.EqualTo("ok-1"));
        Assert.That(list[1].Id, Is.EqualTo("lenient-2"));
        Assert.That(list[1].Misc.ActList.Length, Is.EqualTo(1));
        Assert.That(list[1].Misc.MusicList.Length, Is.EqualTo(1));
        Assert.That(list[1].Misc.MusicList[0].ByArtist.Length, Is.EqualTo(1));
        Assert.That(list[1].About.Keyword, Is.EqualTo(new[] { "single-keyword" }));
        Assert.That(list[1].About.PartOfSeries.Keyword, Is.EqualTo(new[] { "series-keyword" }));
        Assert.That(list[1].About.PartOfSeries.Logo.Medium.Width, Is.EqualTo(640));
        Assert.That(list[1].About.PartOfSeries.Logo.Medium.Height, Is.EqualTo(640));
        Assert.That(list[1].About.Audio.Id, Is.EqualTo("audio-b"));
        Assert.That(list[1].About.Audio.DetailedContent.Length, Is.EqualTo(1));
        Assert.That(list[1].About.Audio.DetailedContent[0].ContentUrl, Is.EqualTo("https://example.com/content-b.m3u8"));
    }
}
