namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 配信サービスではなく、取得方式と保存時の音声処理を指定する。
/// </summary>
public record RecordingAcquisitionPlan(string Method, RecordingAudioOutput AudioOutput = RecordingAudioOutput.CopyAac,
    bool FastStart = false, int TailCompensationSeconds = 0)
{
    public const string Live = "live";
    public const string Archive = "archive";
    public const string RadikoTimeFree = "radiko-timefree";
}

public enum RecordingAudioOutput
{
    CopyAac,
    EncodeAac
}
