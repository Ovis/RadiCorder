using System.Runtime.InteropServices;

namespace RadiCorder.Logics.Extensions
{
    public static class FolderPathExtensions
    {
        /// <param name="fileName">チェックするファイル名</param>
        extension(string fileName)
        {
            /// <summary>
            /// 指定されたファイル名が適切かどうかを判断する。
            /// </summary>
            /// <returns>パスが有効であればtrue、無効であればfalse</returns>
            public bool IsValidFileName()
            {
                // パスがnullまたは空であれば無効
                if (string.IsNullOrEmpty(fileName?.Trim()))
                {
                    return false;
                }

                // パスに無効な文字が含まれているかチェック
                var invalidChars = InvalidFileNameChars;
                if (fileName.IndexOfAny(invalidChars) >= 0)
                {
                    return false;
                }

                // パスを正規化して、ディレクトリトラバーサル攻撃を防ぐ
                try
                {
                    // 仮にベースディレクトリからの相対パスを取得
                    var baseUri = new Uri(Environment.CurrentDirectory + Path.DirectorySeparatorChar);
                    var fullUri = new Uri(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, fileName)));

                    // フルパスがベースディレクトリのサブディレクトリであるかを確認
                    return baseUri.IsBaseOf(fullUri);
                }
                catch
                {
                    // 例外が発生した場合は無効とする
                    return false;
                }
            }

            /// <summary>
            /// 指定されたパスが絶対パスとして適切かどうかを判定
            /// </summary>
            /// <returns></returns>
            public bool IsValidAbsolutePath()
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? fileName.IsValidWindowsAbsolutePath()
                    : fileName.IsValidUnixAbsolutePath();
            }

            /// <summary>
            /// 指定されたパスが相対パスとして有効かどうかを判断する。
            /// ディレクトリトラバーサル攻撃にも対応。
            /// </summary>
            /// <returns>パスが有効であればtrue、無効であればfalse</returns>
            public bool IsValidRelativePath()
            {
                // パスがnullまたは空であれば無効
                if (string.IsNullOrEmpty(fileName?.Trim()))
                {
                    return false;
                }

                // 絶対パスではないかをチェック
                if (Path.IsPathRooted(fileName))
                {
                    return false;
                }

                // Windows形式の絶対パス/UNC形式も拒否（Linux実行時にも効かせる）
                if (fileName.StartsWith(@"\\", StringComparison.Ordinal) ||
                    fileName.StartsWith("//", StringComparison.Ordinal) ||
                    System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^[A-Za-z]:[\\/].*"))
                {
                    return false;
                }

                // パスに無効な文字が含まれているかチェック
                var invalidChars = Path.GetInvalidPathChars();
                if (fileName.IndexOfAny(invalidChars) >= 0)
                {
                    return false;
                }

                // 明示的なトラバーサル表現を拒否（区切り文字は両方扱う）
                var segments = fileName
                    .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Any(segment => segment is "." or ".."))
                {
                    return false;
                }

                // パスを正規化して、ディレクトリトラバーサル攻撃を防ぐ
                try
                {
                    // 仮にベースディレクトリからの相対パスを取得
                    var baseUri = new Uri(Environment.CurrentDirectory + Path.DirectorySeparatorChar);
                    var fullUri = new Uri(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, fileName)));

                    // フルパスがベースディレクトリのサブディレクトリであるかを確認
                    return baseUri.IsBaseOf(fullUri);
                }
                catch
                {
                    // 例外が発生した場合は無効とする
                    return false;
                }
            }

            /// <summary>
            /// ベースパスに対して相対パスを結合して絶対パスを取得、検証する
            /// </summary>
            /// <param name="relativePath"></param>
            /// <param name="combinedPath"></param>
            /// <returns></returns>
            public bool TryCombinePaths(string relativePath, out string combinedPath)
            {
                try
                {
                    combinedPath = string.Empty;

                    if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(relativePath))
                    {
                        return false;
                    }

                    // 相対パスのみ許容
                    if (Path.IsPathRooted(relativePath))
                    {
                        return false;
                    }

                    var invalidChars = Path.GetInvalidPathChars();
                    if (relativePath.IndexOfAny(invalidChars) >= 0)
                    {
                        return false;
                    }

                    // 明示的なトラバーサル表現を拒否
                    var pathSegments = relativePath
                        .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (pathSegments.Any(segment => segment is "." or ".."))
                    {
                        return false;
                    }

                    var baseFullPath = Path.GetFullPath(fileName)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var candidatePath = Path.GetFullPath(Path.Combine(baseFullPath, relativePath));

                    var comparison = OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                    var baseWithSeparator = baseFullPath + Path.DirectorySeparatorChar;
                    var isUnderBase =
                        candidatePath.Equals(baseFullPath, comparison) ||
                        candidatePath.StartsWith(baseWithSeparator, comparison);

                    if (!isUnderBase)
                    {
                        return false;
                    }

                    combinedPath = candidatePath;
                    return true;
                }
                catch (Exception)
                {
                    combinedPath = string.Empty;
                    return false;
                }
            }
        }


        /// <summary>
        /// ディレクトリ・フォルダ名として許可されない文字
        /// </summary>
        private static readonly char[] InvalidFolderNameChars =
        [
            '/','\0','<', '>', ':', '"', '|', '?', '*'
        ];

        /// <summary>
        /// ディレクトリ・フォルダ名として許可されない文字
        /// </summary>
        private static readonly char[] InvalidFileNameChars =
        [
            '/','\0','<', '>', ':', '"', '|', '?', '*', '\\'
        ];


        /// <summary>
        /// 指定されたパスが有効な絶対パスかどうかをチェック(Windows用)
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool IsValidWindowsAbsolutePath(this string path)
        {
            // パスが空またはnullの場合は無効
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // パスが絶対パスかどうかをチェック
            if (!Path.IsPathRooted(path))
            {
                return false;
            }

            // パスのルートドライブを取得
            var root = Path.GetPathRoot(path);

            // UNCパスの場合のチェック
            if (path.StartsWith(@"\\"))
            {
                return IsValidUncPath(path);
            }

            // 現在のパソコンに存在するドライブを取得
            var validDrives = Directory.GetLogicalDrives();

            // ドライブが有効かどうかをチェック
            if (!validDrives.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }



            // パスの形式をチェック
            try
            {
                var fullPath = Path.GetFullPath(path);

                // 入力パスとフルパスの一致を確認（相対パス表現が無いか確認、ディレクトリトラバーサル攻撃対策）
                if (!string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // ドライブ部分とフォルダ部分を分ける
                root = Path.GetPathRoot(fullPath);
                var directories = fullPath[root!.Length..].Split(Path.DirectorySeparatorChar);

                // フォルダ名に無効な文字が含まれているかをチェック
                foreach (var dir in directories)
                {
                    if (dir.Any(ch => InvalidFolderNameChars.Contains(ch) || Path.GetInvalidPathChars().Contains(ch)))
                    {
                        return false;
                    }
                }

                // 指定されたドライブが存在するかをチェック
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                // 例外が発生した場合は無効
                return false;
            }
        }


        /// <summary>
        /// 指定されたパスが有効な絶対パスかどうかをチェック(Linux用)
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool IsValidUnixAbsolutePath(this string path)
        {
            // パスが空またはnullの場合は無効
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // パスが絶対パスかどうかをチェック
            if (!Path.IsPathRooted(path))
            {
                return false;
            }

            // パスの形式をチェック
            try
            {
                var fullPath = Path.GetFullPath(path);

                // ルートパスが正しいかをチェック
                if (!fullPath.StartsWith("/"))
                {
                    return false;
                }

                // 入力パスとフルパスの一致を確認（相対パス表現が無いか確認、ディレクトリトラバーサル攻撃対策）
                if (!string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 各ディレクトリ名に無効な文字が含まれているかをチェック
                var directories = fullPath.TrimEnd('/').Substring(1).Split('/');
                foreach (var dir in directories)
                {
                    if (string.IsNullOrEmpty(dir) || dir.ContainsAny(InvalidFolderNameChars))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                // 例外が発生した場合は無効
                return false;
            }
        }



        /// <summary>
        /// 指定されたパスが有効なUNCパスかどうかをチェック
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool IsValidUncPath(string path)
        {
            // UNCパスの形式をチェック
            try
            {
                var uri = new Uri(path);
                if (!uri.IsUnc)
                {
                    return false;
                }

                // UNCパスのサーバー部分と共有フォルダ部分を分ける
                var uncPathWithoutPrefix = path.Substring(2); // 最初の "\\"
                var parts = uncPathWithoutPrefix.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    return false;
                }

                // フォルダ名に無効な文字が含まれているかをチェック
                for (var i = 1; i < parts.Length; i++)
                {
                    if (parts[i].Any(ch => InvalidFolderNameChars.Contains(ch) || Path.GetInvalidPathChars().Contains(ch)))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                // 例外が発生した場合は無効
                return false;
            }
        }
    }
}
