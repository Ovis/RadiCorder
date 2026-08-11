namespace RadiCorder.Features.Shared.Models;

/// <summary>
/// API共通レスポンス
/// </summary>
/// <typeparam name="T">データ型</typeparam>
/// <param name="Success">処理が成功したか</param>
/// <param name="Data">成功時のデータ</param>
/// <param name="Error">失敗時のエラー情報</param>
/// <param name="Message">表示用メッセージ</param>
public record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    string? Message)
{
    /// <summary>
    /// 成功レスポンスを生成する
    /// </summary>
    /// <param name="data">返却データ</param>
    /// <param name="message">メッセージ</param>
    /// <returns>成功レスポンス</returns>
    public static ApiResponse<T> Ok(T data, string? message = null)
    {
        return new ApiResponse<T>(true, data, null, message);
    }

    /// <summary>
    /// 失敗レスポンスを生成する
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="code">エラーコード</param>
    /// <returns>失敗レスポンス</returns>
    public static ApiResponse<T> Fail(string message, string? code = null)
    {
        return new ApiResponse<T>(false, default, new ApiError(code, message), message);
    }
}

/// <summary>
/// APIエラー情報
/// </summary>
/// <param name="Code">エラーコード</param>
/// <param name="Message">エラーメッセージ</param>
public record ApiError(
    string? Code,
    string Message);

/// <summary>
/// データ本体を返さないAPIレスポンス用の空データ。
/// </summary>
public sealed record EmptyData;

/// <summary>
/// ApiResponse生成ヘルパー
/// </summary>
public static class ApiResponse
{
    /// <summary>
    /// 成功レスポンスを生成する
    /// </summary>
    /// <typeparam name="T">データ型</typeparam>
    /// <param name="data">返却データ</param>
    /// <param name="message">メッセージ</param>
    /// <returns>成功レスポンス</returns>
    public static ApiResponse<T> Ok<T>(T data, string? message = null)
    {
        return ApiResponse<T>.Ok(data, message);
    }

    /// <summary>
    /// 成功レスポンス（データなし）を生成する
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <returns>成功レスポンス</returns>
    public static ApiResponse<EmptyData?> Ok(string? message = null)
    {
        return ApiResponse<EmptyData?>.Ok(null, message);
    }

    /// <summary>
    /// 失敗レスポンスを生成する
    /// </summary>
    /// <typeparam name="T">データ型</typeparam>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="code">エラーコード</param>
    /// <returns>失敗レスポンス</returns>
    public static ApiResponse<T> Fail<T>(string message, string? code = null)
    {
        return ApiResponse<T>.Fail(message, code);
    }

    /// <summary>
    /// 失敗レスポンス（データなし）を生成する
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="code">エラーコード</param>
    /// <returns>失敗レスポンス</returns>
    public static ApiResponse<EmptyData?> Fail(string message, string? code = null)
    {
        return ApiResponse<EmptyData?>.Fail(message, code);
    }
}

