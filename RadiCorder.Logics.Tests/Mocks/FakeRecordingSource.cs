using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// 録音ソースのテスト用スタブ
/// </summary>
public class FakeRecordingSource : IRecordingSource
{
    private readonly RecordingSourceResult _result;
    private readonly RadioServiceKind _kind;
    private readonly Exception? _exception;

    public FakeRecordingSource(RadioServiceKind kind, RecordingSourceResult result, Exception? exception = null)
    {
        _kind = kind;
        _result = result;
        _exception = exception;
    }

    /// <summary>
    /// 指定サービス種別を処理可能か判定
    /// </summary>
    public bool CanHandle(RadioServiceKind kind) => kind == _kind;

    /// <summary>
    /// 固定の録音ソース情報を返す
    /// </summary>
    public ValueTask<RecordingSourceResult> PrepareAsync(RecordingCommand command, CancellationToken cancellationToken = default)
    {
        if (_exception != null)
        {
            throw _exception;
        }

        return ValueTask.FromResult(_result);
    }
}
