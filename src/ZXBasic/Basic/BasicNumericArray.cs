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

public sealed class BasicNumericArray
{
    private readonly int[] m_dimensions;
    private readonly double[] m_values;

    public BasicNumericArray(IEnumerable<int> dimensions)
    {
        m_dimensions = dimensions.ToArray();
        if (m_dimensions.Length == 0 || m_dimensions.Any(size => size < 1))
            throw new BasicSyntaxException("Array dimensions must be positive.", 0);

        m_values = new double[m_dimensions.Aggregate(1, (total, size) => checked(total * size))];
    }

    public double Get(IReadOnlyList<int> indices)
    {
        return m_values[GetOffset(indices)];
    }

    public void Set(IReadOnlyList<int> indices, double value)
    {
        m_values[GetOffset(indices)] = value;
    }

    private int GetOffset(IReadOnlyList<int> indices)
    {
        if (indices.Count != m_dimensions.Length)
            throw new BasicSyntaxException("Wrong number of array indices.", 0);

        var offset = 0;
        for (var i = 0; i < indices.Count; i++)
        {
            if (indices[i] < 1 || indices[i] > m_dimensions[i])
                throw new BasicSyntaxException("Array index out of range.", 0);
            offset = checked(offset * m_dimensions[i] + indices[i] - 1);
        }
        return offset;
    }
}
