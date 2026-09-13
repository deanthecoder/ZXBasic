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

public sealed class SpectrumMemory
{
    public const int FirstAddress = 16384;
    public const int LastAddress = 65535;
    public const int Size = LastAddress - FirstAddress + 1;

    private readonly byte[] m_ram = new byte[Size];
    private readonly SpectrumScreen? m_screen;

    public SpectrumMemory(SpectrumScreen? screen = null)
    {
        m_screen = screen;
    }

    public byte Peek(int address)
    {
        if (m_screen != null && address is >= SpectrumScreen.BitmapMemoryAddress and <= SpectrumScreen.LastDisplayMemoryAddress)
        {
            return m_screen.ReadMemory(address);
        }

        return m_ram[GetIndex(address)];
    }

    public void Poke(int address, byte value)
    {
        m_ram[GetIndex(address)] = value;
        if (m_screen != null && address is >= SpectrumScreen.BitmapMemoryAddress and <= SpectrumScreen.LastDisplayMemoryAddress)
        {
            m_screen.WriteMemory(address, value);
        }
    }

    private static int GetIndex(int address)
    {
        if (address is < FirstAddress or > LastAddress)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The address is outside 48K Spectrum RAM.");
        }

        return address - FirstAddress;
    }
}
