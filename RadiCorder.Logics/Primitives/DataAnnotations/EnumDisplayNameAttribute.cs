namespace RadiCorder.Logics.Primitives.DataAnnotations
{
    [AttributeUsage(AttributeTargets.Field)]
    public class EnumDisplayNameAttribute : Attribute
    {
        public string Name { get; set; }

        /// <summary>
        /// 表示名
        /// </summary>
        /// <param name="name"></param>
        public EnumDisplayNameAttribute(string name)
        {
            Name = name;
        }
    }

    public static class EnumDisplayNameAttributeExtensions
    {

        public static string GetEnumDisplayName<T>(this T enumValue)
        {
            if (enumValue == null)
                throw new ArgumentNullException(nameof(enumValue));

            var field = typeof(T).GetField(enumValue.ToString() ?? string.Empty);

            if (field == null)
                return string.Empty;

            var attrType = typeof(EnumDisplayNameAttribute);
            var attribute = Attribute.GetCustomAttribute(field, attrType);
            var enumDisplayName = (attribute as EnumDisplayNameAttribute)?.Name;
            return enumDisplayName ?? string.Empty;
        }
    }
}
