using AngleSharp;
using AngleSharp.Html.Parser;

namespace RadiCorder.Logics.Extensions
{
    public static class HtmlExtensions
    {
        public static string RemoveImageTagsFromHtml(this string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = context.GetService<IHtmlParser>();
            var document = parser!.ParseDocument(html);

            foreach (var image in document.QuerySelectorAll("img").ToList())
            {
                image.Remove();
            }

            return document.Body?.InnerHtml ?? string.Empty;
        }

        public static string ExtractTextFromHtml(this string html)
        {
            // AngleSharpの設定を構成
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);

            // HTMLをパース
            var parser = context.GetService<IHtmlParser>();
            var document = parser!.ParseDocument(html);

            // テキストを抽出
            return document.Body?.TextContent ?? string.Empty;
        }
    }
}
