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

public static class BasicTokenizer
{
    private static readonly Dictionary<string, BasicKeyword> Keywords = new(StringComparer.Ordinal)
    {
        ["RND"] = BasicKeyword.Rnd,
        ["INKEY$"] = BasicKeyword.InkeyString,
        ["PI"] = BasicKeyword.Pi,
        ["FN"] = BasicKeyword.Fn,
        ["POINT"] = BasicKeyword.Point,
        ["SCREEN$"] = BasicKeyword.ScreenString,
        ["ATTR"] = BasicKeyword.Attr,
        ["AT"] = BasicKeyword.At,
        ["TAB"] = BasicKeyword.Tab,
        ["VAL$"] = BasicKeyword.ValString,
        ["CODE"] = BasicKeyword.Code,
        ["VAL"] = BasicKeyword.Val,
        ["LEN"] = BasicKeyword.Len,
        ["SIN"] = BasicKeyword.Sin,
        ["COS"] = BasicKeyword.Cos,
        ["TAN"] = BasicKeyword.Tan,
        ["ASN"] = BasicKeyword.Asn,
        ["ACS"] = BasicKeyword.Acs,
        ["ATN"] = BasicKeyword.Atn,
        ["LN"] = BasicKeyword.Ln,
        ["EXP"] = BasicKeyword.Exp,
        ["INT"] = BasicKeyword.Int,
        ["SQR"] = BasicKeyword.Sqr,
        ["SGN"] = BasicKeyword.Sgn,
        ["ABS"] = BasicKeyword.Abs,
        ["PEEK"] = BasicKeyword.Peek,
        ["IN"] = BasicKeyword.In,
        ["USR"] = BasicKeyword.Usr,
        ["STR$"] = BasicKeyword.StrString,
        ["CHR$"] = BasicKeyword.ChrString,
        ["NOT"] = BasicKeyword.Not,
        ["BIN"] = BasicKeyword.Bin,
        ["OR"] = BasicKeyword.Or,
        ["AND"] = BasicKeyword.And,
        ["LINE"] = BasicKeyword.Line,
        ["THEN"] = BasicKeyword.Then,
        ["TO"] = BasicKeyword.To,
        ["STEP"] = BasicKeyword.Step,
        ["DEF FN"] = BasicKeyword.DefFn,
        ["DEFFN"] = BasicKeyword.DefFn,
        ["BEEP"] = BasicKeyword.Beep,
        ["CIRCLE"] = BasicKeyword.Circle,
        ["INK"] = BasicKeyword.Ink,
        ["PAPER"] = BasicKeyword.Paper,
        ["FLASH"] = BasicKeyword.Flash,
        ["BRIGHT"] = BasicKeyword.Bright,
        ["INVERSE"] = BasicKeyword.Inverse,
        ["OVER"] = BasicKeyword.Over,
        ["STOP"] = BasicKeyword.Stop,
        ["READ"] = BasicKeyword.Read,
        ["DATA"] = BasicKeyword.Data,
        ["RESTORE"] = BasicKeyword.Restore,
        ["NEW"] = BasicKeyword.New,
        ["BORDER"] = BasicKeyword.Border,
        ["CONTINUE"] = BasicKeyword.Continue,
        ["ERASE"] = BasicKeyword.Erase,
        ["DIM"] = BasicKeyword.Dim,
        ["REM"] = BasicKeyword.Rem,
        ["FOR"] = BasicKeyword.For,
        ["GOTO"] = BasicKeyword.GoTo,
        ["GO TO"] = BasicKeyword.GoTo,
        ["GOSUB"] = BasicKeyword.GoSub,
        ["GO SUB"] = BasicKeyword.GoSub,
        ["INPUT"] = BasicKeyword.Input,
        ["LIST"] = BasicKeyword.List,
        ["LET"] = BasicKeyword.Let,
        ["PAUSE"] = BasicKeyword.Pause,
        ["NEXT"] = BasicKeyword.Next,
        ["POKE"] = BasicKeyword.Poke,
        ["PRINT"] = BasicKeyword.Print,
        ["PLOT"] = BasicKeyword.Plot,
        ["RUN"] = BasicKeyword.Run,
        ["RANDOMIZE"] = BasicKeyword.Randomize,
        ["IF"] = BasicKeyword.If,
        ["CLS"] = BasicKeyword.Cls,
        ["DRAW"] = BasicKeyword.Draw,
        ["CLEAR"] = BasicKeyword.Clear,
        ["RETURN"] = BasicKeyword.Return
    };

    private static readonly string[] OrderedKeywords = Keywords.Keys.OrderByDescending(keyword => keyword.Length).ToArray();

    public static IReadOnlyList<BasicToken> Tokenize(string source)
    {
        var tokens = new List<BasicToken>();
        var position = 0;
        while (position < source.Length)
        {
            if (char.IsWhiteSpace(source[position]))
            {
                position++;
                continue;
            }

            if (source[position] == '"')
            {
                tokens.Add(ReadString(source, ref position));
                continue;
            }

            if (char.IsDigit(source[position]) || source[position] == '.' && position + 1 < source.Length && char.IsDigit(source[position + 1]))
            {
                tokens.Add(ReadNumber(source, ref position));
                continue;
            }

            if (TryReadKeyword(source, ref position, out var keywordToken))
            {
                tokens.Add(keywordToken);
                if (keywordToken.Keyword == BasicKeyword.Rem && position < source.Length)
                {
                    tokens.Add(new BasicToken(BasicTokenKind.Comment, source[position..], position));
                    break;
                }
                continue;
            }

            if (char.IsLetter(source[position]) || source[position] == '_')
            {
                tokens.Add(ReadIdentifier(source, ref position));
                continue;
            }

            tokens.Add(ReadSymbol(source, ref position));
        }

        return tokens;
    }

    private static BasicToken ReadString(string source, ref int position)
    {
        var start = position++;
        while (position < source.Length && source[position] != '"')
            position++;

        if (position == source.Length)
            throw new BasicSyntaxException("Unterminated string", start);

        position++;
        return new BasicToken(BasicTokenKind.String, source[start..position], start);
    }

    private static BasicToken ReadNumber(string source, ref int position)
    {
        var start = position;
        var hasDecimalPoint = false;
        while (position < source.Length && (char.IsDigit(source[position]) || source[position] == '.'))
        {
            if (source[position] == '.' && hasDecimalPoint)
                throw new BasicSyntaxException("Invalid decimal", position);

            hasDecimalPoint |= source[position] == '.';
            position++;
        }

        if (position < source.Length && source[position] == 'E')
        {
            position++;
            if (position < source.Length && source[position] is '+' or '-')
                position++;

            var exponentStart = position;
            while (position < source.Length && char.IsDigit(source[position]))
                position++;
            if (position == exponentStart)
                throw new BasicSyntaxException("Exponent needs number", position);
        }

        return new BasicToken(BasicTokenKind.Number, source[start..position], start);
    }

    private static bool TryReadKeyword(string source, ref int position, out BasicToken token)
    {
        foreach (var keywordText in OrderedKeywords)
        {
            if (!source.AsSpan(position).StartsWith(keywordText, StringComparison.Ordinal) ||
                !HasTokenBoundary(source, position + keywordText.Length))
                continue;

            token = new BasicToken(BasicTokenKind.Keyword, keywordText, position, Keywords[keywordText]);
            position += keywordText.Length;
            return true;
        }

        token = default;
        return false;
    }

    private static bool HasTokenBoundary(string source, int position)
    {
        return position == source.Length || !char.IsLetterOrDigit(source[position]) && source[position] != '$';
    }

    private static BasicToken ReadIdentifier(string source, ref int position)
    {
        var start = position++;
        while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] is '$' or '_'))
            position++;

        return new BasicToken(BasicTokenKind.Identifier, source[start..position], start);
    }

    private static BasicToken ReadSymbol(string source, ref int position)
    {
        var start = position;
        var remaining = source.AsSpan(position);
        foreach (var (text, keyword) in new[]
                 {
                     ("<=", BasicKeyword.LessThanOrEqual),
                     (">=", BasicKeyword.GreaterThanOrEqual),
                     ("<>", BasicKeyword.NotEqual)
                 })
        {
            if (!remaining.StartsWith(text, StringComparison.Ordinal))
                continue;

            position += text.Length;
            return new BasicToken(BasicTokenKind.Keyword, text, start, keyword);
        }

        var character = source[position++];
        if ("+-*/↑^=<>¬".Contains(character))
            return new BasicToken(BasicTokenKind.Operator, character.ToString(), start);
        if ("(),;:'".Contains(character))
            return new BasicToken(BasicTokenKind.Separator, character.ToString(), start);

        throw new BasicSyntaxException($"Bad char '{character}'", start);
    }
}
