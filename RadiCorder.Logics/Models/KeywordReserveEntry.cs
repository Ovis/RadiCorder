using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Models
{
    public class KeywordReserveEntry
    {
        public Ulid Id { get; set; }

        public List<string> SelectedRadikoStationIds { get; set; } = [];

        public List<string> SelectedRadiruStationIds { get; set; } = [];

        public string Keyword { get; set; } = string.Empty;

        public string ExcludedKeyword { get; set; } = string.Empty;

        public string RecordPath { get; set; } = string.Empty;

        public string RecordFileName { get; set; } = string.Empty;

        public bool SearchTitleOnly { get; set; }

        public bool ExcludeTitleOnly { get; set; }

        public List<DaysOfWeek> SelectedDaysOfWeek { get; set; } = [];

        public string StartTimeString { get; set; } = string.Empty;

        public string EndTimeString { get; set; } = string.Empty;

        public TimeOnly? StartTime => string.IsNullOrEmpty(StartTimeString) ? null : TimeOnly.Parse(StartTimeString);

        public TimeOnly? EndTime => string.IsNullOrEmpty(EndTimeString) ? null : TimeOnly.Parse(EndTimeString);

        public bool IsEnabled { get; set; }

        public double? StartDelay { get; set; }

        public double? EndDelay { get; set; }

        public int SortOrder { get; set; }

        public List<Guid> TagIds { get; set; } = [];

        public List<string> Tags { get; set; } = [];

        public KeywordReserveTagMergeBehavior MergeTagBehavior { get; set; } = KeywordReserveTagMergeBehavior.Default;
    }
}
