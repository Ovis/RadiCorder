namespace RadiCorder.Logics.Models.Radiko
{
    public enum AvailabilityTimeFree
    {
        /// <summary>
        /// 利用可能
        /// </summary>
        Available = 0,

        PartiallyAvailable = 1,

        /// <summary>
        /// 利用不可
        /// </summary>
        Unavailable = 2,
    }

    public static class AvailabilityTimeFreeExtensions
    {
        public static string ToDisplayString(this AvailabilityTimeFree availabilityTimeFree)
        {
            return availabilityTimeFree switch
            {
                AvailabilityTimeFree.Available => "利用可能",
                AvailabilityTimeFree.PartiallyAvailable => "一部利用可能",
                AvailabilityTimeFree.Unavailable => "利用不可",
                _ => throw new ArgumentOutOfRangeException(nameof(availabilityTimeFree), availabilityTimeFree, null)
            };
        }


        public static AvailabilityTimeFree FromString(this string availabilityTimeFreeString)
        {
            return availabilityTimeFreeString switch
            {
                "0" => AvailabilityTimeFree.Available,
                "1" => AvailabilityTimeFree.PartiallyAvailable,
                "2" => AvailabilityTimeFree.Unavailable,
                _ => throw new ArgumentOutOfRangeException(nameof(availabilityTimeFreeString), availabilityTimeFreeString, null)
            };
        }
    }
}
