using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext
{
    public class RecordingTag
    {
        /// <summary>
        /// タグID
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// タグ名
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 正規化済みタグ名
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// 更新日時
        /// </summary>
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// 最終利用日時
        /// </summary>
        public DateTimeOffset? LastUsedAt { get; set; }

        public ICollection<RecordingTagRelation> RecordingTagRelations { get; set; } = new List<RecordingTagRelation>();
        public ICollection<KeywordReserveTagRelation> KeywordReserveTagRelations { get; set; } = new List<KeywordReserveTagRelation>();
    }
}
