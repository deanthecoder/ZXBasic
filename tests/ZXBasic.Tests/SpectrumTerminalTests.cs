// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia;
using ZXBasic.Controls;
using ZXBasic.Emulation;

namespace ZXBasic.Tests;

public class SpectrumTerminalTests
{
    [Test]
    public void MouseCoordinatesMatchTheBasicDrawingArea()
    {
        var bounds = new Rect(0, 0, SpectrumScreen.FrameWidth, SpectrumScreen.FrameHeight);

        Assert.Multiple(() =>
        {
            Assert.That(
                SpectrumTerminal.GetBasicDisplayPosition(new Point(SpectrumScreen.BorderX, SpectrumScreen.BorderY), bounds),
                Is.EqualTo((0, SpectrumScreen.DrawingHeight - 1)));
            Assert.That(
                SpectrumTerminal.GetBasicDisplayPosition(
                    new Point(SpectrumScreen.BorderX + SpectrumScreen.Width - 1, SpectrumScreen.BorderY + SpectrumScreen.DrawingHeight - 1),
                    bounds),
                Is.EqualTo((SpectrumScreen.Width - 1, 0)));
        });
    }

    [Test]
    public void MouseCoordinatesRejectTheReservedTextRows()
    {
        var bounds = new Rect(0, 0, SpectrumScreen.FrameWidth, SpectrumScreen.FrameHeight);

        var coordinate = SpectrumTerminal.GetBasicDisplayPosition(
            new Point(SpectrumScreen.BorderX, SpectrumScreen.BorderY + SpectrumScreen.DrawingHeight),
            bounds);

        Assert.That(coordinate, Is.Null);
    }
}
