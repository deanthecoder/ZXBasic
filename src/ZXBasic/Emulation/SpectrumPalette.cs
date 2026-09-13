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

public static class SpectrumPalette
{
    public static ReadOnlySpan<uint> Bgra =>
    [
        0xFF000000, 0xFF0000CD, 0xFFCD0000, 0xFFCD00CD,
        0xFF00CD00, 0xFF00CDCD, 0xFFCDCD00, 0xFFCDCDCD,
        0xFF000000, 0xFF0000FF, 0xFFFF0000, 0xFFFF00FF,
        0xFF00FF00, 0xFF00FFFF, 0xFFFFFF00, 0xFFFFFFFF
    ];
}
