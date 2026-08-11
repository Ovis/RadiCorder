namespace RadiCorder.Logics.Extensions
{
    public static class NetworkPathReferenceConverterExtensions
    {
        public static string ToHttpsUrl(this string url)
        {
            if (url.StartsWith("//"))
            {
                return "https:" + url;
            }

            return url;
        }
    }
}
