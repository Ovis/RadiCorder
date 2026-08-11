using RadiCorder.Logics.Primitives.DataAnnotations;

namespace RadiCorder.Logics.Models.Enums
{
    [Flags]
    public enum DaysOfWeek
    {
        None = 0,
        [EnumDisplayName("日曜日")]
        Sunday = 1 << 0,  // 1
        [EnumDisplayName("月曜日")]
        Monday = 1 << 1,  // 2
        [EnumDisplayName("火曜日")]
        Tuesday = 1 << 2, // 4
        [EnumDisplayName("水曜日")]
        Wednesday = 1 << 3, // 8
        [EnumDisplayName("木曜日")]
        Thursday = 1 << 4, // 16
        [EnumDisplayName("金曜日")]
        Friday = 1 << 5, // 32
        [EnumDisplayName("土曜日")]
        Saturday = 1 << 6 // 64
    }

    public static class DaysOfWeekExtensions
    {
        public static DaysOfWeek ToDaysOfWeek(this DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Sunday => DaysOfWeek.Sunday,
                DayOfWeek.Monday => DaysOfWeek.Monday,
                DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
                DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
                DayOfWeek.Thursday => DaysOfWeek.Thursday,
                DayOfWeek.Friday => DaysOfWeek.Friday,
                DayOfWeek.Saturday => DaysOfWeek.Saturday,
                _ => DaysOfWeek.None
            };
        }


        public static List<DaysOfWeek> ToList(this DaysOfWeek daysOfWeek)
        {
            return Enum.GetValues(typeof(DaysOfWeek)).Cast<DaysOfWeek>()
                .Where(day => day != DaysOfWeek.None && daysOfWeek.HasFlag(day)
                ).ToList();
        }
    }
}
