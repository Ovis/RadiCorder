namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// 番組表更新状態の管理を行う。
/// </summary>
public interface IProgramUpdateStatusService
{
    /// <summary>
    /// 現在の更新状態を取得する。
    /// </summary>
    ProgramUpdateStatusSnapshot GetCurrent();

    /// <summary>
    /// 更新開始状態を記録する。
    /// </summary>
    /// <param name="triggerSource">起動元</param>
    ProgramUpdateStatusSnapshot MarkStarted(string triggerSource);

    /// <summary>
    /// 更新成功状態を記録する。
    /// </summary>
    ProgramUpdateStatusSnapshot MarkSucceeded();

    /// <summary>
    /// 更新失敗状態を記録する。
    /// </summary>
    ProgramUpdateStatusSnapshot MarkFailed();
    ProgramUpdateStatusSnapshot MarkFailed(string message) => MarkFailed() with { Message = message };
}
