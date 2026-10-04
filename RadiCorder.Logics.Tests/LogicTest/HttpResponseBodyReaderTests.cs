using System.Text;
using RadiCorder.Logics.Application;

namespace RadiCorder.Logics.Tests.LogicTest;

public class HttpResponseBodyReaderTests
{
    [Test]
    public async Task 長さが不明な本文でもサイズ上限を守る()
    {
        using var content = new StreamContent(new UnknownLengthStream(new byte[9000]));
        Assert.That(content.Headers.ContentLength, Is.Null);
        Assert.That(async () => await HttpResponseBodyReader.ReadBytesAsync(content, 8192), Throws.InstanceOf<InvalidDataException>());
        using var normal = new StringContent("日本語の本文");
        Assert.That(await HttpResponseBodyReader.ReadStringAsync(normal, 1024), Is.EqualTo("日本語の本文"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task 本文が停止しても期限または呼び出し元のキャンセルで終了する(bool cancelCaller)
    {
        using var content = new StreamContent(new StalledStream());
        using var cts = new CancellationTokenSource();
        if (cancelCaller) cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var task = HttpResponseBodyReader.ReadBytesAsync(content, 1024, cts.Token, TimeSpan.FromMilliseconds(100));
        if (cancelCaller) Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        else Assert.That(async () => await task, Throws.InstanceOf<TimeoutException>());
    }

    [Test]
    public void XMLの外部実体を読み込まない()
    {
        using var content = new StringContent("<!DOCTYPE x [<!ENTITY data SYSTEM 'file:///fixture/private'>]><x>&data;</x>");
        Assert.That(async () => await HttpResponseBodyReader.ReadXmlAsync(content, 1024), Throws.InstanceOf<System.Xml.XmlException>());
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
