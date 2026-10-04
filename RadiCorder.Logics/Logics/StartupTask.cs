using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics
{
    public class StartupTask(
        ILogger<StartupTask> logger,
        IAppConfigurationService config,
        IFfmpegService ffmpegService,
        RadikoUniqueProcessLogic radikoLogic,
        ProgramScheduleLobLogic programScheduleLogic,
        LogMaintenanceLobLogic logMaintenanceLobLogic,
        TemporaryStorageMaintenanceLobLogic temporaryStorageMaintenanceLobLogic,
        StorageCapacityMonitorLobLogic storageCapacityMonitorLobLogic,
        StationLobLogic stationLobLogic,
        NotificationLobLogic notificationLobLogic,
        IServiceScopeFactory? serviceScopeFactory = null)
    {
        public async Task InitializeAsync()
        {
            try
            {
                // FFmpeg調整
                if (!ffmpegService.Initialize())
                {
                    await notificationLobLogic.SetNotificationAsync(
                        logLevel: LogLevel.Error,
                        category: NoticeCategory.SystemError,
                        message: "起動処理でエラーが発生しました。ffmpegがインストールされていません。"
                    );

                    throw new DomainException("FFmpegがインストールされていません。");
                }

                // radikoログイン処理 
                {
                    try
                    {
                        await radikoLogic.LoginRadikoAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.ZLogWarning(ex, $"radikoログインに失敗しました。他の機能の起動を継続します。");
                        await notificationLobLogic.SetNotificationAsync(LogLevel.Warning, NoticeCategory.SystemError, "radikoログインに失敗しました。接続回復後に再試行します。");
                    }
                }

                // 放送局情報の初期化
                {
                    // radiko
                    {
                        try
                        {
                            // radikoの放送局情報を同期
                            await stationLobLogic.UpsertRadikoStationDefinitionAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.ZLogWarning(ex, $"radikoの放送局情報同期に失敗しました。既存データで継続します。");
                            await notificationLobLogic.SetNotificationAsync(
                                logLevel: LogLevel.Warning,
                                category: NoticeCategory.SystemError,
                                message: "radikoの放送局情報同期に失敗しました。既存データで継続します。");
                        }

                        try
                        {
                            // radikoの放送局データをキャッシュ
                            var radikoStationList = await stationLobLogic.GetAllRadikoStationAsync(activeOnly: false);
                            config.UpdateRadikoStationDic(radikoStationList);
                        }
                        catch (Exception ex)
                        {
                            logger.ZLogWarning(ex, $"radikoの放送局キャッシュ更新に失敗しました。");
                        }
                    }


                    // らじる★らじる
                    {
                        if (!await stationLobLogic.CheckInitializedRadiruRadiruStationAsync())
                        {
                            // らじる★らじるの放送局情報を初期化
                            try
                            {
                                await stationLobLogic.UpdateRadiruStationInformationAsync();
                            }
                            catch (Exception ex)
                            {
                                logger.ZLogWarning(ex, $"らじる★らじる初期取得に失敗しました。他の機能の起動を継続します。");
                                await notificationLobLogic.SetNotificationAsync(LogLevel.Warning, NoticeCategory.SystemError, "らじる★らじる初期取得に失敗しました。接続回復後に再試行します。");
                            }
                        }
                    }
                }

                // 番組表更新関係
                {
                    // 24時間以内に番組表更新が行われていない場合のみ、起動時に即時更新を実行する。
                    if (await programScheduleLogic.HasProgramScheduleBeenUpdatedWithin24Hours() is false)
                    {
                        if (serviceScopeFactory != null)
                        {
                            // 起動をブロックしないため、更新はバックグラウンドで開始する。
                            // 専用スコープを作成して破棄済み DbContext 参照を防ぐ。
                            _ = Task.Run(
                                async () =>
                                {
                                    try
                                    {
                                        using var scope = serviceScopeFactory.CreateScope();
                                        var scopedRunner = scope.ServiceProvider.GetRequiredService<ProgramUpdateRunner>();
                                        await scopedRunner.ExecuteAsync("startup");
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.ZLogError(ex, $"起動時の番組表更新バックグラウンド実行でエラーが発生しました。");
                                    }
                                });
                        }
                    }
                }

                // ログメンテナンスを起動時に1回実行
                await logMaintenanceLobLogic.CleanupOldLogFilesAsync(config.LogRetentionDays);
                await temporaryStorageMaintenanceLobLogic.CleanupAsync();

                // ストレージ空き容量監視を起動時に1回実行
                await storageCapacityMonitorLobLogic.CheckAndNotifyLowDiskSpaceAsync();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"起動処理でエラー発生");

                await notificationLobLogic.SetNotificationAsync(
                    logLevel: LogLevel.Error,
                    category: NoticeCategory.SystemError,
                    message: "起動処理でエラーが発生しました。ログを確認してください。"
                );

                throw;
            }
        }
    }
}
