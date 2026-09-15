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

public sealed class SpectrumScreen
{
    public const int BitmapMemoryAddress = 16384;
    public const int BitmapMemoryLength = 6144;
    public const int AttributeMemoryAddress = 22528;
    public const int AttributeMemoryLength = 768;
    public const int LastDisplayMemoryAddress = AttributeMemoryAddress + AttributeMemoryLength - 1;
    public const int Width = 256;
    public const int Height = 192;
    public const int DrawingHeight = 176;
    public const int Columns = 32;
    public const int Rows = 24;
    public const int BorderX = 32;
    public const int BorderY = 24;
    public const int FrameWidth = Width + BorderX * 2;
    public const int FrameHeight = Height + BorderY * 2;

    private readonly byte[] m_bitmap = new byte[Width * Height / 8];
    private readonly byte[] m_attributes = new byte[Columns * Rows];

    public byte BorderColor { get; set; } = 7;

    public SpectrumScreen()
    {
        Clear();
    }

    public void Clear(byte ink = 0, byte paper = 7, bool bright = false)
    {
        Array.Clear(m_bitmap);
        Array.Fill(m_attributes, MakeAttribute(ink, paper, bright));
    }

    public void DrawText(
        int column,
        int row,
        string text,
        SpectrumFont font,
        byte ink = 0,
        byte paper = 7,
        bool bright = false,
        bool flash = false,
        bool inverse = false,
        bool over = false)
    {
        foreach (var character in text)
        {
            if (character == '\n')
            {
                column = 0;
                row++;
                continue;
            }

            if (column >= Columns)
            {
                column = 0;
                row++;
            }

            if (row >= Rows)
                return;

            DrawGlyph(column++, row, font[character], ink, paper, bright, flash, inverse, over);
        }
    }

    public void DrawGlyph(
        int column,
        int row,
        ReadOnlySpan<byte> glyph,
        byte ink = 0,
        byte paper = 7,
        bool bright = false,
        bool flash = false,
        bool inverse = false,
        bool over = false)
    {
        if (column is < 0 or >= Columns || row is < 0 or >= Rows)
            return;

        for (var y = 0; y < 8; y++)
        {
            var value = inverse ? (byte)~glyph[y] : glyph[y];
            var index = (row * 8 + y) * Columns + column;
            if (over)
            {
                m_bitmap[index] ^= value;
            }
            else
            {
                m_bitmap[index] = value;
            }
        }

        m_attributes[row * Columns + column] = MakeAttribute(ink, paper, bright, flash);
    }

    public void ClearTextRow(int row, byte paper = 7, bool bright = false)
    {
        if (row is < 0 or >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "The text row is outside the screen.");
        }

        for (var y = row * 8; y < row * 8 + 8; y++)
        {
            Array.Clear(m_bitmap, y * Columns, Columns);
        }

        Array.Fill(m_attributes, MakeAttribute(0, paper, bright), row * Columns, Columns);
    }

    public void Plot(
        int x,
        int y,
        byte ink,
        byte paper,
        bool bright = false,
        bool flash = false,
        bool inverse = false,
        bool over = false,
        bool preserveAttributes = false)
    {
        if (x is < 0 or >= Width || y is < 0 or >= DrawingHeight)
            return;

        var screenY = DrawingHeight - 1 - y;
        var byteIndex = screenY * Columns + x / 8;
        var mask = (byte)(0x80 >> (x & 7));
        if (over)
        {
            m_bitmap[byteIndex] ^= mask;
        }
        else if (inverse)
        {
            m_bitmap[byteIndex] &= (byte)~mask;
        }
        else
        {
            m_bitmap[byteIndex] |= mask;
        }

        if (!preserveAttributes)
        {
            m_attributes[screenY / 8 * Columns + x / 8] = MakeAttribute(ink, paper, bright, flash);
        }
    }

    public bool IsPixelSet(int x, int y)
    {
        if (x is < 0 or >= Width || y is < 0 or >= DrawingHeight)
            return false;
        var screenY = DrawingHeight - 1 - y;
        return (m_bitmap[screenY * Columns + x / 8] & (0x80 >> (x & 7))) != 0;
    }

    public void FloodFill(
        int x,
        int y,
        byte ink,
        byte paper,
        bool bright = false,
        bool flash = false,
        bool inverse = false,
        bool over = false,
        bool preserveAttributes = false)
    {
        if (x is < 0 or >= Width || y is < 0 or >= DrawingHeight)
        {
            return;
        }

        var target = IsPixelSet(x, y);
        var replacement = over ? !target : !inverse;
        if (target == replacement)
        {
            return;
        }

        var pending = new Queue<(int X, int Y)>();
        FillPixel(x, y);
        pending.Enqueue((x, y));

        while (pending.TryDequeue(out var point))
        {
            TryAdd(point.X - 1, point.Y);
            TryAdd(point.X + 1, point.Y);
            TryAdd(point.X, point.Y - 1);
            TryAdd(point.X, point.Y + 1);
        }
        return;

        void TryAdd(int nextX, int nextY)
        {
            if (nextX is < 0 or >= Width || nextY is < 0 or >= DrawingHeight ||
                IsPixelSet(nextX, nextY) != target)
            {
                return;
            }

            FillPixel(nextX, nextY);
            pending.Enqueue((nextX, nextY));
        }

        void FillPixel(int pixelX, int pixelY)
        {
            // Filling changes pixel ink while retaining each character cell's paper color.
            var attribute = m_attributes[(DrawingHeight - 1 - pixelY) / 8 * Columns + pixelX / 8];
            var existingPaper = (byte)((attribute >> 3) & 7);
            Plot(pixelX, pixelY, ink, existingPaper, bright, flash, inverse, over, preserveAttributes);
        }
    }

    public bool IsScreenPixelSet(int x, int y)
    {
        if (x is < 0 or >= Width || y is < 0 or >= Height)
        {
            return false;
        }

        return (m_bitmap[y * Columns + x / 8] & (0x80 >> (x & 7))) != 0;
    }

    public void CopyGlyph(int row, int column, Span<byte> destination)
    {
        if (row is < 0 or >= Rows || column is < 0 or >= Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "The character position is outside the screen.");
        }
        if (destination.Length < 8)
        {
            throw new ArgumentException("A glyph destination needs eight bytes.", nameof(destination));
        }

        for (var y = 0; y < 8; y++)
        {
            destination[y] = m_bitmap[(row * 8 + y) * Columns + column];
        }
    }

    public void DrawLine(
        int x0,
        int y0,
        int x1,
        int y1,
        byte ink,
        byte paper,
        bool bright = false,
        bool flash = false,
        bool inverse = false,
        bool over = false,
        bool preserveAttributes = false)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            Plot(x0, y0, ink, paper, bright, flash, inverse, over, preserveAttributes);
            if (x0 == x1 && y0 == y1)
                break;
            var twiceError = error * 2;
            if (twiceError >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (twiceError <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    public void DrawCircle(
        int centerX,
        int centerY,
        int radius,
        byte ink,
        byte paper,
        bool bright = false,
        bool flash = false,
        bool inverse = false,
        bool over = false)
    {
        var x = radius;
        var y = 0;
        var error = 1 - radius;
        while (x >= y)
        {
            PlotCirclePoints(centerX, centerY, x, y, ink, paper, bright, flash, inverse, over);
            y++;
            if (error < 0)
                error += 2 * y + 1;
            else
            {
                x--;
                error += 2 * (y - x) + 1;
            }
        }
    }

    private void PlotCirclePoints(
        int centerX,
        int centerY,
        int x,
        int y,
        byte ink,
        byte paper,
        bool bright,
        bool flash,
        bool inverse,
        bool over)
    {
        Plot(centerX + x, centerY + y, ink, paper, bright, flash, inverse, over);
        Plot(centerX + y, centerY + x, ink, paper, bright, flash, inverse, over);
        Plot(centerX - y, centerY + x, ink, paper, bright, flash, inverse, over);
        Plot(centerX - x, centerY + y, ink, paper, bright, flash, inverse, over);
        Plot(centerX - x, centerY - y, ink, paper, bright, flash, inverse, over);
        Plot(centerX - y, centerY - x, ink, paper, bright, flash, inverse, over);
        Plot(centerX + y, centerY - x, ink, paper, bright, flash, inverse, over);
        Plot(centerX + x, centerY - y, ink, paper, bright, flash, inverse, over);
    }

    public void ScrollUp(byte paper, bool bright = false, int rows = Rows)
    {
        if (rows is < 1 or > Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var bitmapLength = rows * Columns * 8;
        Array.Copy(m_bitmap, Columns * 8, m_bitmap, 0, bitmapLength - Columns * 8);
        Array.Clear(m_bitmap, bitmapLength - Columns * 8, Columns * 8);
        var attributeLength = rows * Columns;
        Array.Copy(m_attributes, Columns, m_attributes, 0, attributeLength - Columns);
        Array.Fill(m_attributes, MakeAttribute(0, paper, bright), attributeLength - Columns, Columns);
    }

    public byte GetAttribute(int row, int column)
    {
        if (row is < 0 or >= Rows || column is < 0 or >= Columns)
            throw new ArgumentOutOfRangeException(nameof(row), "Coords exceed screen");
        return m_attributes[row * Columns + column];
    }

    public byte ReadMemory(int address)
    {
        if (address is >= AttributeMemoryAddress and <= LastDisplayMemoryAddress)
        {
            return m_attributes[address - AttributeMemoryAddress];
        }

        if (address is >= BitmapMemoryAddress and < AttributeMemoryAddress)
        {
            return m_bitmap[GetBitmapIndex(address - BitmapMemoryAddress)];
        }

        throw new ArgumentOutOfRangeException(nameof(address), "The address is outside Spectrum display memory.");
    }

    public void WriteMemory(int address, byte value)
    {
        if (address is >= AttributeMemoryAddress and <= LastDisplayMemoryAddress)
        {
            m_attributes[address - AttributeMemoryAddress] = value;
            return;
        }

        if (address is >= BitmapMemoryAddress and < AttributeMemoryAddress)
        {
            m_bitmap[GetBitmapIndex(address - BitmapMemoryAddress)] = value;
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(address), "The address is outside Spectrum display memory.");
    }

    private static int GetBitmapIndex(int memoryOffset)
    {
        var x = memoryOffset & 0x1f;
        var y = (memoryOffset >> 8 & 0x07) |
                (memoryOffset >> 2 & 0x38) |
                (memoryOffset >> 5 & 0xc0);
        return y * Columns + x;
    }

    public SpectrumScreen Copy()
    {
        var copy = new SpectrumScreen { BorderColor = BorderColor };
        Array.Copy(m_bitmap, copy.m_bitmap, m_bitmap.Length);
        Array.Copy(m_attributes, copy.m_attributes, m_attributes.Length);
        return copy;
    }

    public void Render(Span<uint> destination, bool flashPhase = false)
    {
        if (destination.Length < FrameWidth * FrameHeight)
            throw new ArgumentException("The destination is too small for a Spectrum frame.", nameof(destination));

        destination.Fill(SpectrumPalette.Bgra[BorderColor & 7]);

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var attribute = m_attributes[y / 8 * Columns + x / 8];
                var ink = attribute & 7;
                var paper = attribute >> 3 & 7;
                var bright = (attribute & 0x40) == 0 ? 0 : 8;
                var flash = flashPhase && (attribute & 0x80) != 0;
                var isInk = (m_bitmap[y * Columns + x / 8] & (0x80 >> (x & 7))) != 0;
                if (flash)
                    isInk = !isInk;

                var color = (isInk ? ink : paper) + bright;
                destination[(y + BorderY) * FrameWidth + x + BorderX] = SpectrumPalette.Bgra[color];
            }
        }
    }

    private static byte MakeAttribute(byte ink, byte paper, bool bright, bool flash = false)
    {
        return (byte)((ink & 7) | (paper & 7) << 3 | (bright ? 0x40 : 0) | (flash ? 0x80 : 0));
    }
}
