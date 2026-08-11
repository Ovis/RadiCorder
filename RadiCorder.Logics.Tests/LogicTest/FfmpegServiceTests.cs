using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class FfmpegServiceTests
{
    private string _ffmpegDir = string.Empty;
    private string _ffmpegExe = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        _ffmpegExe = Path.Combine(_ffmpegDir, "ffmpeg.exe");

        if (Directory.Exists(_ffmpegDir))
        {
            Directory.Delete(_ffmpegDir, true);
        }
        Directory.CreateDirectory(_ffmpegDir);
        File.WriteAllText(_ffmpegExe, string.Empty);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_ffmpegDir))
        {
            Directory.Delete(_ffmpegDir, true);
        }
    }

    [Test]
    public void Initialize_カレント配置のffmpegがあればtrue()
    {
        var logger = new Mock<ILogger<IFfmpegService>>().Object;
        var appConfig = new Mock<IAppConfigurationService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RadiCorder:LogDirectory"] = Path.Combine(Path.GetTempPath(), "radicorder-tests", "logs")
            })
            .Build();
        appConfig.SetupGet(x => x.FfmpegExecutablePath).Returns(string.Empty);
        var service = new FfmpegService(logger, appConfig.Object, configuration);

        var result = service.Initialize();

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RunProcessAsync_無効なffmpegでもfalse()
    {
        var logger = new Mock<ILogger<IFfmpegService>>().Object;
        var appConfig = new Mock<IAppConfigurationService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RadiCorder:LogDirectory"] = Path.Combine(Path.GetTempPath(), "radicorder-tests", "logs")
            })
            .Build();
        appConfig.SetupGet(x => x.FfmpegExecutablePath).Returns(string.Empty);
        var service = new FfmpegService(logger, appConfig.Object, configuration);

        var result = await service.RunProcessAsync("-version", timeoutSeconds: 1);

        Assert.That(result, Is.False);
    }
}
