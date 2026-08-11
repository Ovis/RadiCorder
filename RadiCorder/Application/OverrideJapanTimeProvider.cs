using RadiCorder.Logics.Primitives;

namespace RadiCorder.Application
{
    public class OverrideJapanTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone =>
            JapanTimeZone.Resolve();
    }
}
