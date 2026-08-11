using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.NotificationLogic
{
    public class NotificationLobLogic(
        ILogger<NotificationLobLogic> logger,
        IRadioAppContext appContext,
        IAppConfigurationService config,
        IEntryMapper entryMapper,
        IHttpClientFactory httpClientFactory,
        INotificationRepository notificationRepository,
        INotificationEventPublisher? notificationEventPublisher = null)
    {
        private HttpClient HttpClient => httpClientFactory.CreateClient(HttpClientNames.Webhook);

        /// <summary>
        /// 未読のお知らせの件数を取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<int> GetUnreadNotificationCountAsync()
        {
            logger.ZLogDebug($"未読のお知らせの件数を取得");

            try
            {
                var categories = ResolveUnreadBadgeCategories();
                return await notificationRepository.GetUnreadCountAsync(categories);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"未読お知らせ件数取得に失敗");
                throw;
            }
        }


        /// <summary>
        /// 未読のお知らせ一覧を取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<List<NotificationEntry>> GetUnreadNotificationListAsync()
        {
            logger.ZLogDebug($"未読のお知らせ一覧を取得");

            try
            {
                var categories = ResolveUnreadBadgeCategories();
                var list = await notificationRepository.GetUnreadListAsync(categories);

                await UpdateReadNotificationAsync(appContext.StandardDateTimeOffset.DateTime, categories);

                return list.Select(r => entryMapper.ToNotificationEntry(r)).ToList();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"お知らせ情報取得に失敗");
                throw;
            }
        }


        /// <summary>
        /// お知らせを既読に更新
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="categories"></param>
        /// <returns></returns>
        public async ValueTask UpdateReadNotificationAsync(
            DateTimeOffset dt,
            IEnumerable<NoticeCategory>? categories = null)
        {
            logger.ZLogDebug($"お知らせを既読に変更");

            try
            {
                await notificationRepository.MarkReadBeforeAsync(dt, categories);
                await PublishNotificationChangedSafeAsync();

            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"お知らせ情報の既読更新に失敗");
            }
        }


        /// <summary>
        /// お知らせ一覧を取得
        /// </summary>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, int Total, List<NotificationEntry>? List, Exception? Error)> GetNotificationListAsync(
            int page,
            int pageSize)
        {
            logger.ZLogDebug($"お知らせ一覧取得");

            try
            {
                var (totalRecords, notificationList) = await notificationRepository.GetPagedAsync(page, pageSize);
                return (true, totalRecords, notificationList.Select(r => entryMapper.ToNotificationEntry(r)).ToList(), null);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"録音済み番組の取得に失敗しました。");
                return (false, 0, null, e);
            }
        }


        /// <summary>
        /// お知らせ登録処理
        /// </summary>
        /// <param name="logLevel"></param>
        /// <param name="category"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public async ValueTask SetNotificationAsync(
            LogLevel logLevel,
            NoticeCategory category,
            string message)
        {
            logger.ZLogDebug($"{logLevel} {category} {message}");

            var entry = new NotificationEntry
            {
                LogLevel = logLevel.ToString(),
                Category = category,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow
            };

            try
            {
                await SetNotificationDbAsync(entry);
                await PublishNotificationChangedSafeAsync();
                await PostDiscordWebhookAsync(entry);
            }
            catch (Exception e)
            {
                // ログにだけ出力して握る
                logger.ZLogError(e, $"お知らせ登録に失敗しました。");
            }
        }


        /// <summary>
        /// お知らせの全件削除
        /// </summary>
        /// <returns></returns>
        public async ValueTask DeleteAllNotificationAsync()
        {
            try
            {
                await notificationRepository.DeleteAllAsync();
                await PublishNotificationChangedSafeAsync();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"お知らせ情報の削除に失敗しました。");
            }
        }


        /// <summary>
        /// お知らせ情報をDBに登録
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        private async ValueTask SetNotificationDbAsync(NotificationEntry entry)
        {
            var notification = entryMapper.ToNotification(entry);

            try
            {
                await notificationRepository.AddAsync(notification);
            }
            catch (Exception e)
            {
                logger.ZLogInformation($"{JsonSerializer.Serialize(notification)}");
                logger.ZLogError(e, $"お知らせ情報のDB登録に失敗しました。");
            }
        }

        /// <summary>
        /// DiscordへのWebhook通知
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        private async ValueTask PostDiscordWebhookAsync(NotificationEntry entry)
        {
            var webhookUrl = config.DiscordWebhookUrl;

            if (string.IsNullOrEmpty(webhookUrl))
                return;

            // 指定された通知カテゴリのみ通知
            if (!(config.NoticeCategories.Contains(entry.Category)))
                return;

            var content = new
            {
                content = $"【{entry.Category.GetEnumDisplayName()}】\n{entry.Message}"
            };

            try
            {
                var json = JsonSerializer.Serialize(content);
                using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
                    logger,
                    HttpClient,
                    "Discord Webhook送信",
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
                        request.Content = new StringContent(json);
                        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                        return request;
                    },
                    config.ExternalServiceUserAgent);
                if (!response.IsSuccessStatusCode)
                {
                    logger.ZLogWarning($"Discordへの通知に失敗しました。status={response.StatusCode.ToString()}");
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"Discordへの通知に失敗しました。");
            }
        }

        private List<NoticeCategory>? ResolveUnreadBadgeCategories()
        {
            var configured = config.UnreadBadgeNoticeCategories;

            if (configured is null || configured.Count == 0)
            {
                return [];
            }

            return configured
                .ToList();
        }

        /// <summary>
        /// お知らせ更新イベント通知を安全に実行する。
        /// </summary>
        private async ValueTask PublishNotificationChangedSafeAsync()
        {
            if (notificationEventPublisher is null)
            {
                return;
            }

            try
            {
                await notificationEventPublisher.PublishAsync(new NotificationChangedEvent(DateTimeOffset.UtcNow));
            }
            catch (Exception e)
            {
                logger.ZLogWarning(e, $"お知らせ更新イベント通知に失敗しました。");
            }
        }
    }
}
