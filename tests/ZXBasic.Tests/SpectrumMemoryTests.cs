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

public class SpectrumMemoryTests
{
    [Test]
    public void PokeUpdatesTheAttachedDisplay()
    {
        var screen = new SpectrumScreen();
        var memory = new SpectrumMemory(screen);

        memory.Poke(16384, 0x80);
        memory.Poke(22528, 0x01);

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.GetAttribute(0, 0), Is.EqualTo(0x01));
        });
    }

    [Test]
    public void PeekReadsChangesMadeThroughScreenCommands()
    {
        var screen = new SpectrumScreen();
        var memory = new SpectrumMemory(screen);

        screen.Plot(0, 175, ink: 2, paper: 7);

        Assert.Multiple(() =>
        {
            Assert.That(memory.Peek(16384), Is.EqualTo(0x80));
            Assert.That(memory.Peek(22528), Is.EqualTo(0x3a));
        });
    }

    [Test]
    public void FramesAdvancesAtFiftyHertzAndWrapsAtTwentyFourBits()
    {
        long timestamp = 1_000;
        var memory = new SpectrumMemory(null, () => timestamp, 1_000, 50);

        timestamp += 20;
        var firstFrame = memory.Peek(SpectrumMemory.FramesAddress);
        timestamp += 0xFFFFFF * 20L;

        Assert.Multiple(() =>
        {
            Assert.That(firstFrame, Is.EqualTo(1));
            Assert.That(memory.Peek(SpectrumMemory.FramesAddress), Is.EqualTo(0));
            Assert.That(memory.Peek(SpectrumMemory.FramesAddress + 1), Is.EqualTo(0));
            Assert.That(memory.Peek(SpectrumMemory.FramesAddress + 2), Is.EqualTo(0));
        });
    }

    [TestCase(50, 60)]
    [TestCase(60, 50)]
    public void FramesUsesTheConfiguredVideoRate(int framesPerSecond, long ticksPerFrame)
    {
        long timestamp = 0;
        var memory = new SpectrumMemory(null, () => timestamp, 3_000, framesPerSecond);

        timestamp = ticksPerFrame;

        Assert.That(memory.Peek(SpectrumMemory.FramesAddress), Is.EqualTo(1));
    }

    [Test]
    public void FramesCanBeChangedWithPoke()
    {
        long timestamp = 0;
        var memory = new SpectrumMemory(null, () => timestamp, 1_000, 50);

        memory.Poke(SpectrumMemory.FramesAddress, 0xFE);
        memory.Poke(SpectrumMemory.FramesAddress + 1, 0x12);
        timestamp = 20;

        Assert.Multiple(() =>
        {
            Assert.That(memory.Peek(SpectrumMemory.FramesAddress), Is.EqualTo(0xFF));
            Assert.That(memory.Peek(SpectrumMemory.FramesAddress + 1), Is.EqualTo(0x12));
            Assert.That(memory.Peek(SpectrumMemory.FramesAddress + 2), Is.EqualTo(0));
        });
    }
}
