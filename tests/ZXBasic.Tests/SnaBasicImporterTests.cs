// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ZXBasic.Basic;

namespace ZXBasic.Tests;

public class SnaBasicImporterTests
{
    [Test]
    public void ImportsTokenizedBasicAndSkipsStoredNumbers()
    {
        var snapshot = CreateSnapshot(
            23755,
            (10, [0xe7, (byte)'1', 0x0e, 0, 0, 1, 0, 0, (byte)':', 0xf5, (byte)'"', (byte)'H', (byte)'I', (byte)'"']));

        var lines = SnaBasicImporter.Import(snapshot);

        Assert.That(lines, Is.EqualTo(new[] { "10 BORDER 1: PRINT \"HI\"" }));
    }

    [Test]
    public void RejectsAnInvalidSnapshotLength()
    {
        Assert.That(
            () => SnaBasicImporter.Import(new byte[100]),
            Throws.TypeOf<BasicSyntaxException>());
    }

    private static byte[] CreateSnapshot(int programAddress, params (int Number, byte[] Source)[] lines)
    {
        var snapshot = new byte[SnaBasicImporter.SnapshotLength];
        var address = programAddress;
        foreach (var (number, source) in lines)
        {
            WriteByte(snapshot, address++, (byte)(number >> 8));
            WriteByte(snapshot, address++, (byte)number);
            var length = source.Length + 1;
            WriteByte(snapshot, address++, (byte)length);
            WriteByte(snapshot, address++, (byte)(length >> 8));
            foreach (var value in source)
            {
                WriteByte(snapshot, address++, value);
            }
            WriteByte(snapshot, address++, 0x0d);
        }

        WriteWord(snapshot, 23635, programAddress);
        WriteWord(snapshot, 23627, address);
        return snapshot;
    }

    private static void WriteWord(byte[] snapshot, int address, int value)
    {
        WriteByte(snapshot, address, (byte)value);
        WriteByte(snapshot, address + 1, (byte)(value >> 8));
    }

    private static void WriteByte(byte[] snapshot, int address, byte value)
    {
        snapshot[27 + address - 16384] = value;
    }
}
