using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Infrastructure.Configuration;

/// <summary>
/// 既存設定テーブルの値を取得・保存する。transactionとscopeは呼び出し側が管理する。
/// </summary>
internal static class AppConfigurationValues
{
    /// <summary>
    /// 文字列設定値を取得する
    /// </summary>
    /// <param name="dbContext">DBコンテキスト</param>
    /// <param name="name">設定キー</param>
    /// <returns>設定値</returns>
    public static string? GetStringValue(RadioDbContext dbContext, string name)
    {
        return dbContext.AppConfigurations.FirstOrDefault(r => r.ConfigurationName == name)?.Val1;
    }

    /// <summary>
    /// 数値設定値を取得する
    /// </summary>
    /// <param name="dbContext">DBコンテキスト</param>
    /// <param name="name">設定キー</param>
    /// <returns>設定値</returns>
    public static int? GetIntValue(RadioDbContext dbContext, string name)
    {
        return dbContext.AppConfigurations.FirstOrDefault(r => r.ConfigurationName == name)?.Val2;
    }

    /// <summary>
    /// 文字列設定値を追加または更新する
    /// </summary>
    /// <param name="dbContext">DBコンテキスト</param>
    /// <param name="name">設定キー</param>
    /// <param name="value">設定値</param>
    public static async ValueTask UpsertStringAsync(RadioDbContext dbContext, string name, string value)
    {
        var existing = await dbContext.AppConfigurations.FirstOrDefaultAsync(r => r.ConfigurationName == name);
        if (existing == null)
        {
            await dbContext.AppConfigurations.AddAsync(new AppConfiguration
            {
                ConfigurationName = name,
                Val1 = value
            });
            await dbContext.SaveChangesAsync();
            return;
        }

        existing.Val1 = value;
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 数値設定値を追加または更新する
    /// </summary>
    /// <param name="dbContext">DBコンテキスト</param>
    /// <param name="name">設定キー</param>
    /// <param name="value">設定値</param>
    public static async ValueTask UpsertIntAsync(RadioDbContext dbContext, string name, int value)
    {
        var existing = await dbContext.AppConfigurations.FirstOrDefaultAsync(r => r.ConfigurationName == name);
        if (existing == null)
        {
            await dbContext.AppConfigurations.AddAsync(new AppConfiguration
            {
                ConfigurationName = name,
                Val2 = value
            });
            await dbContext.SaveChangesAsync();
            return;
        }

        existing.Val2 = value;
        await dbContext.SaveChangesAsync();
    }
}
