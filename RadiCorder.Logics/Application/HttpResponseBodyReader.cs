using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RadiCorder.Logics.Application;

/// <summary>
/// 応答本文の期限と展開後のサイズを制限する。ヘッダー取得後にも期限を適用する。
/// </summary>
public static class HttpResponseBodyReader
{
    public const int DefinitionLimit = 2 * 1024 * 1024;
    public const int ProgramLimit = 20 * 1024 * 1024;
    public const int PlaylistLimit = 2 * 1024 * 1024;
    public const int ImageLimit = 8 * 1024 * 1024;

    public static async Task<byte[]> ReadBytesAsync(HttpContent content, int maxBytes,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (content.Headers.ContentLength > maxBytes) throw new InvalidDataException("HTTP応答本文がサイズ上限を超えています。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            await using var stream = await content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(chunk, deadline.Token);
                if (count == 0) break;
                if (buffer.Length + count > maxBytes) throw new InvalidDataException("HTTP応答本文がサイズ上限を超えています。");
                buffer.Write(chunk, 0, count);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("HTTP応答本文の読み取りが期限を超えました。");
        }
    }

    public static async Task<string> ReadStringAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(content, maxBytes, cancellationToken);
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
        using var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public static async Task<XDocument> ReadXmlAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(content, maxBytes, cancellationToken);
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = maxBytes
        });
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }
}
