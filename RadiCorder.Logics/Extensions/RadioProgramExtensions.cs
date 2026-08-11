namespace RadiCorder.Logics.Extensions
{
    public static class RadioProgramExtensions
    {

        public static DayOfWeek ToRadioDayOfWeek(this DateTimeOffset dateTime)
        {
            // ラジオの1日の切り替わりは午前5時（29時）
            const int cutoffHour = 5;

            // 指定された日時の時刻が午前5時前の場合
            if (dateTime.Hour < cutoffHour)
            {
                // 前の日に遡る
                dateTime = dateTime.AddDays(-1);
            }

            return dateTime.DayOfWeek;
        }


        /// <summary>
        /// 午前5時00分が日付境界となる日付の取得
        /// </summary>
        public static DateOnly ToRadioDate(this DateTimeOffset dateTime)
        {
            // ラジオの1日の切り替わりは午前5時（29時）
            const int cutoffHour = 5;

            // 指定された日時の時刻が午前5時前の場合
            if (dateTime.Hour < cutoffHour)
            {
                // 前の日に遡る
                dateTime = dateTime.AddDays(-1);
            }

            // DateTimeからDateOnlyに変換
            return new DateOnly(dateTime.Year, dateTime.Month, dateTime.Day);
        }
    }
}
