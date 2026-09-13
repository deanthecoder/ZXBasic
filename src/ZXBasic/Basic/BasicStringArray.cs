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

public sealed class BasicStringArray
{
    private readonly int[] m_dimensions;
    private readonly string[] m_values;

    public int Rank => m_dimensions.Length;
    public int Width { get; }

    public BasicStringArray(IEnumerable<int> dimensions)
    {
        var sizes = dimensions.ToArray();
        if (sizes.Length == 0 || sizes.Any(size => size < 1))
        {
            throw new BasicSyntaxException("String array dimensions must be positive.", 0);
        }

        Width = sizes[^1];
        m_dimensions = sizes[..^1];
        var count = m_dimensions.Aggregate(1, (total, size) => checked(total * size));
        m_values = Enumerable.Repeat(new string(' ', Width), count).ToArray();
    }

    public string Get(IReadOnlyList<int> indices)
    {
        return m_values[GetOffset(indices)];
    }

    public void Set(IReadOnlyList<int> indices, string value)
    {
        m_values[GetOffset(indices)] = value.Length >= Width
            ? value[..Width]
            : value.PadRight(Width);
    }

    private int GetOffset(IReadOnlyList<int> indices)
    {
        if (indices.Count != m_dimensions.Length)
        {
            throw new BasicSyntaxException("Wrong number of string array indices.", 0);
        }

        var offset = 0;
        for (var i = 0; i < indices.Count; i++)
        {
            if (indices[i] < 1 || indices[i] > m_dimensions[i])
            {
                throw new BasicSyntaxException("String array index out of range.", 0);
            }
            offset = checked(offset * m_dimensions[i] + indices[i] - 1);
        }
        return offset;
    }
}
