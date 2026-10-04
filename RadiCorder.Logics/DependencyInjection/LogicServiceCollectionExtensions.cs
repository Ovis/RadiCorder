using RadiCorder.Logics.Services.Streaming;
using System.Net;
using RadiCorder.Logics.ApiClients;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Domain.Station;
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
using RadiCorder.Logics.Services;
using RadiCorder.Logics.UseCases.Recording;

using Microsoft.Extensions.DependencyInjection;

namespace RadiCorder.Logics.DependencyInjection;

/// <summary>
/// 業務ロジックの共通DI設定。Web固有の通知実装は呼び出し側で登録する。
/// </summary>
public static class LogicServiceCollectionExtensions
{
    /// <summary>
    /// Web に依存しない業務サービスとバックグラウンドサービスを登録する。
    /// </summary>
    public static IServiceCollection AddRadiCorderLogics(this IServiceCollection services)
    {
        return services
            .AddHttpClients()
            .AddMappings()
            .AddApiClients()
            .AddRecordingServices()
            .AddProgramScheduleServices()
            .AddReserveServices()
            .AddNotificationServices()
            .AddPlaybackServices()
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

    private static IServiceCollection AddRecordingServices(this IServiceCollection services)
    {
        services.AddScoped<RecordingOrchestrator>();
        services.AddScoped<IRecordingSource, RadikoRecordingSource>();
        services.AddScoped<IRecordingSource, RadiruRecordingSource>();
        services.AddScoped<IMediaStorageService, MediaStorageService>();
        services.AddSingleton<RecordingFinalizationJournal>();
        services.AddScoped<RecordingFinalizationRecovery>();
        services.AddScoped<IMediaTranscodeService, MediaTranscodeService>();
        services.AddScoped<IRecordingRepository, RecordingRepository>();
        services.AddScoped<RecordingLobLogic>();
        services.AddTransient<IFfmpegService, FfmpegService>();

        return services;
    }

    private static IServiceCollection AddProgramScheduleServices(this IServiceCollection services)
    {
        services.AddSingleton<IProgramUpdateStatusService, ProgramUpdateStatusService>();
        services.AddScoped<IRadioAppContext, RadioAppContext>();
        services.AddScoped<IStationRepository, StationRepository>();
        services.AddScoped<IProgramScheduleRepository, ProgramScheduleRepository>();
        services.AddScoped<StationLobLogic>();
        services.AddScoped<ProgramScheduleLobLogic>();
        services.AddScoped<ProgramSearchService>();
        services.AddScoped<ProgramUpdateRunner>();
        services.AddScoped<RecordedProgramQueryService>();
        services.AddScoped<RecordedProgramMediaService>();
        services.AddScoped<RecordedProgramDuplicateDetectionService>();
        services.AddScoped<RecordedDuplicateDetectionLobLogic>();
        services.AddScoped<RecordedRadioLobLogic>();
        services.AddScoped<ExternalRecordingImportLobLogic>();
        services.AddScoped<RecordingFileMaintenanceLobLogic>();
        services.AddScoped<LogMaintenanceLobLogic>();
        services.AddScoped<TemporaryStorageMaintenanceLobLogic>();
        services.AddScoped<StorageCapacityMonitorLobLogic>();
        services.AddScoped<ClockSkewMonitorLobLogic>();
        services.AddScoped<ReleaseCheckLobLogic>();
        services.AddScoped<RecordJobLobLogic>();
        services.AddScoped<RecordingJobExecutor>();
        services.AddScoped<RadikoUniqueProcessLogic>();

        return services;
    }

    private static IServiceCollection AddReserveServices(this IServiceCollection services)
    {
        services.AddScoped<IReserveRepository, ReserveRepository>();
        services.AddScoped<ReserveLobLogic>();
        services.AddScoped<TagLobLogic>();
        return services;
    }

    private static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<NotificationLobLogic>();
        return services;
    }

    private static IServiceCollection AddPlaybackServices(this IServiceCollection services)
    {
        services.AddScoped<PlayProgramLobLogic>();
        services.AddScoped<RadikoPlaylistClient>();
        return services;
    }

    private static IServiceCollection AddStartupTasks(this IServiceCollection services)
    {
        services.AddScoped<StartupTask>();
        return services;
    }

    public static IServiceCollection AddRadiCorderBackgroundServices(this IServiceCollection services)
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
}
