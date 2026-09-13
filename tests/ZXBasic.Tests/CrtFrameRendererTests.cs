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

public class CrtFrameRendererTests
{
    [Test]
    public void ExpandsPixelsIntoRgbPhosphorsAndADimmedScanline()
    {
        var renderer = new CrtFrameRenderer(1, 1);
        var destination = new uint[renderer.OutputWidth * renderer.OutputHeight];

        renderer.Render([0xffffffff], destination);

        Assert.Multiple(() =>
        {
            Assert.That(Red(destination[0]), Is.GreaterThan(Green(destination[0])));
            Assert.That(Green(destination[1]), Is.GreaterThan(Red(destination[1])));
            Assert.That(Blue(destination[2]), Is.GreaterThan(Green(destination[2])));
            Assert.That(Red(destination[renderer.OutputWidth * 3]), Is.LessThan(Red(destination[0])));
        });
    }

    private static byte Red(uint pixel) => (byte)(pixel >> 16);
    private static byte Green(uint pixel) => (byte)(pixel >> 8);
    private static byte Blue(uint pixel) => (byte)pixel;
}
