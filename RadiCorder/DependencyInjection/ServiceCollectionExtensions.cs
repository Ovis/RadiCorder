using System;
using System.Net;
using Microsoft.EntityFrameworkCore;
using RadiCorder.Application;
using RadiCorder.Hubs;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.ApiClients;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Infrastructure.Notification;
using RadiCorder.Logics.Infrastructure.ProgramSchedule;
using RadiCorder.Logics.Infrastructure.Reserve;
using RadiCorder.Logics.Infrastructure.Station;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.PlayProgramLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Logics.RecordingLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.UseCases.Recording;
using ZLogger;
using ZLogger.Providers;

namespace RadiCorder.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private static string ResolvePathFromConfigOrDefault(IConfiguration config, string key, string defaultPath)
    {
        var configured = config[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaultPath;
        }

        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
    }

    public static IServiceCollection AddDbDi(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var defaultDbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"db");
        var dbFilePath = ResolvePathFromConfigOrDefault(config, "RadiCorder:DbDirectory", defaultDbDir);
        if (!Directory.Exists(dbFilePath))
        {
            Directory.CreateDirectory(dbFilePath);
        }

        var enableEfSensitiveDataLogging = environment.IsDevelopment() &&
                                           config.GetValue<bool>("Logging:EnableEfSensitiveDataLogging");

        //DBの設定
        services.AddDbContext<RadioDbContext>(options =>
        {
            if (enableEfSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }
            options.UseSqlite($"Data Source={Path.Combine(dbFilePath, "radio.db")}");
        });

        return services;
    }


    public static IServiceCollection AddConfigDi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RadikoOptions>(config.GetSection(@"RadikoOptions"));
        services.Configure<StorageOptions>(config.GetSection(@"RadiCorder"));
        services.Configure<ExternalServiceOptions>(config.GetSection(@"ExternalServiceOptions"));
        services.Configure<MonitoringOptions>(config.GetSection(@"MonitoringOptions"));
        services.Configure<AutomationOptions>(config.GetSection(@"AutomationOptions"));
        services.Configure<ReleaseOptions>(config.GetSection(@"ReleaseOptions"));

        services.AddSingleton<IAppConfigurationService, AppConfigurationService>();
        services.AddSingleton<ILocalApplicationUrlService, LocalApplicationUrlService>();
        services.AddSingleton<IRadikoProxyTicketService, RadikoProxyTicketService>();

        return services;
    }


    /// <summary>
    /// LoggingのDI設定
    /// </summary>
    /// <param name="logging"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    public static ILoggingBuilder AddLoggingDi(this ILoggingBuilder logging, IConfiguration config)
    {
        var defaultLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        var logDir = ResolvePathFromConfigOrDefault(config, "RadiCorder:LogDirectory", defaultLogDir);
        Directory.CreateDirectory(logDir);

        logging
            .ClearProviders()
            .AddZLoggerConsole(options =>
            {
                options.IncludeScopes = true;
                options.UsePlainTextFormatter(formatter =>
                {
                    formatter.SetPrefixFormatter($"{0}|{1}|", (in MessageTemplate template, in LogInfo info) => template.Format(info.Timestamp, info.LogLevel));
                    formatter.SetExceptionFormatter((writer, ex) => Utf8StringInterpolation.Utf8String.Format(writer, $"{ex}"));
                });
            })
            .AddZLoggerRollingFile(options =>
            {
                options.RollingInterval = RollingInterval.Day;
                options.TimeProvider = new OverrideJapanTimeProvider();

                options.IncludeScopes = true;
                options.FilePathSelector = (timestamp, sequenceNumber) =>
                    $"{logDir}/{timestamp.ToJapanDateTime():yyyy-MM-dd}_{sequenceNumber:000}.log";

                options.UsePlainTextFormatter(formatter =>
                {
                    formatter.SetPrefixFormatter($"{0}|{1}|", (in MessageTemplate template, in LogInfo info) => template.Format(info.Timestamp, info.LogLevel));
                    formatter.SetExceptionFormatter((writer, ex) => Utf8StringInterpolation.Utf8String.Format(writer, $"{ex}"));
                });

                options.RollingSizeKB = 1024;
            });

        return logging;
    }


    public static IServiceCollection AddLogicDiCollection(this IServiceCollection services, IConfiguration config)
    {
        return services
            .AddHttpClients()
            .AddMappings()
            .AddRealtimeEventServices()
            .AddApiClients()
            .AddRecordingServices()
            .AddProgramScheduleServices()
            .AddReserveServices()
            .AddNotificationServices()
            .AddPlaybackServices()
            .AddBackgroundJobs()
            .AddStartupTasks();
    }

    private static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientNames.Radiko).ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            return handler;
        }).ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient(HttpClientNames.Radiru).ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            return handler;
        }).ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient(HttpClientNames.Webhook).ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient(HttpClientNames.GitHub).ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }

    private static IServiceCollection AddMappings(this IServiceCollection services)
    {
        services.AddScoped<IEntryMapper, EntryMapper>();
        return services;
    }

    private static IServiceCollection AddApiClients(this IServiceCollection services)
    {
        services.AddScoped<IRadikoApiClient, RadikoApiClient>();
        services.AddScoped<IRadiruApiClient, RadiruApiClient>();
        return services;
    }

    private static IServiceCollection AddRealtimeEventServices(this IServiceCollection services)
    {
        services.AddScoped<IAppToastEventPublisher, AppToastSignalRPublisher>();
        services.AddScoped<IAppOperationEventPublisher, AppOperationSignalRPublisher>();
        return services;
    }

    private static IServiceCollection AddRecordingServices(this IServiceCollection services)
    {
        services.AddScoped<RecordingOrchestrator>();
        services.AddScoped<IRecordingSource, RadikoRecordingSource>();
        services.AddScoped<IRecordingSource, RadiruRecordingSource>();
        services.AddScoped<IRecordingStateEventPublisher, RecordingStateSignalRPublisher>();
        services.AddScoped<IMediaStorageService, MediaStorageService>();
        services.AddScoped<IMediaTranscodeService, MediaTranscodeService>();
        services.AddScoped<IRecordingRepository, RecordingRepository>();
        services.AddScoped<RecordingLobLogic>();
        services.AddTransient<IFfmpegService, FfmpegService>();

        return services;
    }

    private static IServiceCollection AddProgramScheduleServices(this IServiceCollection services)
    {
        services.AddSingleton<IProgramUpdateStatusService, ProgramUpdateStatusService>();
        services.AddScoped<IProgramUpdateStatusPublisher, ProgramUpdateStatusSignalRPublisher>();
        services.AddScoped<IRadioAppContext, RadioAppContext>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IProgramScheduleRepository, ProgramScheduleRepository>();
        services.AddScoped<StationLobLogic>();
        services.AddScoped<ProgramScheduleLobLogic>();
        services.AddScoped<ProgramUpdateRunner>();
        services.AddScoped<RecordedProgramQueryService>();
        services.AddScoped<RecordedProgramMediaService>();
        services.AddScoped<RecordedProgramDuplicateDetectionService>();
        services.AddScoped<RecordedDuplicateDetectionLobLogic>();
        services.AddScoped<IRecordedDuplicateDetectionStatusPublisher, RecordedDuplicateDetectionStatusSignalRPublisher>();
        services.AddScoped<RecordedRadioLobLogic>();
        services.AddScoped<ExternalRecordingImportLobLogic>();
        services.AddScoped<RecordingFileMaintenanceLobLogic>();
        services.AddScoped<LogMaintenanceLobLogic>();
        services.AddScoped<TemporaryStorageMaintenanceLobLogic>();
        services.AddScoped<StorageCapacityMonitorLobLogic>();
        services.AddScoped<ClockSkewMonitorLobLogic>();
        services.AddScoped<ReleaseCheckLobLogic>();
        services.AddScoped<RecordJobLobLogic>();
        services.AddScoped<RadikoUniqueProcessLogic>();

        return services;
    }

    private static IServiceCollection AddReserveServices(this IServiceCollection services)
    {
        services.AddScoped<IReserveScheduleEventPublisher, ReserveScheduleSignalRPublisher>();
        services.AddScoped<IReserveRepository, ReserveRepository>();
        services.AddScoped<ReserveLobLogic>();
        services.AddScoped<TagLobLogic>();
        return services;
    }

    private static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationEventPublisher, NotificationSignalRPublisher>();
        services.AddScoped<NotificationLobLogic>();
        return services;
    }

    private static IServiceCollection AddPlaybackServices(this IServiceCollection services)
    {
        services.AddScoped<PlayProgramLobLogic>();
        return services;
    }

    private static IServiceCollection AddBackgroundJobs(this IServiceCollection services)
    {
        services.AddSingleton<IRecordingScheduleWakeup, RecordingScheduleWakeup>();
        services.AddHostedService<RecordingScheduleBackgroundService>();
        services.AddHostedService<ProgramUpdateScheduleBackgroundService>();
        services.AddHostedService<MaintenanceCleanupScheduleBackgroundService>();
        services.AddHostedService<StorageCapacityMonitorBackgroundService>();
        services.AddHostedService<ClockSkewMonitorBackgroundService>();
        services.AddHostedService<ReleaseCheckBackgroundService>();
        services.AddHostedService<DuplicateDetectionScheduleBackgroundService>();
        return services;
    }

    private static IServiceCollection AddStartupTasks(this IServiceCollection services)
    {
        services.AddScoped<StartupTask>();
        return services;
    }
}
