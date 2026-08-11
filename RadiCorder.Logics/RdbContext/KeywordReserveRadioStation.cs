using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.RdbContext
{
    /// <summary>
    /// キーワード録音予約情報
    /// </summary>
    public class KeywordReserveRadioStation
    {
        /// <summary>
        /// 予約ID
        /// </summary>
        [Required]
        [MaxLength(26)]
        public Ulid Id { get; set; }


        /// <summary>
        /// サービス種別
        /// </summary>
        public RadioServiceKind RadioServiceKind { get; set; }


        /// <summary>
        /// 録音対象放送局ID
        /// </summary>
        [MaxLength(20)]
        public string RadioStation { get; set; } = string.Empty;
    }
}
