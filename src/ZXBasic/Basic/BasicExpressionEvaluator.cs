// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;

namespace ZXBasic.Basic;

public sealed class BasicExpressionEvaluator
{
    private readonly BasicRuntime m_runtime;

    public BasicExpressionEvaluator(BasicRuntime runtime)
    {
        m_runtime = runtime;
    }

    public double Evaluate(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count == 0)
            throw new BasicSyntaxException("Expected an expression.", 0);

        var parser = new Parser(tokens, m_runtime);
        var value = parser.ParseExpression();
        if (!parser.IsAtEnd)
            throw new BasicSyntaxException("Unexpected text after the expression.", parser.Position);
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new BasicSyntaxException("Number out of range.", parser.Position);

        return value;
    }

    public string EvaluateString(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count == 0)
        {
            throw new BasicSyntaxException("Expected a string expression.", 0);
        }

        var parser = new Parser(tokens, m_runtime);
        var value = parser.ParseStringExpression();
        if (!parser.IsAtEnd)
        {
            throw new BasicSyntaxException("Unexpected text after the string expression.", parser.Position);
        }

        return value;
    }

    public static bool IsStringExpression(IReadOnlyList<BasicToken> tokens, int start = 0)
    {
        return start < tokens.Count && Parser.IsStringStart(tokens[start]);
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<BasicToken> m_tokens;
        private readonly BasicRuntime m_runtime;
        private int m_index;

        public bool IsAtEnd => m_index == m_tokens.Count;
        public int Position => IsAtEnd ? m_tokens[^1].Position + m_tokens[^1].Text.Length : Current.Position;
        private BasicToken Current => m_tokens[m_index];

        public Parser(IReadOnlyList<BasicToken> tokens, BasicRuntime runtime)
        {
            m_tokens = tokens;
            m_runtime = runtime;
        }

        public double ParseExpression()
        {
            return ParseOr();
        }

        private double ParseOr()
        {
            var value = ParseAnd();
            while (MatchKeyword(BasicKeyword.Or))
            {
                var right = ParseAnd();
                value = IsTrue(right) ? 1 : value;
            }
            return value;
        }

        private double ParseAnd()
        {
            var value = ParseComparison();
            while (MatchKeyword(BasicKeyword.And))
            {
                var right = ParseComparison();
                value = IsTrue(right) ? value : 0;
            }
            return value;
        }

        private double ParseComparison()
        {
            if (!IsAtEnd && IsStringStart(Current))
            {
                var leftText = ParseStringExpression();
                if (IsAtEnd || !IsComparison(Current))
                {
                    throw new BasicSyntaxException("A string value needs a comparison here.", Position);
                }

                var operation = Current;
                m_index++;
                var rightText = ParseStringExpression();
                var comparison = string.CompareOrdinal(leftText, rightText);
                return operation.Keyword switch
                {
                    BasicKeyword.LessThanOrEqual => comparison <= 0 ? 1 : 0,
                    BasicKeyword.GreaterThanOrEqual => comparison >= 0 ? 1 : 0,
                    BasicKeyword.NotEqual => comparison != 0 ? 1 : 0,
                    _ when operation.Text == "=" => comparison == 0 ? 1 : 0,
                    _ when operation.Text == "<" => comparison < 0 ? 1 : 0,
                    _ => comparison > 0 ? 1 : 0
                };
            }

            var value = ParseAddition();
            while (!IsAtEnd)
            {
                var operation = Current;
                if (!IsComparison(operation))
                    break;
                m_index++;
                var right = ParseAddition();
                value = operation.Keyword switch
                {
                    BasicKeyword.LessThanOrEqual => value <= right ? 1 : 0,
                    BasicKeyword.GreaterThanOrEqual => value >= right ? 1 : 0,
                    BasicKeyword.NotEqual => value != right ? 1 : 0,
                    _ when operation.Text == "=" => value == right ? 1 : 0,
                    _ when operation.Text == "<" => value < right ? 1 : 0,
                    _ => value > right ? 1 : 0
                };
            }
            return value;
        }

        private double ParseAddition()
        {
            var value = ParseMultiplication();
            while (MatchOperator(out var operation, "+", "-"))
            {
                var right = ParseMultiplication();
                value = operation == "+" ? value + right : value - right;
            }
            return value;
        }

        private double ParseMultiplication()
        {
            var value = ParsePower();
            while (MatchOperator(out var operation, "*", "/"))
            {
                var right = ParsePower();
                if (operation == "/" && right == 0)
                    throw new BasicSyntaxException("Division by zero.", Position);
                value = operation == "*" ? value * right : value / right;
            }
            return value;
        }

        private double ParsePower()
        {
            var value = ParseUnary();
            if (MatchOperator(out _, "↑", "^"))
                value = Math.Pow(value, ParsePower());
            return value;
        }

        private double ParseUnary()
        {
            if (MatchOperator(out var operation, "+", "-"))
            {
                var value = ParseUnary();
                return operation == "-" ? -value : value;
            }

            if (MatchKeyword(BasicKeyword.Not))
                return IsTrue(ParseUnary()) ? 0 : 1;

            if (MatchKeyword(BasicKeyword.Bin))
            {
                if (IsAtEnd || Current.Kind != BasicTokenKind.Number ||
                    Current.Text.Length > 16 || Current.Text.Any(character => character is not ('0' or '1')))
                {
                    throw new BasicSyntaxException("BIN needs up to 16 binary digits.", Position);
                }

                var value = Convert.ToInt32(Current.Text, 2);
                m_index++;
                return value;
            }

            if (!IsAtEnd && Current.Keyword is BasicKeyword.Len or BasicKeyword.Code or BasicKeyword.Val)
            {
                var function = Current.Keyword!.Value;
                m_index++;
                var text = ParseStringPrimary();
                return function switch
                {
                    BasicKeyword.Len => text.Length,
                    BasicKeyword.Code => text.Length == 0 ? 0 : text[0],
                    BasicKeyword.Val => ParseNumber(text),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }

            if (!IsAtEnd && Current.Kind == BasicTokenKind.Keyword && IsFunction(Current.Keyword))
            {
                var function = Current.Keyword!.Value;
                m_index++;
                return ApplyFunction(function, ParseUnary());
            }

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (IsAtEnd)
                throw new BasicSyntaxException("Expected a number, variable or function.", Position);

            var token = Current;
            if (token.Kind == BasicTokenKind.Number)
            {
                m_index++;
                return double.Parse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (token.Kind == BasicTokenKind.Identifier)
            {
                m_index++;
                if (MatchSeparator("("))
                {
                    var indices = ParseArgumentList()
                        .Select(value => checked((int)Math.Round(value, MidpointRounding.AwayFromZero)))
                        .ToArray();
                    return m_runtime.GetArrayValue(token.Text, indices);
                }
                return m_runtime.GetVariable(token.Text);
            }

            if (token.Keyword == BasicKeyword.Fn)
            {
                m_index++;
                if (IsAtEnd || Current.Kind != BasicTokenKind.Identifier)
                    throw new BasicSyntaxException("FN needs a function name.", Position);
                var name = Current.Text;
                m_index++;
                RequireSeparator("(");
                return m_runtime.InvokeFunction(name, ParseArgumentList());
            }

            if (token.Keyword == BasicKeyword.Usr)
            {
                m_index++;
                var character = ParseStringPrimary();
                if (character.Length != 1)
                    throw new BasicSyntaxException("USR needs one UDG letter.", token.Position);
                return BasicRuntime.GetUdgAddress(character[0]);
            }

            if (token.Keyword == BasicKeyword.Attr)
            {
                m_index++;
                RequireSeparator("(");
                var arguments = ParseArgumentList();
                if (arguments.Count != 2)
                    throw new BasicSyntaxException("ATTR needs row and column.", token.Position);
                var row = checked((int)Math.Round(arguments[0], MidpointRounding.AwayFromZero));
                var column = checked((int)Math.Round(arguments[1], MidpointRounding.AwayFromZero));
                try
                {
                    return m_runtime.Screen.GetAttribute(row, column);
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new BasicSyntaxException("ATTR coordinates are outside the screen.", token.Position);
                }
            }

            if (token.Keyword == BasicKeyword.Point)
            {
                m_index++;
                RequireSeparator("(");
                var arguments = ParseArgumentList();
                if (arguments.Count != 2)
                    throw new BasicSyntaxException("POINT needs x and y coordinates.", token.Position);
                var x = checked((int)Math.Round(arguments[0], MidpointRounding.AwayFromZero));
                var y = checked((int)Math.Round(arguments[1], MidpointRounding.AwayFromZero));
                return m_runtime.Screen.IsPixelSet(x, y) ? 1 : 0;
            }

            if (token.Keyword == BasicKeyword.Pi)
            {
                m_index++;
                return Math.PI;
            }

            if (token.Keyword == BasicKeyword.Rnd)
            {
                m_index++;
                return m_runtime.Random.NextDouble();
            }

            if (MatchSeparator("("))
            {
                var value = ParseExpression();
                RequireSeparator(")");
                return value;
            }

            throw new BasicSyntaxException("Expected a number, variable or function.", token.Position);
        }

        public string ParseStringExpression()
        {
            var value = ParseStringPrimary();
            while (MatchOperator(out _, "+"))
            {
                value += ParseStringPrimary();
            }
            return value;
        }

        private string ParseStringPrimary()
        {
            if (IsAtEnd)
            {
                throw new BasicSyntaxException("Expected a string value.", Position);
            }

            var token = Current;
            if (token.Kind == BasicTokenKind.String)
            {
                m_index++;
                return ApplySlice(token.Text[1..^1]);
            }

            if (token.Kind == BasicTokenKind.Identifier && token.Text.EndsWith('$'))
            {
                m_index++;
                if (m_runtime.TryGetStringArrayRank(token.Text, out var rank) && rank > 0)
                {
                    RequireSeparator("(");
                    var indices = ParseArgumentList()
                        .Select(value => checked((int)Math.Round(value, MidpointRounding.AwayFromZero)))
                        .ToArray();
                    return ApplySlice(m_runtime.GetStringArrayValue(token.Text, indices));
                }
                return ApplySlice(m_runtime.GetStringVariable(token.Text));
            }

            if (token.Keyword == BasicKeyword.ScreenString)
            {
                m_index++;
                RequireSeparator("(");
                var row = checked((int)Math.Round(ParseExpression(), MidpointRounding.AwayFromZero));
                RequireSeparator(",");
                var column = checked((int)Math.Round(ParseExpression(), MidpointRounding.AwayFromZero));
                RequireSeparator(")");
                return ApplySlice(m_runtime.ReadScreenCharacter(row, column));
            }

            if (token.Keyword == BasicKeyword.InkeyString)
            {
                m_index++;
                return ApplySlice(m_runtime.ReadInkey());
            }

            if (token.Keyword == BasicKeyword.ChrString)
            {
                m_index++;
                var code = checked((int)Math.Round(ParseUnary(), MidpointRounding.AwayFromZero));
                if (code is < 0 or > 255)
                {
                    throw new BasicSyntaxException("CHR$ needs a value from 0 to 255.", token.Position);
                }
                return ((char)code).ToString();
            }

            if (token.Keyword == BasicKeyword.StrString)
            {
                m_index++;
                return ParseUnary().ToString("G10", CultureInfo.InvariantCulture);
            }

            if (token.Keyword == BasicKeyword.ValString)
            {
                m_index++;
                var source = ParseStringPrimary();
                try
                {
                    return new BasicExpressionEvaluator(m_runtime).EvaluateString(BasicTokenizer.Tokenize(source));
                }
                catch (BasicSyntaxException)
                {
                    throw new BasicSyntaxException("VAL$ needs a valid string expression.", token.Position);
                }
            }

            if (MatchSeparator("("))
            {
                var value = ParseStringExpression();
                RequireSeparator(")");
                return value;
            }

            throw new BasicSyntaxException("Expected a string value.", token.Position);
        }

        private string ApplySlice(string value)
        {
            if (!MatchSeparator("("))
            {
                return value;
            }

            var start = 1;
            var hasRange = MatchKeyword(BasicKeyword.To);
            if (!hasRange)
            {
                start = checked((int)Math.Round(ParseExpression(), MidpointRounding.AwayFromZero));
                hasRange = MatchKeyword(BasicKeyword.To);
            }

            var end = start;
            if (hasRange)
            {
                end = !IsAtEnd && Current.Text != ")"
                    ? checked((int)Math.Round(ParseExpression(), MidpointRounding.AwayFromZero))
                    : value.Length;
            }

            RequireSeparator(")");
            if (start < 1 || end < start || end > value.Length)
            {
                throw new BasicSyntaxException("String subscript is out of range.", Position);
            }

            return value[(start - 1)..end];
        }

        public static bool IsStringStart(BasicToken token)
        {
            return token.Kind == BasicTokenKind.String ||
                token.Kind == BasicTokenKind.Identifier && token.Text.EndsWith('$') ||
                token.Keyword is BasicKeyword.ChrString or BasicKeyword.StrString or BasicKeyword.ScreenString or
                BasicKeyword.InkeyString or BasicKeyword.ValString;
        }

        private static double ParseNumber(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new BasicSyntaxException("VAL needs a numeric string.", 0);
            }
            return value;
        }

        private IReadOnlyList<double> ParseArgumentList()
        {
            var arguments = new List<double>();
            if (MatchSeparator(")"))
                return arguments;

            while (true)
            {
                arguments.Add(ParseExpression());
                if (MatchSeparator(")"))
                    return arguments;
                if (!MatchSeparator(","))
                    throw new BasicSyntaxException("Expected ',' or ')'.", Position);
            }
        }

        private static bool IsFunction(BasicKeyword? keyword)
        {
            return keyword is BasicKeyword.Sin or BasicKeyword.Cos or BasicKeyword.Tan or
                BasicKeyword.Asn or BasicKeyword.Acs or BasicKeyword.Atn or BasicKeyword.Ln or
                BasicKeyword.Exp or BasicKeyword.Int or BasicKeyword.Sqr or BasicKeyword.Sgn or BasicKeyword.Abs or
                BasicKeyword.Peek;
        }

        private double ApplyFunction(BasicKeyword keyword, double value)
        {
            return keyword switch
            {
                BasicKeyword.Sin => Math.Sin(value),
                BasicKeyword.Cos => Math.Cos(value),
                BasicKeyword.Tan => Math.Tan(value),
                BasicKeyword.Asn => Math.Asin(value),
                BasicKeyword.Acs => Math.Acos(value),
                BasicKeyword.Atn => Math.Atan(value),
                BasicKeyword.Ln => Math.Log(value),
                BasicKeyword.Exp => Math.Exp(value),
                BasicKeyword.Int => Math.Floor(value),
                BasicKeyword.Sqr => Math.Sqrt(value),
                BasicKeyword.Sgn => Math.Sign(value),
                BasicKeyword.Abs => Math.Abs(value),
                BasicKeyword.Peek => Peek(value),
                _ => throw new ArgumentOutOfRangeException(nameof(keyword))
            };
        }

        private double Peek(double value)
        {
            var address = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
            try
            {
                return m_runtime.Memory.Peek(address);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new BasicSyntaxException("PEEK address is outside 48K Spectrum RAM.", Position);
            }
        }

        private bool MatchKeyword(BasicKeyword keyword)
        {
            if (IsAtEnd || Current.Keyword != keyword)
                return false;
            m_index++;
            return true;
        }

        private bool MatchOperator(out string operation, params string[] candidates)
        {
            operation = string.Empty;
            if (IsAtEnd || Current.Kind != BasicTokenKind.Operator || !candidates.Contains(Current.Text))
                return false;
            operation = Current.Text;
            m_index++;
            return true;
        }

        private bool MatchSeparator(string separator)
        {
            if (IsAtEnd || Current is not { Kind: BasicTokenKind.Separator } || Current.Text != separator)
                return false;
            m_index++;
            return true;
        }

        private void RequireSeparator(string separator)
        {
            if (!MatchSeparator(separator))
                throw new BasicSyntaxException($"Expected '{separator}'.", Position);
        }

        private static bool IsComparison(BasicToken token)
        {
            return token.Keyword is BasicKeyword.LessThanOrEqual or BasicKeyword.GreaterThanOrEqual or BasicKeyword.NotEqual ||
                token is { Kind: BasicTokenKind.Operator, Text: "=" or "<" or ">" };
        }

        private static bool IsTrue(double value)
        {
            return value != 0;
        }
    }
}
