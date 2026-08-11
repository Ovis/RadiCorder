using System.Text.Json.Serialization;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Primitives.DataAnnotations;

namespace RadiCorder.Logics.Models
{
    public class ProgramSearchEntity
    {
        public string ServiceKind { get; set; } = string.Empty;

        public List<string> SelectedRadikoStationIds { get; set; } = [];

        public List<string> SelectedRadiruStationIds { get; set; } = [];

        public string Keyword { get; set; } = string.Empty;

        public string ExcludedKeyword { get; set; } = string.Empty;

        public bool SearchTitleOnly { get; set; }

        public bool SearchTitleOnlyExcludedKeyword { get; set; }

        public List<DaysOfWeek> SelectedDaysOfWeek { get; set; } = [];

        public TimeOnly StartTime { get; set; }

        public TimeOnly EndTime { get; set; }

        public bool IncludeHistoricalPrograms { get; set; }

        public bool RecordableOnly { get; set; }

        [JsonPropertyName("orderKind")]
        public string OrderKindString { get; set; } = string.Empty;

        [JsonIgnore]
        public KeywordReserveOrderKind OrderKind => OrderKindString.GetEnumByCodeId<KeywordReserveOrderKind>();
    }
}
