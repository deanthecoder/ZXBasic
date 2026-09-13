// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ZXBasic.Basic;

public readonly record struct BasicDataValue(double? Number, string? Text)
{
    public bool IsString => Text != null;

    public static BasicDataValue FromNumber(double value)
    {
        return new BasicDataValue(value, null);
    }

    public static BasicDataValue FromString(string value)
    {
        return new BasicDataValue(null, value);
    }
}
