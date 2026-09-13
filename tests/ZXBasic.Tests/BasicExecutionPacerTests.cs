// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ZXBasic.Basic;

namespace ZXBasic.Tests;

public class BasicExecutionPacerTests
{
    [Test]
    public void ChangingSpeedStartsAFreshTimingPeriod()
    {
        var pacer = new BasicExecutionPacer(BasicExecutionSpeed.Unlimited, 0, 0);

        var delay = pacer.GetDelayMilliseconds(BasicExecutionSpeed.Spectrum, 100_000, 100);

        Assert.That(delay, Is.Zero);
    }

    [Test]
    public void SpectrumSpeedIsMeasuredFromTheLatestSpeedChange()
    {
        var pacer = new BasicExecutionPacer(BasicExecutionSpeed.Unlimited, 0, 0);
        pacer.GetDelayMilliseconds(BasicExecutionSpeed.Spectrum, 100_000, 100);

        var delay = pacer.GetDelayMilliseconds(BasicExecutionSpeed.Spectrum, 100_500, 600);

        Assert.That(delay, Is.EqualTo(500).Within(0.01));
    }
}
