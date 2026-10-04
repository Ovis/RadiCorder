using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.Domain.DuplicateDetection;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

[Platform("Linux")]
public class FfmpegProcessLifetimeTests
{
    private string _root = null!;
    private string _script = null!;
    private Mock<IAppConfigurationService> _config = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "radicorder-process-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
        _script = Path.Combine(_root, "ffmpeg");
        _config = new Mock<IAppConfigurationService>();
        _config.SetupGet(x => x.FfmpegExecutablePath).Returns(_script);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public async Task ログ保存先が使用できなくても出力を読み切って正常終了する()
    {
        File.WriteAllText(_script, "#!/bin/sh\necho output\necho error >&2\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // ディレクトリ作成には成功するが、最初の書き込みで失敗する状態。
        Directory.CreateDirectory(Path.Combine(_root, "logs", "番組.log"));
        var service = CreateService();
        Assert.That(await service.RunProcessAsync("", 5, "番組"), Is.True);
    }

    [Test]
    public async Task キャンセル時は実行中のプロセスを終了してから例外を返す()
    {
        var pidPath = Path.Combine(_root, "pid");
        File.WriteAllText(_script, $"#!/bin/sh\necho $$ > '{pidPath}'\nsleep 30\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var cts = new CancellationTokenSource();
        var task = CreateService().RunProcessAsync("", 60, cancellationToken: cts.Token).AsTask();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(pidPath)) await Task.Delay(10, deadline.Token);
        var pid = int.Parse(File.ReadAllText(pidPath));
        cts.Cancel();
        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(ProcessExists(pid), Is.False);
    }

    [Test]
    public async Task 音声指紋のキャンセルを空配列として扱わず子プロセスを終了する()
    {
        var pidPath = Path.Combine(_root, "fingerprint-pid");
        File.WriteAllText(_script, $"#!/bin/sh\necho $$ > '{pidPath}'\nsleep 30\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(_root, "input.m4a"), "fixture");
        _config.SetupGet(x => x.RecordFileSaveDir).Returns(_root);
        var reader = new RecordingAudioFingerprintReader(NullLogger.Instance, _config.Object, new ConfigurationBuilder().Build());
        using var cts = new CancellationTokenSource();
        var cache = new Dictionary<Ulid, double[]>();
        var task = reader.GetFingerprintAsync(new DuplicateRecording
        {
            RecordingId = Ulid.NewUlid(), FileRelativePath = "input.m4a", DurationSeconds = 300
        }, cache, cts.Token).AsTask();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(pidPath)) await Task.Delay(10, deadline.Token);
        var pid = int.Parse(File.ReadAllText(pidPath));
        cts.Cancel();
        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(ProcessExists(pid), Is.False);
        Assert.That(cache, Is.Empty);
    }

    private FfmpegService CreateService() => new(NullLogger<IFfmpegService>.Instance, _config.Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RadiCorder:LogDirectory"] = Path.Combine(_root, "logs")
        }).Build());

    private static bool ProcessExists(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
