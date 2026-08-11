using System.Text.RegularExpressions;

namespace RadiCorder.Logics.Extensions
{
    public static class StringExtensions
    {
        /// <summary>
        /// LinuxとWindowsで、ファイルパスに含めることができない文字を全角に変換して返す
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static string ToSafeName(this string filePath)
        {
            return filePath
                .Replace(@"\", "￥")
                .Replace("/", "／")
                .Replace(":", "：")
                .Replace("*", "＊")
                .Replace("?", "？")
                .Replace("\"", "”")
                .Replace("<", "＜")
                .Replace(">", "＞")
                .Replace("|", "｜")
                .To半角英数字();
        }

        /// <summary>
        /// LinuxとWindowsで、ファイルパスに含めることができない文字を全角に変換したうえで、コマンドで渡せる値として返す
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static string ToSafeNameAndSafeCommandParameter(this string filePath)
        {
            return filePath.ToSafeName()
                .Replace("\"", "”")
                .Replace("'", "’");
        }

        public static string To半角英数字(this string filePath)
        {
            // 全角英数字を半角英数字に変換
            return filePath
                .Replace("０", "0")
                .Replace("１", "1")
                .Replace("２", "2")
                .Replace("３", "3")
                .Replace("４", "4")
                .Replace("５", "5")
                .Replace("６", "6")
                .Replace("７", "7")
                .Replace("８", "8")
                .Replace("９", "9")
                .Replace("Ａ", "A")
                .Replace("Ｂ", "B")
                .Replace("Ｃ", "C")
                .Replace("Ｄ", "D")
                .Replace("Ｅ", "E")
                .Replace("Ｆ", "F")
                .Replace("Ｇ", "G")
                .Replace("Ｈ", "H")
                .Replace("Ｉ", "I")
                .Replace("Ｊ", "J")
                .Replace("Ｋ", "K")
                .Replace("Ｌ", "L")
                .Replace("Ｍ", "M")
                .Replace("Ｎ", "N")
                .Replace("Ｏ", "O")
                .Replace("Ｐ", "P")
                .Replace("Ｑ", "Q")
                .Replace("Ｒ", "R")
                .Replace("Ｓ", "S")
                .Replace("Ｔ", "T")
                .Replace("Ｕ", "U")
                .Replace("Ｖ", "V")
                .Replace("Ｗ", "W")
                .Replace("Ｘ", "X")
                .Replace("Ｙ", "Y")
                .Replace("Ｚ", "Z")
                .Replace("ａ", "a")
                .Replace("ｂ", "b")
                .Replace("ｃ", "c")
                .Replace("ｄ", "d")
                .Replace("ｅ", "e")
                .Replace("ｆ", "f")
                .Replace("ｇ", "g")
                .Replace("ｈ", "h")
                .Replace("ｉ", "i")
                .Replace("ｊ", "j")
                .Replace("ｋ", "k")
                .Replace("ｌ", "l")
                .Replace("ｍ", "m")
                .Replace("ｎ", "n")
                .Replace("ｏ", "o")
                .Replace("ｐ", "p")
                .Replace("ｑ", "q")
                .Replace("ｒ", "r")
                .Replace("ｓ", "s")
                .Replace("ｔ", "t")
                .Replace("ｕ", "u")
                .Replace("ｖ", "v")
                .Replace("ｗ", "w")
                .Replace("ｘ", "x")
                .Replace("ｙ", "y")
                .Replace("ｚ", "z");
        }

        /// <summary>
        /// 文字列内に指定された文字が含まれるかをチェックする拡張メソッド
        /// </summary>
        /// <param name="source"></param>
        /// <param name="chars"></param>
        /// <returns></returns>
        public static bool ContainsAny(this string source, params char[] chars)
        {
            return source.Any(chars.Contains);
        }


        /// <summary>
        /// 指定された文字列をキーワードに分割して返す拡張メソッド
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static List<string> ParseKeywords(this string input)
        {
            var result = new List<string>();

            // ダブルクォーテーションで囲まれた部分を優先的に抽出
            var matches = Regex.Matches(input, "\"([^\"]*)\"");

            foreach (Match match in matches)
            {
                result.Add(match.Groups[1].Value);
            }

            // ダブルクォーテーションで囲まれていない部分を抽出
            var remainingInput = Regex.Replace(input, "\"([^\"]*)\"", "");
            var remainingKeywords = remainingInput.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            result.AddRange(remainingKeywords);

            return result;
        }

        /// <summary>
        /// ログファイル名として安全に使える文字列へ正規化する。
        /// </summary>
        public static string ToSafeLogFileName(this string? rawFileName, int maxLength = 120)
        {
            if (string.IsNullOrWhiteSpace(rawFileName))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var trimmed = rawFileName.Trim();
            var buffer = new char[trimmed.Length];
            var index = 0;

            foreach (var c in trimmed)
            {
                if (Array.IndexOf(invalidChars, c) >= 0 ||
                    c == Path.DirectorySeparatorChar ||
                    c == Path.AltDirectorySeparatorChar ||
                    c == '/' ||
                    c == '\\' ||
                    char.IsControl(c))
                {
                    buffer[index++] = '_';
                    continue;
                }

                buffer[index++] = c;
            }

            var normalized = new string(buffer, 0, index);
            normalized = normalized.Replace("..", "_", StringComparison.Ordinal);
            normalized = normalized.Trim(' ', '.');

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (maxLength > 0 && normalized.Length > maxLength)
            {
                normalized = normalized[..maxLength];
            }

            return normalized;
        }
    }
}
