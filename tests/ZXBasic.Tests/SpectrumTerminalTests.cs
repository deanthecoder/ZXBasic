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
using Avalonia.Input;
using Avalonia.Media.Imaging;
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

    [Test]
    public void ScreenshotPreservesTheDisplayedAspectRatio()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                SpectrumTerminal.GetScreenshotPixelSize(new PixelSize(320, 240), false),
                Is.EqualTo(new PixelSize(320, 240)));
            Assert.That(
                SpectrumTerminal.GetScreenshotPixelSize(new PixelSize(960, 960), true),
                Is.EqualTo(new PixelSize(960, 720)));
        });
    }

    [Test]
    public void CrtControlsDisplayInterpolationAtEveryScalingStage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                SpectrumTerminal.GetDisplayInterpolationMode(true),
                Is.EqualTo(BitmapInterpolationMode.HighQuality));
            Assert.That(
                SpectrumTerminal.GetDisplayInterpolationMode(false),
                Is.EqualTo(BitmapInterpolationMode.LowQuality));
        });
    }

    [Test]
    public void ListingMarkerMovesToAdjacentProgramLines()
    {
        int[] lineNumbers = [10, 30, 100];

        Assert.Multiple(() =>
        {
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber(lineNumbers, 30, -1), Is.EqualTo(10));
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber(lineNumbers, 30, 1), Is.EqualTo(100));
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber(lineNumbers, 10, -1), Is.EqualTo(10));
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber(lineNumbers, 100, 1), Is.EqualTo(100));
        });
    }

    [Test]
    public void ListingMarkerStartsAtTheEndMatchingTheArrowDirection()
    {
        int[] lineNumbers = [10, 30, 100];

        Assert.Multiple(() =>
        {
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber(lineNumbers, null, -1), Is.EqualTo(100));
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber(lineNumbers, null, 1), Is.EqualTo(10));
            Assert.That(SpectrumTerminal.GetAdjacentListingLineNumber([], null, 1), Is.Null);
        });
    }

    [TestCase(Key.Enter, true)]
    [TestCase(Key.Escape, true)]
    [TestCase(Key.A, false)]
    [TestCase(Key.Up, false)]
    [TestCase(Key.Back, false)]
    public void ReportDismissalConsumesOnlyEnterAndEscape(Key key, bool expected)
    {
        Assert.That(SpectrumTerminal.ShouldConsumeReportDismissalKey(key), Is.EqualTo(expected));
    }
}
