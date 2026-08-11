using System.Globalization;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Primitives;

namespace RadiCorder.Logics.Context;

public class RadioAppContext(DateTimeOffset? standardDateTimeOffset = null) : IRadioAppContext
{
    public DateTimeOffset StandardDateTimeOffset { get; } = standardDateTimeOffset ?? DateTimeOffset.UtcNow.ToNormalizedByTz();

    public TimeZoneInfo TimeZoneInfo { get; } = JapanTimeZone.Resolve();

    public CultureInfo CultureInfo { get; } = new("ja-JP");
}
