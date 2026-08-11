using RadiCorder.Logics.Extensions;

namespace RadiCorder.Logics.Tests.ExtensionsTest
{
    public class RadioProgramExtensionsTests
    {
        [Test]
        public void ToRadioDayOfWeek_Should_Return_Correct_DayOfWeek()
        {
            // 2023年12月31日(日) 午前4時
            {
                var dateTime = new DateTimeOffset(2023, 12, 31, 4, 59, 59, TimeSpan.FromHours(9)); // 午前4時

                var result = dateTime.ToRadioDayOfWeek();

                Assert.That(result, Is.EqualTo(DayOfWeek.Saturday));
            }

            // 2023年12月31日(日) 午前5時
            {
                var dateTime = new DateTimeOffset(2023, 12, 31, 5, 0, 0, TimeSpan.FromHours(9)); // 午前5時

                var result = dateTime.ToRadioDayOfWeek();

                Assert.That(result, Is.EqualTo(DayOfWeek.Sunday));
            }
        }

        [Test]
        public void ToRadioDate_Should_Return_Correct_DateOnly()
        {
            // 2022年1月1日(土) 午前4時
            {
                var dateTime = new DateTimeOffset(2024, 1, 1, 4, 59, 59, TimeSpan.FromHours(9)); // 午前4時

                var result = dateTime.ToRadioDate();

                Assert.That(result, Is.EqualTo(new DateOnly(2023, 12, 31)));
            }

            // 2022年1月1日(土) 午前5時
            {
                var dateTime = new DateTimeOffset(2024, 1, 1, 5, 0, 0, TimeSpan.FromHours(9)); // 午前5時

                var result = dateTime.ToRadioDate();

                Assert.That(result, Is.EqualTo(new DateOnly(2024, 1, 1)));
            }
        }
    }
}