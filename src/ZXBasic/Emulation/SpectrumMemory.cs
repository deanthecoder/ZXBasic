// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Diagnostics;

namespace ZXBasic.Emulation;

public sealed class SpectrumMemory
{
    public const int FirstAddress = 16384;
    public const int LastAddress = 65535;
    public const int FramesAddress = 23672;
    public const int FramesPerSecond = 50;
    private const int Size = LastAddress - FirstAddress + 1;
    private const int FramesByteCount = 3;
    private const int FramesMask = 0xFFFFFF;

    private readonly byte[] m_ram = new byte[Size];
    private readonly SpectrumScreen? m_screen;
    private readonly Func<long> m_timestampProvider;
    private readonly long m_timestampFrequency;
    private readonly int m_framesPerSecond;
    private long m_framesOriginTimestamp;
    private int m_framesOriginValue;

    public SpectrumMemory(SpectrumScreen? screen = null)
        : this(screen, Stopwatch.GetTimestamp, Stopwatch.Frequency, FramesPerSecond)
    {
    }

    internal SpectrumMemory(
        SpectrumScreen? screen,
        Func<long> timestampProvider,
        long timestampFrequency,
        int framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(timestampProvider);
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }
        if (framesPerSecond is not (50 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Frame rate must be 50 or 60 Hz.");
        }

        m_screen = screen;
        m_timestampProvider = timestampProvider;
        m_timestampFrequency = timestampFrequency;
        m_framesPerSecond = framesPerSecond;
        m_framesOriginTimestamp = timestampProvider();
    }

    public byte Peek(int address)
    {
        if (address is >= FramesAddress and < FramesAddress + FramesByteCount)
        {
            var shift = (address - FramesAddress) * 8;
            return (byte)(GetFrames(m_timestampProvider()) >> shift);
        }

        if (m_screen != null && address is >= SpectrumScreen.BitmapMemoryAddress and <= SpectrumScreen.LastDisplayMemoryAddress)
        {
            return m_screen.ReadMemory(address);
        }

        return m_ram[GetIndex(address)];
    }

    public void Poke(int address, byte value)
    {
        if (address is >= FramesAddress and < FramesAddress + FramesByteCount)
        {
            var timestamp = m_timestampProvider();
            var shift = (address - FramesAddress) * 8;
            var mask = 0xFF << shift;
            m_framesOriginValue = (GetFrames(timestamp) & ~mask) | (value << shift);
            m_framesOriginTimestamp = timestamp;
            return;
        }

        m_ram[GetIndex(address)] = value;
        if (m_screen != null && address is >= SpectrumScreen.BitmapMemoryAddress and <= SpectrumScreen.LastDisplayMemoryAddress)
        {
            m_screen.WriteMemory(address, value);
        }
    }

    private int GetFrames(long timestamp)
    {
        var elapsedTicks = Math.Max(0, timestamp - m_framesOriginTimestamp);
        var elapsedFrames = (long)((Int128)elapsedTicks * m_framesPerSecond / m_timestampFrequency);
        return (int)((m_framesOriginValue + elapsedFrames) & FramesMask);
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
