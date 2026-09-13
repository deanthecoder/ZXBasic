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
    private const int FontOffset = 0x3D00;
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
        var romFile = FindRom();
        var rom = File.ReadAllBytes(romFile.FullName);
        var requiredLength = FontOffset + GlyphCount * BytesPerGlyph;
        if (rom.Length < requiredLength)
            throw new InvalidDataException($"The ROM at '{romFile.FullName}' is too small to contain the Spectrum character set.");

        return FromGlyphs(rom.AsSpan(FontOffset, GlyphCount * BytesPerGlyph));
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

    private static FileInfo FindRom()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ZXBASIC_ROM_PATH");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var candidates = new[]
        {
            configuredPath,
            Path.Combine(AppContext.BaseDirectory, "48.rom"),
            Path.Combine(documents, "Source", "Repos", "ZXSpeculator", "Speculator", "Speculator", "ROMs", "Standard Spectrum 48K BASIC.rom")
        };

        var path = candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
        if (path == null)
        {
            throw new FileNotFoundException(
                "ZXBasic needs a Spectrum 48K ROM to obtain the original character set. Set ZXBASIC_ROM_PATH or place 48.rom beside the application.");
        }

        return new FileInfo(path);
    }
}
