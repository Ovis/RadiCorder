namespace RadiCorder.Logics.Models;

public struct RadioFileInfo(string fileName, string fullPath, string relativePath)
{
    /// <summary>
    /// ファイル名（拡張子なし）
    /// </summary>
    public string FileName { get; set; } = fileName;

    /// <summary>
    /// ファイル拡張子
    /// </summary>
    public string FileExtension => "m4a";

    /// <summary>
    /// ファイル名（拡張子あり）
    /// </summary>
    public string FileFullName => $"{FileName}.{FileExtension}";

    /// <summary>
    /// ファイルが保存されているディレクトリのフルパス
    /// </summary>
    public string DirectoryFullPath { get; set; } = fullPath;

    public string DirectoryRelativePath { get; set; } = relativePath;

    /// <summary>
    /// ファイルのフルパス
    /// </summary>
    public string FileFullPath => Path.Combine(DirectoryFullPath, FileFullName);

    public string FileRelativePath => Path.Combine(DirectoryRelativePath, FileFullName);
}