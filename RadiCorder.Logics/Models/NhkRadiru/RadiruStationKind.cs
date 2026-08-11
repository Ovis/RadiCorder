using RadiCorder.Logics.Primitives;

namespace RadiCorder.Logics.Models.NhkRadiru
{
    public class RadiruStationKind : Enumeration
    {
        private static readonly TimeSpan VisibilityGracePeriod = TimeSpan.FromDays(7);

        public static readonly RadiruStationKind R1 = new(
            id: 1,
            name: "NHK-AM",
            serviceId: "r1");

        public static readonly RadiruStationKind R2 = new(
            id: 2,
            name: "NHKラジオ第2",
            serviceId: "r2",
            abolishedAtJst: new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.FromHours(9)));

        public static readonly RadiruStationKind FM = new(
            id: 3,
            name: "NHK-FM",
            serviceId: "r3");



        public string ServiceId { get; set; }

        public DateTimeOffset? AvailableFromJst { get; set; }

        public DateTimeOffset? AbolishedAtJst { get; set; }

        /// <summary>
        /// らじる★らじるの局種別を初期化する
        /// </summary>
        /// <param name="id">ID</param>
        /// <param name="name">表示名</param>
        public RadiruStationKind(int id, string name) : base(id, name)
        {
            ServiceId = string.Empty;
            AvailableFromJst = null;
            AbolishedAtJst = null;
        }

        public RadiruStationKind(
            int id,
            string name,
            string serviceId,
            DateTimeOffset? availableFromJst = null,
            DateTimeOffset? abolishedAtJst = null) : base(id, name)
        {
            ServiceId = serviceId;
            AvailableFromJst = availableFromJst;
            AbolishedAtJst = abolishedAtJst;
        }

        public bool IsVisibleAt(DateTimeOffset nowJst)
        {
            if (AvailableFromJst.HasValue && nowJst < AvailableFromJst.Value)
            {
                return false;
            }

            if (AbolishedAtJst.HasValue && nowJst >= AbolishedAtJst.Value.Add(VisibilityGracePeriod))
            {
                return false;
            }

            return true;
        }

        public bool CanFetchProgramsForDateAt(DateTimeOffset nowJst, DateTimeOffset targetDateJst)
        {
            if (AvailableFromJst.HasValue && targetDateJst < AvailableFromJst.Value)
            {
                return false;
            }

            if (!AbolishedAtJst.HasValue)
            {
                return true;
            }

            if (nowJst >= AbolishedAtJst.Value.Add(VisibilityGracePeriod))
            {
                return false;
            }

            return targetDateJst < AbolishedAtJst.Value;
        }

        public static RadiruStationKind? FindByServiceId(string serviceId)
        {
            return Enumeration.GetAll<RadiruStationKind>()
                .FirstOrDefault(r => r.ServiceId == serviceId);
        }
    }
}
