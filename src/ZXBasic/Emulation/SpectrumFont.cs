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

public sealed class SpectrumFont
{
    private const int GlyphCount = 96;
    private const int BytesPerGlyph = 8;
    private readonly byte[] m_glyphs;

    private SpectrumFont(byte[] glyphs)
    {
        m_glyphs = glyphs;
    }

    public ReadOnlySpan<byte> this[char character]
    {
        get
        {
            var code = character switch
            {
                '©' => 127,
                '↑' => 94,
                _ => character
            };
            if (code is < 32 or > 127)
                code = '?';

            return m_glyphs.AsSpan((code - 32) * BytesPerGlyph, BytesPerGlyph);
        }
    }

    public static SpectrumFont Load()
    {
        return FromGlyphs(SpectrumFontData.Glyphs);
    }

    public static SpectrumFont FromGlyphs(ReadOnlySpan<byte> glyphs)
    {
        if (glyphs.Length != GlyphCount * BytesPerGlyph)
            throw new ArgumentException("A Spectrum font must contain 96 eight-byte glyphs.", nameof(glyphs));
        return new SpectrumFont(glyphs.ToArray());
    }

    public char? Recognize(ReadOnlySpan<byte> glyph)
    {
        if (glyph.Length < BytesPerGlyph)
        {
            throw new ArgumentException("A Spectrum glyph needs eight bytes.", nameof(glyph));
        }

        for (var index = 0; index < GlyphCount; index++)
        {
            if (glyph[..BytesPerGlyph].SequenceEqual(m_glyphs.AsSpan(index * BytesPerGlyph, BytesPerGlyph)))
            {
                return (char)(index + 32);
            }
        }

        return null;
    }
}
