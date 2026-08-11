using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext
{
    public class AppConfiguration
    {
        /// <summary>
        /// 設定項目名
        /// </summary>
        [Key]
        [MaxLength(100)]
        public string ConfigurationName { get; set; } = string.Empty;

        /// <summary>
        /// 文字列項目
        /// </summary>
        [MaxLength(255)]
        public string? Val1 { get; set; }

        /// <summary>
        /// 数値項目
        /// </summary>
        public int? Val2 { get; set; }

        /// <summary>
        /// 整数値項目
        /// </summary>
        public decimal? Val3 { get; set; }

        /// <summary>
        /// 日付項目
        /// </summary>
        public DateTime? Val4 { get; set; }




    }
}
