using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext
{
    public class RecordingTagRelation
    {
        /// <summary>
        /// ID
        /// </summary>
        [Required]
        [Key]
        [MaxLength(26)]
        public Ulid RecordingId { get; set; }

        public Recording Recording { get; set; } = null!;

        /// <summary>
        /// タグID
        /// </summary>
        public Guid TagId { get; set; }

        public RecordingTag Tag { get; set; } = null!;

    }
}
