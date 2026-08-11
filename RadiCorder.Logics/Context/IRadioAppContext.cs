using System.Globalization;

namespace RadiCorder.Logics.Context
{
    public interface IRadioAppContext
    {
        public DateTimeOffset StandardDateTimeOffset { get; }

        public TimeZoneInfo TimeZoneInfo { get; }

        public CultureInfo CultureInfo { get; }
    }
}
