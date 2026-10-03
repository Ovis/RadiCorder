using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Services
{
    public partial class AppConfigurationService : IAppConfigurationService
    {
        private readonly ILogger<AppConfigurationService> _logger;

        private readonly IServiceProvider _serviceProvider;
        private readonly IDataProtector _dataProtector;
        private readonly object _lock = new();

        public RadikoOptions RadikoOptions { get; }
        private StorageOptions StorageOptions { get; }
        private ExternalServiceOptions ExternalServiceOptions { get; }
        private MonitoringOptions MonitoringOptions { get; }
        private AutomationOptions AutomationOptions { get; }
        private ReleaseOptions ReleaseOptions { get; }

        public string FfmpegExecutablePath => StorageOptions.FfmpegExecutablePath;
        public int LogRetentionDays { get; private set; }
        public int StorageLowSpaceCheckIntervalMinutes { get; private set; }
        public int StorageLowSpaceNotificationCooldownHours { get; private set; }
        public bool ClockSkewMonitoringEnabled { get; private set; }
        public int ClockSkewCheckIntervalHours { get; private set; }
        public int ClockSkewThresholdSeconds { get; private set; }
        public string ClockSkewNtpServer { get; private set; } = string.Empty;
        public int RadiruApiMinRequestIntervalMs { get; private set; }
        public int RadiruApiRequestJitterMs { get; private set; }
        public string ReleaseCheckGitHubOwner => ReleaseOptions.ReleaseCheckGitHubOwner;
        public string ReleaseCheckGitHubRepository => ReleaseOptions.ReleaseCheckGitHubRepository;

        public bool IsRadikoPremiumUser { get; private set; }
        public bool IsRadikoAreaFree { get; private set; }
        public bool HasRadikoCredentials { get; private set; }

        public string RecordFileSaveDir { get; private set; }

        public string TemporaryFileSaveDir { get; private set; }

        public string? RecordDirectoryRelativePath { get; private set; }
        public string? RecordFileNameTemplate { get; private set; }

        public TimeSpan RecordStartDuration { get; private set; }

        public TimeSpan RecordEndDuration { get; private set; }

        public string RadiruArea { get; private set; } = string.Empty;
        public string ExternalServiceUserAgent { get; private set; } = string.Empty;

        public string? DiscordWebhookUrl { get; private set; }
        public string ExternalImportFileTimeZoneId { get; private set; } = JapanTimeZone.Resolve().Id;
        public int StorageLowSpaceThresholdMb { get; private set; }
        public bool MergeTagsFromAllMatchedKeywordRules { get; private set; }
        public bool EmbedProgramImageOnRecord { get; private set; }
        public bool ResumePlaybackAcrossPages { get; private set; }
        public int ReleaseCheckIntervalDays { get; private set; }
        public int DuplicateDetectionIntervalDays { get; private set; }
        public int DuplicateDetectionScheduleDayOfWeek { get; private set; }
        public int DuplicateDetectionScheduleHour { get; private set; }
        public int DuplicateDetectionScheduleMinute { get; private set; }
        public List<NoticeCategory> NoticeCategories { get; private set; } = [];
        public List<NoticeCategory> UnreadBadgeNoticeCategories { get; private set; } = [];

        public ConcurrentDictionary<string, string> RadikoStationDic { get; private set; } = new();

        public IMemoryCache Cache { get; set; } =
            new MemoryCache(new MemoryCacheOptions
            {
                ExpirationScanFrequency = TimeSpan.FromSeconds(60),
            });

        public TimeSpan AbsoluteExpirationRelativeToNow { get; set; } = TimeSpan.FromDays(1);


        public AppConfigurationService(
            ILogger<AppConfigurationService> logger,
            IServiceProvider serviceProvider,
            IOptionsMonitor<RadikoOptions> radikoOptions,
            IOptionsMonitor<StorageOptions> storageOptions,
            IOptionsMonitor<ExternalServiceOptions> externalServiceOptions,
            IOptionsMonitor<MonitoringOptions> monitoringOptions,
            IOptionsMonitor<AutomationOptions> automationOptions,
            IOptionsMonitor<ReleaseOptions> releaseOptions,
            IDataProtectionProvider dataProtectionProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            RadikoOptions = radikoOptions.CurrentValue;
            StorageOptions = storageOptions.CurrentValue;
            ExternalServiceOptions = externalServiceOptions.CurrentValue;
            MonitoringOptions = monitoringOptions.CurrentValue;
            AutomationOptions = automationOptions.CurrentValue;
            ReleaseOptions = releaseOptions.CurrentValue;

            _dataProtector = dataProtectionProvider.CreateProtector("RadiCorder.RadikoCredentials.v1");

            var configuredRecordDir = NormalizeStorageDirectory(
                StorageOptions.RecordFileSaveFolder,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "record"));
            var configuredTempDir = NormalizeStorageDirectory(
                StorageOptions.TemporaryFileSaveFolder,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp"));

            // 録音ファイル保存フォルダの設定
            {
                if (!Directory.Exists(configuredRecordDir))
                {
                    logger.ZLogDebug($"録音ファイル保存フォルダ{configuredRecordDir} が存在しないため作成");
                    var recordFileSaveDir = configuredRecordDir;
                    logger.ZLogDebug($"録音ファイル保存フォルダパス：{recordFileSaveDir}");
                    Directory.CreateDirectory(recordFileSaveDir);

                    RecordFileSaveDir = recordFileSaveDir;
                }
                else
                {
                    logger.ZLogDebug($"録音ファイル保存フォルダ{configuredRecordDir} が存在するため保存フォルダとして採用");
                    RecordFileSaveDir = configuredRecordDir;
                }
            }

            // 一時ファイル保存フォルダの設定
            {
                if (Directory.Exists(configuredTempDir))
                {
                    logger.ZLogDebug($"一時フォルダ{configuredTempDir} が存在するため利用する");
                    TemporaryFileSaveDir = configuredTempDir;
                }
                else
                {
                    var temporaryFileSaveFolder = configuredTempDir;
                    logger.ZLogDebug($"一時フォルダ{temporaryFileSaveFolder}を作成し利用する");

                    TemporaryFileSaveDir = temporaryFileSaveFolder;

                    try
                    {
                        Directory.CreateDirectory(temporaryFileSaveFolder);
                    }
                    catch (Exception e)
                    {
                        logger.ZLogError(e, $"一時フォルダ{temporaryFileSaveFolder}の作成に失敗");

                        throw new DomainException("一時フォルダ作成に失敗しました。");
                    }
                }
            }

            // DBから必要な設定値を取得
            InitializeSettings();
        }

        private static string NormalizeStorageDirectory(string? configuredPath, string fallbackPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return fallbackPath;
            }

            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath);
        }


        /// <summary>
        /// 放送局名の取得
        /// </summary>
        /// <param name="kind"></param>
        /// <param name="stationId"></param>
        /// <returns></returns>
        public string ChooseStationName(RadioServiceKind kind, string stationId)
        {
            return kind switch
            {
                RadioServiceKind.Radiko => ResolveRadikoStationName(stationId),
                RadioServiceKind.Radiru => ResolveRadiruStationName(stationId),
                _ => throw new DomainException("未対応のサービス種別です。")
            };
        }

        private string ResolveRadikoStationName(string stationId)
        {
            if (RadikoStationDic.TryGetValue(stationId, out var stationName))
            {
                return stationName;
            }

            _logger.ZLogWarning($"radiko局名を解決できませんでした。 stationId={stationId}");
            return $"不明局({stationId})";
        }

        private static string ResolveRadiruStationName(string stationId)
        {
            var station = Enumeration.GetAll<RadiruStationKind>()
                .FirstOrDefault(r => r.ServiceId == stationId);

            return station?.Name ?? $"不明局({stationId})";
        }

        /// <summary>
        /// radikoの放送局情報キャッシュを更新
        /// </summary>
        /// <param name="stationList"></param>
        /// <returns></returns>
        public void UpdateRadikoStationDic(List<RadikoStation> stationList)
        {
            lock (_lock)
            {
                var normalizedStations = stationList
                    .Where(station => !string.IsNullOrWhiteSpace(station.StationId))
                    .GroupBy(station => station.StationId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();

                var newMap = new ConcurrentDictionary<string, string>(
                    normalizedStations.Select(station => new KeyValuePair<string, string>(station.StationId, station.StationName)),
                    StringComparer.OrdinalIgnoreCase);

                RadikoStationDic = newMap;

            }
        }

        private IServiceScope CreateDbContextScope(out RadioDbContext context)
        {
            var scope = _serviceProvider.CreateScope();
            context = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return scope;
        }


    }
}

