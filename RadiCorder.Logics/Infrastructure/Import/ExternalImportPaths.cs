namespace RadiCorder.Logics.Infrastructure.Import;

/// <summary>
/// 外部取込の管理下pathと既存fileのpathを解決する。
/// </summary>
public static class ExternalImportPaths
{
    public static bool TryResolveManagedFilePath(string relativePath, string rootPath, out string fullPath, out string normalizedRelativePath)
    {
        fullPath = string.Empty;
        normalizedRelativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var invalidChars = Path.GetInvalidPathChars();
        if (relativePath.IndexOfAny(invalidChars) >= 0)
        {
            return false;
        }

        var rawRelativePath = relativePath
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(rawRelativePath))
        {
            return false;
        }

        // ディレクトリトラバーサルを明示的に拒否
        var pathSegments = rawRelativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathSegments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(root, rawRelativePath));
        var combinedNoEnd = combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var isUnderRoot = OperatingSystem.IsWindows()
            ? combinedNoEnd.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            : combinedNoEnd.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!isUnderRoot)
        {
            return false;
        }

        if (ContainsReparsePoint(root, combined))
        {
            return false;
        }

        fullPath = combined;
        normalizedRelativePath = Path.GetRelativePath(root, combined);
        return true;
    }

    private static bool ContainsReparsePoint(string rootPath, string fullPath)
    {
        try
        {
            if (HasReparsePoint(rootPath))
            {
                return true;
            }

            var relative = Path.GetRelativePath(rootPath, fullPath);
            if (string.IsNullOrWhiteSpace(relative) ||
                relative.Equals(".", StringComparison.Ordinal) ||
                relative.Equals("..", StringComparison.Ordinal))
            {
                return false;
            }

            var current = rootPath;
            var segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                if (HasReparsePoint(current))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    public static string NormalizeExistingToAbsolutePath(string storedPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return string.Empty;
        }

        try
        {
            if (Path.IsPathRooted(storedPath))
            {
                return Path.GetFullPath(storedPath);
            }

            return Path.GetFullPath(Path.Combine(rootPath, storedPath));
        }
        catch
        {
            return string.Empty;
        }
    }
}
