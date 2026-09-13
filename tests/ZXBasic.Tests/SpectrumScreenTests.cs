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

public class SpectrumScreenTests
{
    [Test]
    public void PlotUsesBottomLeftCoordinatesAndCellAttributes()
    {
        var screen = new SpectrumScreen();
        var pixels = new uint[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];

        screen.Plot(0, 0, ink: 2, paper: 7);
        screen.Plot(1, 0, ink: 1, paper: 6);
        screen.Render(pixels);

        var bottomLeft = (SpectrumScreen.BorderY + SpectrumScreen.DrawingHeight - 1) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX;
        var lowerScreenLeft = (SpectrumScreen.BorderY + SpectrumScreen.Height - 1) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX;
        Assert.Multiple(() =>
        {
            Assert.That(pixels[bottomLeft], Is.EqualTo(SpectrumPalette.Bgra[1]));
            Assert.That(pixels[bottomLeft + 1], Is.EqualTo(SpectrumPalette.Bgra[1]));
            Assert.That(pixels[bottomLeft + 2], Is.EqualTo(SpectrumPalette.Bgra[6]));
            Assert.That(pixels[lowerScreenLeft], Is.EqualTo(SpectrumPalette.Bgra[7]));
            Assert.That(pixels[0], Is.EqualTo(SpectrumPalette.Bgra[7]));
        });
    }

    [Test]
    public void ClearingATextRowRemovesPreviouslyDrawnCharacters()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        screen.DrawText(0, 23, "OLD COMMAND", font);

        screen.ClearTextRow(23);

        Assert.That(screen.IsScreenPixelSet(0, 184), Is.False);
    }

    [Test]
    public void ClearingReservedEditorRowsUsesTheBorderPaperColor()
    {
        var screen = new SpectrumScreen { BorderColor = 3 };
        var pixels = new uint[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];

        screen.ClearTextRow(22, screen.BorderColor);
        screen.ClearTextRow(23, screen.BorderColor);
        screen.Render(pixels);

        Assert.Multiple(() =>
        {
            Assert.That(pixels[(SpectrumScreen.BorderY + 176) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX],
                Is.EqualTo(SpectrumPalette.Bgra[3]));
            Assert.That(pixels[(SpectrumScreen.BorderY + 191) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX],
                Is.EqualTo(SpectrumPalette.Bgra[3]));
        });
    }

    [Test]
    public void FlashSwapsInkAndPaperDuringTheFlashPhase()
    {
        var screen = new SpectrumScreen();
        var pixels = new uint[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];
        screen.Plot(0, 0, ink: 2, paper: 6, flash: true);
        var pixel = (SpectrumScreen.BorderY + SpectrumScreen.DrawingHeight - 1) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX;

        screen.Render(pixels, flashPhase: false);
        var normalColor = pixels[pixel];
        screen.Render(pixels, flashPhase: true);

        Assert.Multiple(() =>
        {
            Assert.That(normalColor, Is.EqualTo(SpectrumPalette.Bgra[2]));
            Assert.That(pixels[pixel], Is.EqualTo(SpectrumPalette.Bgra[6]));
        });
    }

    [Test]
    public void SpectrumBitmapMemoryUsesTheOriginalRowLayout()
    {
        var screen = new SpectrumScreen();

        screen.WriteMemory(16384, 0x80);
        screen.WriteMemory(16640, 0x40);
        screen.WriteMemory(16416, 0x20);

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(1, 1), Is.True);
            Assert.That(screen.IsScreenPixelSet(2, 8), Is.True);
            Assert.That(screen.ReadMemory(16384), Is.EqualTo(0x80));
            Assert.That(screen.ReadMemory(16640), Is.EqualTo(0x40));
            Assert.That(screen.ReadMemory(16416), Is.EqualTo(0x20));
        });
    }

    [Test]
    public void SpectrumAttributeMemoryMapsToCharacterCells()
    {
        var screen = new SpectrumScreen();

        screen.WriteMemory(22528, 0x01);
        screen.WriteMemory(23295, 0x38);

        Assert.Multiple(() =>
        {
            Assert.That(screen.GetAttribute(0, 0), Is.EqualTo(0x01));
            Assert.That(screen.GetAttribute(23, 31), Is.EqualTo(0x38));
            Assert.That(screen.ReadMemory(22528), Is.EqualTo(0x01));
            Assert.That(screen.ReadMemory(23295), Is.EqualTo(0x38));
        });
    }
}
