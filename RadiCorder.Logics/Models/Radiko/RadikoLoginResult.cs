using System.Text.Json.Serialization;

namespace RadiCorder.Logics.Models.Radiko
{
    public class RadikoLoginResult
    {
        [JsonPropertyName("unpaid")]
        public string UnPaid { get; set; } = string.Empty;

        [JsonPropertyName("areafree")]
        public string AreaFree { get; set; } = string.Empty;

        [JsonPropertyName("privileges")]
        public string[] Privileges { get; set; } = [];

        [JsonPropertyName("twitter_name")]
        public string TwitterName { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("member_ukey")]
        public string MemberUKey { get; set; } = string.Empty;

        [JsonPropertyName("radiko_session")]
        public string RadikoSession { get; set; } = string.Empty;

        [JsonPropertyName("paid_member")]
        public string PaidMember { get; set; } = string.Empty;

        [JsonPropertyName("facebook_name")]
        public string FacebookName { get; set; } = string.Empty;
    }
}
