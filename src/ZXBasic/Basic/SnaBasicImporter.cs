// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;

namespace ZXBasic.Basic;

public static class SnaBasicImporter
{
    public const int SnapshotLength = 27 + 48 * 1024;
    private const int HeaderLength = 27;
    private const int FirstRamAddress = 16384;
    private const int VarsAddress = 23627;
    private const int ProgAddress = 23635;
    private const byte NumberMarker = 0x0e;
    private const byte FirstKeyword = 0xa5;

    private static readonly string[] Keywords =
    [
        "RND", "INKEY$", "PI", "FN", "POINT", "SCREEN$", "ATTR", "AT", "TAB", "VAL$", "CODE", "VAL",
        "LEN", "SIN", "COS", "TAN", "ASN", "ACS", "ATN", "LN", "EXP", "INT", "SQR", "SGN", "ABS",
        "PEEK", "IN", "USR", "STR$", "CHR$", "NOT", "BIN", "OR", "AND", "<=", ">=", "<>", "LINE",
        "THEN", "TO", "STEP", "DEF FN", "CAT", "FORMAT", "MOVE", "ERASE", "OPEN #", "CLOSE #", "MERGE",
        "VERIFY", "BEEP", "CIRCLE", "INK", "PAPER", "FLASH", "BRIGHT", "INVERSE", "OVER", "OUT", "LPRINT",
        "LLIST", "STOP", "READ", "DATA", "RESTORE", "NEW", "BORDER", "CONTINUE", "DIM", "REM", "FOR",
        "GO TO", "GO SUB", "INPUT", "LOAD", "LIST", "LET", "PAUSE", "NEXT", "POKE", "PRINT", "PLOT",
        "RUN", "SAVE", "RANDOMIZE", "IF", "CLS", "DRAW", "CLEAR", "RETURN", "COPY"
    ];

    public static IReadOnlyList<string> Import(byte[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Length != SnapshotLength)
        {
            throw new BasicSyntaxException("SNA size 49179 only", 0);
        }

        var programAddress = ReadWord(snapshot, ProgAddress);
        var variablesAddress = ReadWord(snapshot, VarsAddress);
        if (programAddress < FirstRamAddress || variablesAddress < programAddress || variablesAddress > 65535)
        {
            throw new BasicSyntaxException("Bad BASIC pointers", 0);
        }

        var lines = new List<string>();
        var address = programAddress;
        while (address < variablesAddress)
        {
            if (address + 4 > variablesAddress)
            {
                throw new BasicSyntaxException("Truncated BASIC line", 0);
            }

            var lineNumber = ReadByte(snapshot, address) * 256 + ReadByte(snapshot, address + 1);
            var lineLength = ReadByte(snapshot, address + 2) | ReadByte(snapshot, address + 3) << 8;
            var textAddress = address + 4;
            var nextAddress = textAddress + lineLength;
            if (lineNumber > 9999 || lineLength < 1 || nextAddress > variablesAddress ||
                ReadByte(snapshot, nextAddress - 1) != 0x0d)
            {
                throw new BasicSyntaxException("Invalid BASIC line", 0);
            }

            lines.Add($"{lineNumber} {Detokenize(snapshot, textAddress, nextAddress - 1)}");
            address = nextAddress;
        }

        return lines;
    }

    private static string Detokenize(byte[] snapshot, int startAddress, int endAddress)
    {
        var source = new StringBuilder();
        for (var address = startAddress; address < endAddress; address++)
        {
            var value = ReadByte(snapshot, address);
            if (value == NumberMarker)
            {
                if (address + 5 >= endAddress)
                {
                    throw new BasicSyntaxException("Truncated number", 0);
                }
                address += 5;
                continue;
            }

            if (value >= FirstKeyword)
            {
                source.Append(' ');
                source.Append(Keywords[value - FirstKeyword]);
                source.Append(' ');
                continue;
            }

            if (value is >= 0x10 and <= 0x17)
            {
                var parameterCount = value is 0x16 or 0x17 ? 2 : 1;
                address += parameterCount;
                continue;
            }

            source.Append(DecodeCharacter(value));
        }

        return CollapseSpaces(source.ToString()).Trim();
    }

    private static char DecodeCharacter(byte value)
    {
        return value switch
        {
            >= 32 and <= 126 => (char)value,
            127 => '©',
            >= 144 and <= 164 => (char)('A' + value - 144),
            _ => '?'
        };
    }

    private static string CollapseSpaces(string source)
    {
        var result = new StringBuilder(source.Length);
        var quoted = false;
        var previousWasSpace = false;
        foreach (var character in source)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    result.Append(' ');
                }
                previousWasSpace = true;
                continue;
            }

            result.Append(character);
            previousWasSpace = false;
        }
        return result.ToString();
    }

    private static int ReadWord(byte[] snapshot, int address)
    {
        return ReadByte(snapshot, address) | ReadByte(snapshot, address + 1) << 8;
    }

    private static byte ReadByte(byte[] snapshot, int address)
    {
        return snapshot[HeaderLength + address - FirstRamAddress];
    }
}
