using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Features.Shared.Models
{
    public class ProgramInformationRequestEntry
    {
        /// <summary>
        /// 番組ID
        /// </summary>
        [Required]
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 配信サービス種別
        /// </summary>
        public RadioServiceKind RadioServiceKind { get; set; }

        /// <summary>
        /// 録音方法
        /// </summary>
        public RecordingType RecordingType { get; set; }
    }
}

