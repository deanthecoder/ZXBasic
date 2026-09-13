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

public enum ProgramLineChange
{
    Added,
    Replaced,
    Deleted,
    Unchanged
}

public sealed class BasicProgram
{
    private readonly SortedDictionary<int, BasicProgramLine> m_lines = [];

    public IReadOnlyCollection<BasicProgramLine> Lines => m_lines.Values;

    public string? GetSourceLine(int lineNumber)
    {
        return m_lines.TryGetValue(lineNumber, out var line) ? line.ToString() : null;
    }

    public ProgramLineChange Enter(string source)
    {
        var text = source.Trim();
        if (!BasicLineValidator.IsValid(text))
            throw new BasicSyntaxException("The program line is not valid Sinclair BASIC.", 0);

        var digitCount = text.TakeWhile(char.IsDigit).Count();
        if (digitCount == 0 || !int.TryParse(text[..digitCount], out var lineNumber) || lineNumber > 9999)
            throw new BasicSyntaxException("A program line needs a number from 0 to 9999.", 0);

        var statement = text[digitCount..].Trim();
        if (statement.Length == 0)
            return m_lines.Remove(lineNumber) ? ProgramLineChange.Deleted : ProgramLineChange.Unchanged;

        var tokens = BasicTokenizer.Tokenize(statement);
        var change = m_lines.ContainsKey(lineNumber) ? ProgramLineChange.Replaced : ProgramLineChange.Added;
        m_lines[lineNumber] = new BasicProgramLine(lineNumber, statement, tokens);
        return change;
    }

    public void Clear()
    {
        m_lines.Clear();
    }

    public void ReplaceListing(IEnumerable<string> lines)
    {
        var source = string.Join('\n', lines);
        var replacement = new BasicProgram();
        replacement.EnterListing(source);
        m_lines.Clear();
        foreach (var line in replacement.Lines)
        {
            m_lines[line.Number] = line;
        }
    }

    public void Renumber(int start = 10, int step = 10)
    {
        if (start < 0 || step < 1 || m_lines.Count > 0 && (long)start + (m_lines.Count - 1L) * step > 9999)
        {
            throw new BasicSyntaxException("RENUMBER would produce a line outside 0 to 9999.", 0);
        }

        var oldLines = m_lines.Values.ToArray();
        var mapping = oldLines
            .Select((line, index) => (line.Number, NewNumber: start + index * step))
            .ToDictionary(item => item.Number, item => item.NewNumber);

        m_lines.Clear();
        foreach (var line in oldLines)
        {
            var source = RewriteLineReferences(line, mapping);
            var number = mapping[line.Number];
            m_lines[number] = new BasicProgramLine(number, source, BasicTokenizer.Tokenize(source));
        }
    }

    private static string RewriteLineReferences(BasicProgramLine line, IReadOnlyDictionary<int, int> mapping)
    {
        var replacements = new List<(int Position, int Length, string Text)>();
        for (var i = 1; i < line.Tokens.Count; i++)
        {
            var token = line.Tokens[i];
            if (token.Kind != BasicTokenKind.Number || !int.TryParse(token.Text, out var target) ||
                !mapping.TryGetValue(target, out var newTarget))
            {
                continue;
            }

            if (line.Tokens[i - 1].Keyword is
                BasicKeyword.GoTo or BasicKeyword.GoSub or BasicKeyword.Then or BasicKeyword.Restore or BasicKeyword.Run)
            {
                replacements.Add((token.Position, token.Text.Length, newTarget.ToString()));
            }
        }

        var source = line.Source;
        foreach (var replacement in replacements.OrderByDescending(item => item.Position))
        {
            source = source.Remove(replacement.Position, replacement.Length)
                .Insert(replacement.Position, replacement.Text);
        }
        return source;
    }

    public int? EnterListing(string source)
    {
        var lines = source
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim().ToUpperInvariant())
            .Where(line => line.Length > 0)
            .ToArray();

        foreach (var line in lines)
        {
            if (!char.IsDigit(line[0]) || !BasicLineValidator.IsValid(line))
            {
                throw new BasicSyntaxException("A pasted listing must contain valid numbered BASIC lines.", 0);
            }
        }

        int? lastLineNumber = null;
        foreach (var line in lines)
        {
            Enter(line);
            var digitCount = line.TakeWhile(char.IsDigit).Count();
            lastLineNumber = int.Parse(line[..digitCount]);
        }

        return lastLineNumber;
    }

    public IReadOnlyList<string> GetListing(int startLineNumber = 0)
    {
        return m_lines.Values
            .Where(line => line.Number >= startLineNumber)
            .Select(line => line.ToString())
            .ToArray();
    }

    public IReadOnlyList<string> GetAutomaticListing(int currentLineNumber, int columns, int rows)
    {
        var lines = m_lines.Values.ToArray();
        if (lines.Length == 0)
        {
            return [];
        }

        var entries = lines.Select(line =>
        {
            var marker = line.Number == currentLineNumber ? '>' : ' ';
            var text = $"{line.Number}{marker}{line.Source}";
            var requiredRows = Math.Max(1, (text.Length + columns - 1) / columns);
            return (Text: text, RequiredRows: requiredRows);
        }).ToArray();

        if (entries.Sum(entry => entry.RequiredRows) <= rows)
        {
            return entries.Select(entry => entry.Text).ToArray();
        }

        var anchor = Array.FindLastIndex(lines, line => line.Number <= currentLineNumber);
        if (anchor < 0)
        {
            anchor = 0;
        }

        var start = anchor;
        var end = anchor;
        var usedRows = entries[anchor].RequiredRows;
        while (end + 1 < entries.Length && usedRows + entries[end + 1].RequiredRows <= rows)
        {
            usedRows += entries[++end].RequiredRows;
        }

        while (start > 0 && usedRows + entries[start - 1].RequiredRows <= rows)
        {
            usedRows += entries[--start].RequiredRows;
        }

        return entries[start..(end + 1)].Select(entry => entry.Text).ToArray();
    }
}
