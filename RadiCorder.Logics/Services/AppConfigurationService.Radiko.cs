using Microsoft.AspNetCore.DataProtection;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.RdbContext;
using ZLogger;

using static RadiCorder.Logics.Infrastructure.Configuration.AppConfigurationValues;

namespace RadiCorder.Logics.Services
{
    public partial class AppConfigurationService
    {
        /// <summary>
        /// IsRadikoPremiumUserの値を更新
        /// </summary>
        /// <param name="isRadikoPremiumUser"></param>
        public void UpdateRadikoPremiumUser(bool isRadikoPremiumUser)
        {
            lock (_lock)
            {
                IsRadikoPremiumUser = isRadikoPremiumUser;
            }
        }

        /// <summary>
        /// radikoのエリアフリー利用可否を更新
        /// </summary>
        /// <param name="isRadikoAreaFree"></param>
        public void UpdateRadikoAreaFree(bool isRadikoAreaFree)
        {
            lock (_lock)
            {
                IsRadikoAreaFree = isRadikoAreaFree;
            }
        }

        public async ValueTask UpdateRadikoCredentialsAsync(string userId, string password)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                var protectedUserId = _dataProtector.Protect(userId);
                var protectedPassword = _dataProtector.Protect(password);

                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoUserIdProtected, protectedUserId);
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoPasswordProtected, protectedPassword);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateRadikoCredentials");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                RadikoOptions.RadikoUserId = userId;
                HasRadikoCredentials = true;
            }
        }

        public async ValueTask ClearRadikoCredentialsAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoUserIdProtected, string.Empty);
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoPasswordProtected, string.Empty);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed ClearRadikoCredentials");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                RadikoOptions.RadikoUserId = string.Empty;
                RadikoOptions.RadikoPassword = string.Empty;
                HasRadikoCredentials = false;
                IsRadikoPremiumUser = false;
                IsRadikoAreaFree = false;
            }
        }

        /// <summary>
        /// 保存済みのradiko資格情報を取得する
        /// </summary>
        /// <returns>取得可否と資格情報</returns>
        public ValueTask<(bool IsSuccess, string UserId, string Password)> TryGetRadikoCredentialsAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            var protectedUserId = GetStringValue(dbContext, AppConfigurationNames.RadikoUserIdProtected) ?? string.Empty;
            var protectedPassword = GetStringValue(dbContext, AppConfigurationNames.RadikoPasswordProtected) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(protectedUserId) || string.IsNullOrWhiteSpace(protectedPassword))
            {
                return ValueTask.FromResult((false, string.Empty, string.Empty));
            }

            try
            {
                var userId = _dataProtector.Unprotect(protectedUserId);
                var password = _dataProtector.Unprotect(protectedPassword);
                return ValueTask.FromResult((true, userId, password));
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"radikoログイン情報の復号に失敗しました。");
                return ValueTask.FromResult((false, string.Empty, string.Empty));
            }
        }
    }
}
