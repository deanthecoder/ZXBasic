// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ZXBasic.Emulation;

namespace ZXBasic.Tests;

public class SpectrumBeepPlayerTests
{
    [Test]
    public void PitchZeroIsMiddleCAndOctavesDoubleTheFrequency()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpectrumBeepPlayer.GetFrequency(0), Is.EqualTo(261.6256).Within(0.0001));
            Assert.That(SpectrumBeepPlayer.GetFrequency(12), Is.EqualTo(523.2511).Within(0.0001));
            Assert.That(SpectrumBeepPlayer.GetFrequency(-12), Is.EqualTo(130.8128).Within(0.0001));
        });
    }

    [Test]
    public void CreatesRequestedLengthOfSquareWaveSamples()
    {
        var samples = SpectrumBeepPlayer.CreateSamples(0.25, 0);

        Assert.Multiple(() =>
        {
            Assert.That(samples, Has.Length.EqualTo(SpectrumBeepPlayer.SampleRate / 4));
            Assert.That(samples, Has.Some.EqualTo((byte)32));
            Assert.That(samples, Has.Some.EqualTo((byte)224));
        });
    }
}
