namespace RadiCorder.Logics.Errors;

/// <summary>
/// ドメイン/アプリケーション層のエラーを表す例外
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// ユーザー向けメッセージ
    /// </summary>
    public string UserMessage { get; }

    /// <summary>
    /// ドメイン例外を初期化する
    /// </summary>
    /// <param name="userMessage">ユーザー向けメッセージ</param>
    /// <param name="innerException">内側の例外</param>
    public DomainException(string userMessage, Exception? innerException = null)
        : base(userMessage, innerException)
    {
        UserMessage = userMessage;
    }
}
