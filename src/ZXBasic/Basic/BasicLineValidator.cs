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

public static class BasicLineValidator
{
    private static readonly string[] StatementKeywords =
    [
        "BEEP", "BORDER", "BRIGHT", "CIRCLE", "CLEAR", "CLS", "CONTINUE", "DATA", "DEF FN", "DEFFN", "DIM", "ERASE",
        "DRAW", "FILL", "FOR", "GO SUB", "GOSUB", "GO TO", "GOTO", "IF", "INK", "INPUT", "LET", "LIST", "NEW", "NEXT", "OVER",
        "PAPER", "PAUSE", "PLOT", "POKE", "PRINT", "RANDOMIZE", "READ", "REM", "RESTORE", "RETURN", "RUN", "STOP",
        "FLASH", "INVERSE"
    ];

    public static bool IsValid(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || !HasBalancedQuotesAndParentheses(text))
            return false;
        if (IsRenumberCommand(text) || text == "RESET")
            return true;

        var statement = text;
        if (char.IsDigit(text[0]))
        {
            var digitCount = text.TakeWhile(char.IsDigit).Count();
            if (!int.TryParse(text[..digitCount], out var lineNumber) || lineNumber > 9999)
                return false;

            statement = text[digitCount..].TrimStart();
            if (statement.Length == 0)
                return true;
        }

        IReadOnlyList<BasicToken> tokens;
        try
        {
            tokens = BasicTokenizer.Tokenize(statement);
        }
        catch (BasicSyntaxException)
        {
            return false;
        }

        if (ContainsUnsupportedUsr(tokens))
            return false;

        if (tokens.Count == 0 || tokens[0].Kind != BasicTokenKind.Keyword)
            return false;

        return StatementKeywords.Any(keyword =>
            statement.Equals(keyword, StringComparison.Ordinal) ||
            statement.StartsWith(keyword + " ", StringComparison.Ordinal) ||
            statement.StartsWith(keyword + ":", StringComparison.Ordinal));
    }

    private static bool ContainsUnsupportedUsr(IReadOnlyList<BasicToken> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Keyword != BasicKeyword.Usr)
                continue;

            if (index + 1 >= tokens.Count || tokens[index + 1].Kind != BasicTokenKind.String ||
                tokens[index + 1].Text.Length != 3 ||
                char.ToUpperInvariant(tokens[index + 1].Text[1]) is < 'A' or > 'U')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRenumberCommand(string text)
    {
        var commandLength = text.StartsWith("RENUMBER", StringComparison.Ordinal) ? 8 :
            text.StartsWith("RENUM", StringComparison.Ordinal) ? 5 : 0;
        if (commandLength == 0)
        {
            return false;
        }

        var arguments = text[commandLength..].Trim();
        if (arguments.Length == 0)
        {
            return true;
        }

        var values = arguments.Split(',');
        return values.Length <= 2 && values.All(value => int.TryParse(value.Trim(), out _));
    }

    private static bool HasBalancedQuotesAndParentheses(string text)
    {
        var quoted = false;
        var depth = 0;
        foreach (var character in text)
        {
            if (character == '"')
                quoted = !quoted;
            else if (!quoted && character == '(')
                depth++;
            else if (!quoted && character == ')' && --depth < 0)
                return false;
        }

        return !quoted && depth == 0;
    }
}
