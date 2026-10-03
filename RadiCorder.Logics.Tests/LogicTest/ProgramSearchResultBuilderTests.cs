using Moq;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProgramSearchResultBuilderTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-04-01T00:00:00Z");
    private static readonly EntryMapper Mapper = new(CreateConfiguration());

    private static IAppConfigurationService CreateConfiguration()
    {
        var config = new Mock<IAppConfigurationService>();
        config.SetupGet(x => x.RadikoStationDic).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>());
        return config.Object;
    }

    [TestCase(KeywordReserveOrderKind.ProgramStartDateTimeAsc, "R0,N0,N1,R2")]
    [TestCase(KeywordReserveOrderKind.ProgramStartDateTimeDesc, "R2,N1,R0,N0")]
    [TestCase(KeywordReserveOrderKind.ProgramEndDateTimeAsc, "R0,N0,N1,R2")]
    [TestCase(KeywordReserveOrderKind.ProgramEndDateTimeDesc, "R2,N1,R0,N0")]
    [TestCase(KeywordReserveOrderKind.ProgramNameAsc, "R0,N0,R2,N1")]
    [TestCase(KeywordReserveOrderKind.ProgramNameDesc, "N1,R2,R0,N0")]
    [TestCase((KeywordReserveOrderKind)0, "R2,R0,N1,N0")]
    public void Build_並び順と同順位のサービス優先順を維持する(KeywordReserveOrderKind order, string expected)
    {
        var radiko = new[] { Radiko(2, "B"), Radiko(0, "A") };
        var radiru = new[] { Radiru(1, "C"), Radiru(0, "A") };
        var result = ProgramSearchResultBuilder.Build(radiko, radiru, order, Mapper);
        Assert.That(result.Select(x => x.ProgramId), Is.EqualTo(expected.Split(',')));
    }

    [TestCase(KeywordReserveOrderKind.ProgramStartDateTimeAsc, 50, 50)]
    [TestCase((KeywordReserveOrderKind)0, 60, 40)]
    public void Build_サービス横断で100件に制限する(KeywordReserveOrderKind order, int radikoCount, int radiruCount)
    {
        var result = ProgramSearchResultBuilder.Build(
            Enumerable.Range(0, 60).Select(i => Radiko(i, "番組")).ToArray(),
            Enumerable.Range(0, 60).Select(i => Radiru(i, "番組")).ToArray(), order, Mapper);
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(100));
            Assert.That(result.Count(x => x.ServiceKind == RadioServiceKind.Radiko), Is.EqualTo(radikoCount));
            Assert.That(result.Count(x => x.ServiceKind == RadioServiceKind.Radiru), Is.EqualTo(radiruCount));
        });
    }

    private static RadikoProgram Radiko(int minute, string title) => new()
    {
        ProgramId = $"R{minute}", Title = title, StartTime = Start.AddMinutes(minute), EndTime = Start.AddMinutes(minute + 30)
    };

    private static NhkRadiruProgram Radiru(int minute, string title) => new()
    {
        ProgramId = $"N{minute}", Title = title, AreaId = "130", StationId = "r1",
        StartTime = Start.AddMinutes(minute), EndTime = Start.AddMinutes(minute + 30)
    };
}
