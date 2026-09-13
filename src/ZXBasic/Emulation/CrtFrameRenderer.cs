// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ZXBasic.Emulation;

public sealed class CrtFrameRenderer
{
    private const float ScanlineMultiplier = 0.7f;
    private const float PhosphorShrink = 0.5f;
    private const float Brightness = 1.5f;
    private readonly int m_sourceWidth;
    private readonly int m_sourceHeight;
    private readonly float[] m_grain;
    private readonly float[] m_vignette;

    public int OutputWidth => m_sourceWidth * 3;
    public int OutputHeight => m_sourceHeight * 4;

    public CrtFrameRenderer(int sourceWidth, int sourceHeight)
    {
        m_sourceWidth = sourceWidth;
        m_sourceHeight = sourceHeight;
        m_grain = new float[sourceWidth * sourceHeight];
        m_vignette = new float[sourceWidth * sourceHeight];

        var random = new Random(0);
        for (var y = 0; y < sourceHeight; y++)
        {
            var uvY = (double)y / sourceHeight;
            for (var x = 0; x < sourceWidth; x++)
            {
                var index = y * sourceWidth + x;
                var uvX = (double)x / sourceWidth;
                m_grain[index] = (float)(random.NextDouble() * 10.0);
                m_vignette[index] = (float)(0.7 + 0.3 * Math.Sqrt(
                    64.0 * uvX * uvY * (1.0 - uvX) * (1.0 - uvY)));
            }
        }
    }

    public void Render(ReadOnlySpan<uint> source, Span<uint> destination)
    {
        if (source.Length < m_sourceWidth * m_sourceHeight)
        {
            throw new ArgumentException("The source is too small for the CRT frame.", nameof(source));
        }

        if (destination.Length < OutputWidth * OutputHeight)
        {
            throw new ArgumentException("The destination is too small for the CRT frame.", nameof(destination));
        }

        for (var y = 0; y < m_sourceHeight; y++)
        {
            for (var x = 0; x < m_sourceWidth; x++)
            {
                var sourceIndex = y * m_sourceWidth + x;
                var sourceColor = source[sourceIndex];
                var grain = m_grain[sourceIndex];
                var vignette = m_vignette[sourceIndex];
                var red = (((sourceColor >> 16) & 0xff) + grain) * Brightness * vignette * 1.1f;
                var green = (((sourceColor >> 8) & 0xff) + grain) * Brightness * vignette;
                var blue = ((sourceColor & 0xff) + grain) * Brightness * vignette * 1.1f;
                var outputX = x * 3;
                var outputY = y * 4;

                SetVerticalStripe(destination, outputX, outputY, red, green * PhosphorShrink, blue * PhosphorShrink);
                SetVerticalStripe(destination, outputX + 1, outputY, red * PhosphorShrink, green, blue * PhosphorShrink);
                SetVerticalStripe(destination, outputX + 2, outputY, red * PhosphorShrink, green * PhosphorShrink, blue);
            }
        }
    }

    private void SetVerticalStripe(
        Span<uint> destination,
        int x,
        int y,
        float red,
        float green,
        float blue)
    {
        var pixel = Pack(red, green, blue);
        var offset = y * OutputWidth + x;
        destination[offset] = pixel;
        destination[offset + OutputWidth] = pixel;
        destination[offset + OutputWidth * 2] = pixel;
        destination[offset + OutputWidth * 3] = Pack(
            red * ScanlineMultiplier,
            green * ScanlineMultiplier,
            blue * ScanlineMultiplier);
    }

    private static uint Pack(float red, float green, float blue)
    {
        var r = (uint)Math.Clamp(red, 0, 255);
        var g = (uint)Math.Clamp(green, 0, 255);
        var b = (uint)Math.Clamp(blue, 0, 255);
        return 0xff000000 | r << 16 | g << 8 | b;
    }
}
