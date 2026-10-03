using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

public class KeywordReservationPolicyTests
{
    [Test]
    public void Priority_並び順が同じ場合はID順に決定する()
    {
        var lower = new KeywordReserve { Id = Ulid.Parse("01ARZ3NDEKTSV4RRFFQ69G5FAV"), SortOrder = 3 };
        var higher = new KeywordReserve { Id = Ulid.Parse("01ARZ3NDEKTSV4RRFFQ69G5FAW"), SortOrder = 3 };
        Assert.That(KeywordReservationPolicy.ResolvePrimaryKeywordReserve([higher, lower]), Is.SameAs(lower));
        Assert.That(KeywordReservationPolicy.IsHigherPriority(lower, higher), Is.True);
        higher.SortOrder = 2;
        Assert.That(KeywordReservationPolicy.ResolvePrimaryKeywordReserve([lower, higher]), Is.SameAs(higher));
        Assert.That(KeywordReservationPolicy.IsHigherPriority(lower, higher), Is.False);
    }

    [TestCase(AvailabilityTimeFree.Available, RecordingType.TimeFree)]
    [TestCase(AvailabilityTimeFree.PartiallyAvailable, RecordingType.TimeFree)]
    [TestCase(AvailabilityTimeFree.Unavailable, RecordingType.RealTime)]
    public void RecordingType_タイムフリー可否の規則を維持する(AvailabilityTimeFree availability, RecordingType expected)
    {
        Assert.That(KeywordReservationPolicy.ResolveKeywordReserveRecordingType(new RadioProgramEntry { AvailabilityTimeFree = availability }), Is.EqualTo(expected));
    }
}
