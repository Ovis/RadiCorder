using RadiCorder.Logics.BackgroundServices;

namespace RadiCorder.Logics.Tests.LogicTest;

[TestFixture]
public class RecordingScheduleWakeupTests
{
    [Test]
    public async Task Wake_待機中なら即時に解除される()
    {
        var wakeup = new RecordingScheduleWakeup();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var waitTask = wakeup.WaitAsync(cts.Token).AsTask();
        wakeup.Wake();

        await waitTask;
        Assert.That(waitTask.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task Wake_先行通知でも次回待機で解除される()
    {
        var wakeup = new RecordingScheduleWakeup();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        wakeup.Wake();
        await wakeup.WaitAsync(cts.Token);
    }

    [Test]
    public async Task Wake_古い待機があっても次の待機で取りこぼさない()
    {
        var wakeup = new RecordingScheduleWakeup();
        using var staleCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var activeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var staleWait = wakeup.WaitAsync(staleCts.Token).AsTask();
        var winnerWait = wakeup.WaitAsync(activeCts.Token).AsTask();

        wakeup.Wake();

        await Task.WhenAll(staleWait, winnerWait);
        Assert.That(staleWait.IsCompletedSuccessfully, Is.True);
        Assert.That(winnerWait.IsCompletedSuccessfully, Is.True);
    }
}
