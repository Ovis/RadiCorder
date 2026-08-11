using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.NhkRadiru;

namespace RadiCorder.Models
{
    public class KeywordReserveViewModel
    {
        public IEnumerable<RadikoStationInformationEntry> RadikoStationList { get; set; } = [];

        public IEnumerable<RadiruStationEntry> RadiruStationList { get; set; } = [];
    }
}
