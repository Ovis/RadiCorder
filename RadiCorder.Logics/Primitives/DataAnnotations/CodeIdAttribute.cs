using System.Reflection;

namespace RadiCorder.Logics.Primitives.DataAnnotations
{
    [AttributeUsage(AttributeTargets.Field)]
    public class CodeIdAttribute(string codeId) : Attribute
    {
        public string Id { get; private set; } = codeId;
    }


    public static class CodeIdAttributeExtensions
    {
        public static string GetEnumCodeId<T>(this T value) where T : Enum
        {
            var field = value.GetType().GetField(value.ToString());
            if (field != null)
            {
                var attribute = field.GetCustomAttribute<CodeIdAttribute>();
                return attribute?.Id ?? string.Empty;
            }
            else
            {
                return string.Empty;
            }
        }


        public static T? GetEnumByCodeId<T>(this string codeId) where T : Enum
        {
            if (string.IsNullOrEmpty(codeId))
            {
                throw new ArgumentNullException(nameof(codeId));
            }

            foreach (T value in Enum.GetValues(typeof(T)))
            {
                if (codeId == value.GetEnumCodeId())
                {
                    return value;
                }
            }
            return default;
        }
    }
}
