using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class AppConfigurationServiceTests
{
    private string _baseDir = string.Empty;
    private string _dbPath = string.Empty;
    private string _keyDir = string.Empty;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appconfig-tests", Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_baseDir, "appconfig-test.db");
        _keyDir = Path.Combine(_baseDir, "keys");

        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_keyDir);

        var services = new ServiceCollection();
        services.AddDbContext<RadioDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        try
        {
            if (Directory.Exists(_baseDir))
            {
                Directory.Delete(_baseDir, true);
            }
        }
        catch (IOException)
        {
            // ファイルロックが残っている場合は削除をスキップする
        }
    }

    private AppConfigurationService CreateService()
    {
        var logger = new Mock<ILogger<AppConfigurationService>>().Object;
        var radikoOptions = new RadikoOptions();
        var storageOptions = new StorageOptions
        {
            RecordFileSaveFolder = Path.Combine(_baseDir, "record"),
            TemporaryFileSaveFolder = Path.Combine(_baseDir, "temp")
        };
        var externalServiceOptions = new ExternalServiceOptions();
        var monitoringOptions = new MonitoringOptions();
        var automationOptions = new AutomationOptions();
        var releaseOptions = new ReleaseOptions();

        var radikoOptionsMonitor = new Mock<IOptionsMonitor<RadikoOptions>>();
        radikoOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(radikoOptions);

        var storageOptionsMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        storageOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(storageOptions);
        var externalServiceOptionsMonitor = new Mock<IOptionsMonitor<ExternalServiceOptions>>();
        externalServiceOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(externalServiceOptions);
        var monitoringOptionsMonitor = new Mock<IOptionsMonitor<MonitoringOptions>>();
        monitoringOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(monitoringOptions);
        var automationOptionsMonitor = new Mock<IOptionsMonitor<AutomationOptions>>();
        automationOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(automationOptions);
        var releaseOptionsMonitor = new Mock<IOptionsMonitor<ReleaseOptions>>();
        releaseOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(releaseOptions);

        var dataProtectionProvider = new FakeDataProtectionProvider();

        return new AppConfigurationService(
            logger,
            _provider,
            radikoOptionsMonitor.Object,
            storageOptionsMonitor.Object,
            externalServiceOptionsMonitor.Object,
            monitoringOptionsMonitor.Object,
            automationOptionsMonitor.Object,
            releaseOptionsMonitor.Object,
            dataProtectionProvider);
    }

    [Test]
    public async Task UpdateRadikoCredentialsAsync_暗号化して保存される()
    {
        var service = CreateService();

        await service.UpdateRadikoCredentialsAsync("user@example.com", "password123");

        Assert.That(service.RadikoOptions.RadikoUserId, Is.EqualTo("user@example.com"));
        Assert.That(service.RadikoOptions.RadikoPassword, Is.EqualTo(string.Empty));
        Assert.That(service.HasRadikoCredentials, Is.True);

        var (isSuccess, userIdFromStore, passwordFromStore) = await service.TryGetRadikoCredentialsAsync();
        Assert.That(isSuccess, Is.True);
        Assert.That(userIdFromStore, Is.EqualTo("user@example.com"));
        Assert.That(passwordFromStore, Is.EqualTo("password123"));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        var protectedUserId = db.AppConfigurations.Single(r => r.ConfigurationName == AppConfigurationNames.RadikoUserIdProtected).Val1;
        var protectedPassword = db.AppConfigurations.Single(r => r.ConfigurationName == AppConfigurationNames.RadikoPasswordProtected).Val1;

        Assert.That(protectedUserId, Is.Not.EqualTo("user@example.com"));
        Assert.That(string.IsNullOrEmpty(protectedUserId), Is.False);
        Assert.That(protectedPassword, Is.Not.EqualTo("password123"));
        Assert.That(string.IsNullOrEmpty(protectedPassword), Is.False);
    }

    [Test]
    public async Task InitializeSettings_暗号化情報を復号できる()
    {
        var service = CreateService();
        await service.UpdateRadikoCredentialsAsync("user@example.com", "password123");

        var service2 = CreateService();

        Assert.That(service2.RadikoOptions.RadikoUserId, Is.EqualTo("user@example.com"));
        Assert.That(service2.RadikoOptions.RadikoPassword, Is.EqualTo(string.Empty));
        Assert.That(service2.HasRadikoCredentials, Is.True);

        var (isSuccess, userIdFromStore, passwordFromStore) = await service2.TryGetRadikoCredentialsAsync();
        Assert.That(isSuccess, Is.True);
        Assert.That(userIdFromStore, Is.EqualTo("user@example.com"));
        Assert.That(passwordFromStore, Is.EqualTo("password123"));
    }

    [Test]
    public async Task ClearRadikoCredentialsAsync_情報が消える()
    {
        var service = CreateService();
        await service.UpdateRadikoCredentialsAsync("user@example.com", "password123");
        service.UpdateRadikoPremiumUser(true);
        service.UpdateRadikoAreaFree(true);

        await service.ClearRadikoCredentialsAsync();

        Assert.That(service.RadikoOptions.RadikoUserId, Is.EqualTo(string.Empty));
        Assert.That(service.RadikoOptions.RadikoPassword, Is.EqualTo(string.Empty));
        Assert.That(service.IsRadikoPremiumUser, Is.False);
        Assert.That(service.IsRadikoAreaFree, Is.False);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        var userId = db.AppConfigurations.Single(r => r.ConfigurationName == AppConfigurationNames.RadikoUserIdProtected).Val1;
        var protectedPassword = db.AppConfigurations.Single(r => r.ConfigurationName == AppConfigurationNames.RadikoPasswordProtected).Val1;

        Assert.That(userId, Is.EqualTo(string.Empty));
        Assert.That(protectedPassword, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task UpdateRecordSettings_設定値が更新される()
    {
        var service = CreateService();

        await service.UpdateRecordDirectoryPathAsync(@"Folder\Path");
        await service.UpdateRecordFileNameTemplateAsync("$Title$_$SYYYY$");
        await service.UpdateDurationAsync(10, 20);

        Assert.That(service.RecordDirectoryRelativePath, Is.EqualTo(@"Folder\Path"));
        Assert.That(service.RecordFileNameTemplate, Is.EqualTo("$Title$_$SYYYY$"));
        Assert.That(service.RecordStartDuration.TotalSeconds, Is.EqualTo(10));
        Assert.That(service.RecordEndDuration.TotalSeconds, Is.EqualTo(20));
    }

    [Test]
    public async Task UpdateRadiruAreaAndNotice_設定値が更新される()
    {
        var service = CreateService();

        await service.UpdateRadiruAreaAsync("130");
        await service.UpdateNoticeSettingAsync(
            "https://example.com/webhook",
            [(int)RadiCorder.Logics.Logics.NotificationLogic.NoticeCategory.RecordingSuccess,
             (int)RadiCorder.Logics.Logics.NotificationLogic.NoticeCategory.RecordingError]);
        await service.UpdateUnreadBadgeNoticeCategoriesAsync([(int)RadiCorder.Logics.Logics.NotificationLogic.NoticeCategory.SystemError]);

        Assert.That(service.RadiruArea, Is.EqualTo("130"));
        Assert.That(service.DiscordWebhookUrl, Is.EqualTo("https://example.com/webhook"));
        Assert.That(service.NoticeCategories.Count, Is.EqualTo(2));
        Assert.That(service.UnreadBadgeNoticeCategories, Is.EqualTo([RadiCorder.Logics.Logics.NotificationLogic.NoticeCategory.SystemError]));
    }

    [Test]
    public void ChooseStationName_Radiru未知StationIdはフォールバック名を返す()
    {
        var service = CreateService();

        var stationName = service.ChooseStationName(RadioServiceKind.Radiru, "am");

        Assert.That(stationName, Is.EqualTo("不明局(am)"));
    }

    [Test]
    public void ChooseStationName_Radiko未知StationIdはフォールバック名を返す()
    {
        var service = CreateService();

        var stationName = service.ChooseStationName(RadioServiceKind.Radiko, "MAJAL");

        Assert.That(stationName, Is.EqualTo("不明局(MAJAL)"));
    }
}
