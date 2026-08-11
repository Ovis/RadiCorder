namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音状態変更時に配信するイベント情報。
/// </summary>
/// <param name="RecordingId">録音ID</param>
/// <param name="State">更新後の録音状態</param>
/// <param name="ErrorMessage">失敗時メッセージ</param>
/// <param name="UpdatedAtUtc">更新時刻(UTC)</param>
public sealed record RecordingStateChangedEvent(
    Ulid RecordingId,
    RecordingState State,
    string? ErrorMessage,
    DateTimeOffset UpdatedAtUtc);
