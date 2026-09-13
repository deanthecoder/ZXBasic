// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ZXBasic.Emulation;

namespace ZXBasic.Basic;

public sealed class BasicStatementExecutor
{
    private readonly BasicExpressionEvaluator m_expressionEvaluator;

    public BasicRuntime Runtime { get; }

    public BasicStatementExecutor(SpectrumScreen screen, SpectrumFont? font = null)
        : this(new BasicRuntime(screen, font))
    {
    }

    public BasicStatementExecutor(BasicRuntime runtime)
    {
        Runtime = runtime;
        m_expressionEvaluator = new BasicExpressionEvaluator(runtime);
    }

    public bool TryExecute(IReadOnlyList<BasicToken> tokens)
    {
        return Execute(tokens).Handled;
    }

    public BasicStatementResult ExecuteSequence(IReadOnlyList<BasicToken> tokens)
    {
        var statements = SplitTopLevel(tokens, 0, ":");
        if (statements.Any(statement => statement.Count == 0))
        {
            throw new BasicSyntaxException("A direct statement cannot be empty.", 0);
        }

        var result = BasicStatementResult.Continue;
        foreach (var statement in statements)
        {
            result = Execute(statement);
            if (!result.Handled || result.Flow != BasicStatementFlow.Continue)
            {
                return result;
            }
        }
        return result;
    }

    public BasicStatementResult Execute(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count == 0 || tokens[0].Kind != BasicTokenKind.Keyword)
            throw new BasicSyntaxException("A statement must begin with a command.", 0);

        return tokens[0].Keyword switch
        {
            BasicKeyword.Border => ExecuteColorValue(tokens, color => Runtime.Screen.BorderColor = color, "BORDER", 7),
            BasicKeyword.Ink => ExecuteInk(tokens),
            BasicKeyword.Paper => ExecuteColorValue(tokens, color => Runtime.Paper = color, "PAPER", 7),
            BasicKeyword.Bright => ExecuteBooleanValue(tokens, value => Runtime.Bright = value, "BRIGHT"),
            BasicKeyword.Flash => ExecuteBooleanValue(tokens, value => Runtime.Flash = value, "FLASH"),
            BasicKeyword.Inverse => ExecuteBooleanValue(tokens, value => Runtime.Inverse = value, "INVERSE"),
            BasicKeyword.Over => ExecuteBooleanValue(tokens, value => Runtime.Over = value, "OVER"),
            BasicKeyword.Cls => ExecuteCls(tokens),
            BasicKeyword.Let => ExecuteLet(tokens),
            BasicKeyword.Dim => ExecuteDim(tokens),
            BasicKeyword.Erase => ExecuteErase(tokens),
            BasicKeyword.Plot => ExecutePlot(tokens),
            BasicKeyword.Draw => ExecuteDraw(tokens),
            BasicKeyword.Circle => ExecuteCircle(tokens),
            BasicKeyword.Print => ExecutePrint(tokens),
            BasicKeyword.Rem => BasicStatementResult.Continue,
            BasicKeyword.Pause => ExecutePause(tokens),
            BasicKeyword.Beep => ExecuteBeep(tokens),
            BasicKeyword.Randomize => ExecuteRandomize(tokens),
            BasicKeyword.Poke => ExecutePoke(tokens),
            BasicKeyword.Data => BasicStatementResult.Continue,
            BasicKeyword.Read => ExecuteRead(tokens),
            BasicKeyword.Restore => ExecuteRestore(tokens),
            BasicKeyword.Clear => ExecuteClear(tokens),
            BasicKeyword.Stop => RequireNoArguments(tokens, BasicStatementResult.Stop),
            _ => BasicStatementResult.NotHandled
        };
    }

    private BasicStatementResult ExecuteColorValue(
        IReadOnlyList<BasicToken> tokens,
        Action<byte> assign,
        string command,
        byte maximum)
    {
        var value = ToInteger(Evaluate(tokens, 1));
        if (value is < 0 || value > maximum)
            throw new BasicSyntaxException($"{command} needs an integer from 0 to {maximum}.", ArgumentPosition(tokens));

        assign((byte)value);
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteInk(IReadOnlyList<BasicToken> tokens)
    {
        var value = ToInteger(Evaluate(tokens, 1));
        if (value is < 0 or > 9)
        {
            throw new BasicSyntaxException("INK needs a value from 0 to 9.", ArgumentPosition(tokens));
        }

        if (value < 8)
        {
            Runtime.Ink = (byte)value;
        }
        else if (value == 9)
        {
            Runtime.Ink = Runtime.Paper < 4 ? (byte)7 : (byte)0;
        }

        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteBooleanValue(
        IReadOnlyList<BasicToken> tokens,
        Action<bool> assign,
        string command)
    {
        var value = ToInteger(Evaluate(tokens, 1));
        if (value is < 0 or > 1)
            throw new BasicSyntaxException($"{command} needs 0 or 1.", ArgumentPosition(tokens));

        assign(value == 1);
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteCls(IReadOnlyList<BasicToken> tokens)
    {
        RequireNoArguments(tokens);
        Runtime.ClearScreen();
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteLet(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count < 4 || tokens[1].Kind != BasicTokenKind.Identifier)
            throw new BasicSyntaxException("LET needs a variable and an expression.", ArgumentPosition(tokens));

        var equals = FindTopLevel(tokens, 2, "=");
        if (equals < 0 || equals == tokens.Count - 1)
            throw new BasicSyntaxException("LET needs '=' and an expression.", ArgumentPosition(tokens));

        if (tokens[1].Text.EndsWith('$'))
        {
            var stringValue = m_expressionEvaluator.EvaluateString(tokens.Skip(equals + 1).ToArray());
            if (equals == 2)
            {
                Runtime.SetStringVariable(tokens[1].Text, stringValue);
                return BasicStatementResult.Continue;
            }

            if (tokens[2].Text != "(" || tokens[equals - 1].Text != ")")
            {
                throw new BasicSyntaxException("Invalid string array assignment.", tokens[1].Position);
            }

            var indices = EvaluateArguments(tokens, 3, equals - 1).Select(ToInteger).ToArray();
            Runtime.SetStringArrayValue(tokens[1].Text, indices, stringValue);
            return BasicStatementResult.Continue;
        }

        var value = Evaluate(tokens, equals + 1, tokens.Count);
        if (equals == 2)
            Runtime.SetVariable(tokens[1].Text, value);
        else
        {
            if (tokens[2].Text != "(" || tokens[equals - 1].Text != ")")
                throw new BasicSyntaxException("Invalid array assignment.", tokens[2].Position);
            var indices = EvaluateArguments(tokens, 3, equals - 1).Select(ToInteger).ToArray();
            Runtime.SetArrayValue(tokens[1].Text, indices, value);
        }
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteDim(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count < 5 || tokens[1].Kind != BasicTokenKind.Identifier ||
            tokens[2].Text != "(" || tokens[^1].Text != ")")
            throw new BasicSyntaxException("DIM needs an array name and dimensions.", ArgumentPosition(tokens));

        var dimensions = EvaluateArguments(tokens, 3, tokens.Count - 1).Select(ToInteger).ToArray();
        if (tokens[1].Text.EndsWith('$'))
        {
            Runtime.DefineStringArray(tokens[1].Text, dimensions);
        }
        else
        {
            Runtime.DefineArray(tokens[1].Text, dimensions);
        }
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteErase(IReadOnlyList<BasicToken> tokens)
    {
        var names = SplitTopLevel(tokens, 1, ",");
        if (names.Count == 0 || names.Any(name => name.Count != 1 || name[0].Kind != BasicTokenKind.Identifier))
        {
            throw new BasicSyntaxException("ERASE needs one or more array names.", ArgumentPosition(tokens));
        }

        foreach (var name in names)
        {
            Runtime.EraseArray(name[0].Text);
        }
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecutePlot(IReadOnlyList<BasicToken> tokens)
    {
        var ink = Runtime.Ink;
        var paper = Runtime.Paper;
        var bright = Runtime.Bright;
        var flash = Runtime.Flash;
        var inverse = Runtime.Inverse;
        var over = Runtime.Over;
        var position = 1;

        while (position < tokens.Count && tokens[position].Keyword is
               BasicKeyword.Ink or BasicKeyword.Paper or BasicKeyword.Bright or
               BasicKeyword.Flash or BasicKeyword.Inverse or BasicKeyword.Over)
        {
            var modifier = tokens[position++].Keyword!.Value;
            var semicolon = FindTopLevel(tokens, position, ";");
            if (semicolon < 0)
                throw new BasicSyntaxException("A PLOT color item must end with ';'.", tokens[position - 1].Position);

            var value = ToInteger(Evaluate(tokens, position, semicolon));
            if (modifier == BasicKeyword.Bright)
            {
                if (value is < 0 or > 1)
                    throw new BasicSyntaxException("BRIGHT needs 0 or 1.", tokens[position - 1].Position);
                bright = value == 1;
            }
            else if (modifier is BasicKeyword.Flash or BasicKeyword.Inverse or BasicKeyword.Over)
            {
                if (value is < 0 or > 1)
                {
                    throw new BasicSyntaxException($"{modifier.ToString().ToUpperInvariant()} needs 0 or 1.", tokens[position - 1].Position);
                }

                if (modifier == BasicKeyword.Flash)
                {
                    flash = value == 1;
                }
                else if (modifier == BasicKeyword.Inverse)
                {
                    inverse = value == 1;
                }
                else
                {
                    over = value == 1;
                }
            }
            else
            {
                if (value is < 0 or > 7)
                    throw new BasicSyntaxException("A color must be from 0 to 7.", tokens[position - 1].Position);
                if (modifier == BasicKeyword.Ink)
                    ink = (byte)value;
                else
                    paper = (byte)value;
            }

            position = semicolon + 1;
        }

        var comma = FindTopLevel(tokens, position, ",");
        if (comma < 0)
            throw new BasicSyntaxException("PLOT needs x and y coordinates.", ArgumentPosition(tokens));

        var x = ToInteger(Evaluate(tokens, position, comma));
        var y = ToInteger(Evaluate(tokens, comma + 1, tokens.Count));
        if (x is < 0 or >= SpectrumScreen.Width || y is < 0 or >= 176)
            throw new BasicSyntaxException("PLOT coordinates are outside the BASIC drawing area.", tokens[position].Position);

        Runtime.Screen.Plot(x, y, ink, paper, bright, flash, inverse, over);
        Runtime.PlotX = x;
        Runtime.PlotY = y;
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteDraw(IReadOnlyList<BasicToken> tokens)
    {
        var values = EvaluateArguments(tokens, 1, tokens.Count);
        if (values.Count is < 2 or > 3)
            throw new BasicSyntaxException("DRAW needs x and y offsets and an optional angle.", ArgumentPosition(tokens));

        var x = Runtime.PlotX + ToInteger(values[0]);
        var y = Runtime.PlotY + ToInteger(values[1]);
        if (x is < 0 or >= SpectrumScreen.Width || y is < 0 or >= 176)
            throw new BasicSyntaxException("DRAW ends outside the BASIC drawing area.", ArgumentPosition(tokens));

        if (values.Count == 2 || Math.Abs(values[2]) < 1e-10)
        {
            DrawLine(Runtime.PlotX, Runtime.PlotY, x, y);
        }
        else
        {
            DrawArc(Runtime.PlotX, Runtime.PlotY, x, y, values[2], ArgumentPosition(tokens));
        }
        Runtime.PlotX = x;
        Runtime.PlotY = y;
        return BasicStatementResult.Continue;
    }

    private void DrawArc(int startX, int startY, int endX, int endY, double angle, int position)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var chord = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var tangent = Math.Tan(angle / 2);
        if (chord == 0 || Math.Abs(tangent) < 1e-10)
        {
            DrawLine(startX, startY, endX, endY);
            return;
        }

        var centerX = (startX + endX) / 2.0 - deltaY / (2 * tangent);
        var centerY = (startY + endY) / 2.0 + deltaX / (2 * tangent);
        var radius = chord / (2 * Math.Abs(Math.Sin(angle / 2)));
        var startAngle = Math.Atan2(startY - centerY, startX - centerX);
        var segments = Math.Max(2, checked((int)Math.Ceiling(Math.Abs(angle) * radius / 2)));
        var points = new (int X, int Y)[segments + 1];
        for (var segment = 0; segment <= segments; segment++)
        {
            var currentAngle = startAngle + angle * segment / segments;
            points[segment] = (
                checked((int)Math.Round(centerX + radius * Math.Cos(currentAngle), MidpointRounding.AwayFromZero)),
                checked((int)Math.Round(centerY + radius * Math.Sin(currentAngle), MidpointRounding.AwayFromZero)));
            if (points[segment].X is < 0 or >= SpectrumScreen.Width ||
                points[segment].Y is < 0 or >= SpectrumScreen.DrawingHeight)
            {
                throw new BasicSyntaxException("DRAW arc leaves the BASIC drawing area.", position);
            }
        }

        points[0] = (startX, startY);
        points[^1] = (endX, endY);
        for (var segment = 1; segment < points.Length; segment++)
        {
            DrawLine(points[segment - 1].X, points[segment - 1].Y, points[segment].X, points[segment].Y);
        }
    }

    private void DrawLine(int startX, int startY, int endX, int endY)
    {
        Runtime.Screen.DrawLine(
            startX,
            startY,
            endX,
            endY,
            Runtime.Ink,
            Runtime.Paper,
            Runtime.Bright,
            Runtime.Flash,
            Runtime.Inverse,
            Runtime.Over);
    }

    private BasicStatementResult ExecuteCircle(IReadOnlyList<BasicToken> tokens)
    {
        var values = EvaluateArguments(tokens, 1, tokens.Count);
        if (values.Count != 3)
            throw new BasicSyntaxException("CIRCLE needs x, y and radius.", ArgumentPosition(tokens));

        var x = ToInteger(values[0]);
        var y = ToInteger(values[1]);
        var radius = ToInteger(values[2]);
        if (radius < 0 || x - radius < 0 || x + radius >= SpectrumScreen.Width ||
            y - radius < 0 || y + radius >= 176)
            throw new BasicSyntaxException("CIRCLE lies outside the BASIC drawing area.", ArgumentPosition(tokens));

        Runtime.Screen.DrawCircle(
            x,
            y,
            radius,
            Runtime.Ink,
            Runtime.Paper,
            Runtime.Bright,
            Runtime.Flash,
            Runtime.Inverse,
            Runtime.Over);
        Runtime.PlotX = x + radius;
        Runtime.PlotY = y;
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecutePrint(IReadOnlyList<BasicToken> tokens)
    {
        var originalInk = Runtime.Ink;
        var originalPaper = Runtime.Paper;
        var originalBright = Runtime.Bright;
        var originalFlash = Runtime.Flash;
        var originalInverse = Runtime.Inverse;
        var originalOver = Runtime.Over;
        try
        {
            return ExecutePrintItems(tokens);
        }
        finally
        {
            Runtime.Ink = originalInk;
            Runtime.Paper = originalPaper;
            Runtime.Bright = originalBright;
            Runtime.Flash = originalFlash;
            Runtime.Inverse = originalInverse;
            Runtime.Over = originalOver;
        }
    }

    private BasicStatementResult ExecutePrintItems(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count == 1)
        {
            Runtime.NewLine();
            return BasicStatementResult.Continue;
        }

        var position = 1;
        var suppressNewLine = false;
        while (position < tokens.Count)
        {
            if (tokens[position].Kind == BasicTokenKind.Separator && tokens[position].Text is ";" or "," or "'")
            {
                suppressNewLine = true;
                if (tokens[position].Text == ",")
                {
                    Runtime.AdvanceToNextPrintZone();
                }
                else if (tokens[position].Text == "'")
                {
                    Runtime.NewLine();
                }

                position++;
                continue;
            }

            if (tokens[position].Keyword is
                BasicKeyword.Ink or BasicKeyword.Paper or BasicKeyword.Bright or
                BasicKeyword.Flash or BasicKeyword.Inverse or BasicKeyword.Over)
            {
                var controlSeparator = FindNextPrintSeparator(tokens, position + 1);
                var controlEnd = controlSeparator < 0 ? tokens.Count : controlSeparator;
                Execute(tokens.Skip(position).Take(controlEnd - position).ToArray());
                suppressNewLine = true;
                if (controlSeparator < 0)
                {
                    position = tokens.Count;
                    continue;
                }

                if (tokens[controlSeparator].Text == ",")
                {
                    Runtime.AdvanceToNextPrintZone();
                }
                else if (tokens[controlSeparator].Text == "'")
                {
                    Runtime.NewLine();
                }
                position = controlSeparator + 1;
                continue;
            }

            if (tokens[position].Keyword == BasicKeyword.At)
            {
                var coordinateComma = FindTopLevel(tokens, position + 1, ",");
                var controlSeparator = coordinateComma < 0 ? -1 : FindNextPrintSeparator(tokens, coordinateComma + 1);
                if (coordinateComma < 0)
                {
                    throw new BasicSyntaxException("PRINT AT needs row and column.", tokens[position].Position);
                }

                var controlEnd = controlSeparator < 0 ? tokens.Count : controlSeparator;
                var row = ToInteger(Evaluate(tokens, position + 1, coordinateComma));
                var column = ToInteger(Evaluate(tokens, coordinateComma + 1, controlEnd));
                Runtime.SetPrintPosition(row, column);
                suppressNewLine = true;
                if (controlSeparator < 0)
                {
                    position = tokens.Count;
                    continue;
                }

                if (tokens[controlSeparator].Text == ",")
                {
                    Runtime.AdvanceToNextPrintZone();
                }
                else if (tokens[controlSeparator].Text == "'")
                {
                    Runtime.NewLine();
                }
                position = controlSeparator + 1;
                continue;
            }

            if (tokens[position].Keyword == BasicKeyword.Tab)
            {
                var controlSeparator = FindNextPrintSeparator(tokens, position + 1);
                var controlEnd = controlSeparator < 0 ? tokens.Count : controlSeparator;
                Runtime.Tab(ToInteger(Evaluate(tokens, position + 1, controlEnd)));
                suppressNewLine = true;
                if (controlSeparator < 0)
                {
                    position = tokens.Count;
                    continue;
                }

                if (tokens[controlSeparator].Text == ",")
                {
                    Runtime.AdvanceToNextPrintZone();
                }
                else if (tokens[controlSeparator].Text == "'")
                {
                    Runtime.NewLine();
                }
                position = controlSeparator + 1;
                continue;
            }

            var separator = FindNextPrintSeparator(tokens, position);
            var end = separator < 0 ? tokens.Count : separator;
            if (end == position)
                throw new BasicSyntaxException("PRINT needs a value before its separator.", tokens[position].Position);

            if (BasicExpressionEvaluator.IsStringExpression(tokens, position))
                Runtime.Write(m_expressionEvaluator.EvaluateString(tokens.Skip(position).Take(end - position).ToArray()));
            else
                Runtime.WriteNumber(Evaluate(tokens, position, end));

            if (separator < 0)
            {
                suppressNewLine = false;
                break;
            }

            suppressNewLine = true;
            if (tokens[separator].Text == ",")
            {
                Runtime.AdvanceToNextPrintZone();
            }
            else if (tokens[separator].Text == "'")
            {
                Runtime.NewLine();
            }
            position = separator + 1;
        }

        if (!suppressNewLine)
            Runtime.NewLine();
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecutePause(IReadOnlyList<BasicToken> tokens)
    {
        var frames = ToInteger(Evaluate(tokens, 1));
        if (frames is < 0 or > 65535)
            throw new BasicSyntaxException("PAUSE needs a value from 0 to 65535.", ArgumentPosition(tokens));
        return BasicStatementResult.Pause(frames);
    }

    private BasicStatementResult ExecuteBeep(IReadOnlyList<BasicToken> tokens)
    {
        var comma = FindTopLevel(tokens, 1, ",");
        if (comma < 0)
            throw new BasicSyntaxException("BEEP needs duration and pitch values.", ArgumentPosition(tokens));
        var duration = Evaluate(tokens, 1, comma);
        var pitch = Evaluate(tokens, comma + 1, tokens.Count);
        if (duration is < 0 or > 10 || pitch is < -60 or > 69)
        {
            throw new BasicSyntaxException("BEEP duration or pitch is out of range.", ArgumentPosition(tokens));
        }

        var frames = checked((int)Math.Round(duration * 50, MidpointRounding.AwayFromZero));
        return frames == 0 ? BasicStatementResult.Continue : BasicStatementResult.Pause(frames);
    }

    private BasicStatementResult ExecuteRandomize(IReadOnlyList<BasicToken> tokens)
    {
        var seed = tokens.Count == 1 ? Environment.TickCount : ToInteger(Evaluate(tokens, 1));
        Runtime.SetRandomSeed(seed);
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecutePoke(IReadOnlyList<BasicToken> tokens)
    {
        var comma = FindTopLevel(tokens, 1, ",");
        if (comma < 0)
        {
            throw new BasicSyntaxException("POKE needs an address and value.", ArgumentPosition(tokens));
        }

        var address = ToInteger(Evaluate(tokens, 1, comma));
        var value = ToInteger(Evaluate(tokens, comma + 1, tokens.Count));
        if (value is < 0 or > 255)
        {
            throw new BasicSyntaxException("POKE needs a value from 0 to 255.", ArgumentPosition(tokens));
        }

        try
        {
            Runtime.Memory.Poke(address, (byte)value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new BasicSyntaxException("POKE address is outside 48K Spectrum RAM.", ArgumentPosition(tokens));
        }

        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteRead(IReadOnlyList<BasicToken> tokens)
    {
        var targets = SplitTopLevel(tokens, 1, ",");
        if (targets.Count == 0)
        {
            throw new BasicSyntaxException("READ needs at least one variable.", ArgumentPosition(tokens));
        }

        foreach (var target in targets)
        {
            if (target.Count == 0 || target[0].Kind != BasicTokenKind.Identifier)
            {
                throw new BasicSyntaxException("READ needs variable names.", ArgumentPosition(tokens));
            }

            if (target[0].Text.EndsWith('$'))
            {
                var stringValue = Runtime.ReadStringData();
                if (target.Count == 1)
                {
                    Runtime.SetStringVariable(target[0].Text, stringValue);
                    continue;
                }

                if (target.Count < 4 || target[1].Text != "(" || target[^1].Text != ")")
                {
                    throw new BasicSyntaxException("Invalid READ string array target.", target[0].Position);
                }

                var stringIndices = EvaluateArguments(target, 2, target.Count - 1).Select(ToInteger).ToArray();
                Runtime.SetStringArrayValue(target[0].Text, stringIndices, stringValue);
                continue;
            }

            var value = Runtime.ReadData();
            if (target.Count == 1)
            {
                Runtime.SetVariable(target[0].Text, value);
                continue;
            }

            if (target.Count < 4 || target[1].Text != "(" || target[^1].Text != ")")
            {
                throw new BasicSyntaxException("Invalid READ array target.", target[0].Position);
            }

            var indices = EvaluateArguments(target, 2, target.Count - 1).Select(ToInteger).ToArray();
            Runtime.SetArrayValue(target[0].Text, indices, value);
        }

        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteRestore(IReadOnlyList<BasicToken> tokens)
    {
        var lineNumber = tokens.Count == 1 ? 0 : ToInteger(Evaluate(tokens, 1));
        if (lineNumber is < 0 or > 9999)
        {
            throw new BasicSyntaxException("RESTORE needs a line number from 0 to 9999.", ArgumentPosition(tokens));
        }

        Runtime.RestoreData(lineNumber);
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteClear(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count > 1)
        {
            var ramTop = ToInteger(Evaluate(tokens, 1));
            if (ramTop is < SpectrumMemory.FirstAddress or > SpectrumMemory.LastAddress)
            {
                throw new BasicSyntaxException("CLEAR address is outside 48K Spectrum RAM.", ArgumentPosition(tokens));
            }
        }

        Runtime.ClearVariables();
        return BasicStatementResult.Continue;
    }

    private double Evaluate(IReadOnlyList<BasicToken> tokens, int start)
    {
        return Evaluate(tokens, start, tokens.Count);
    }

    private double Evaluate(IReadOnlyList<BasicToken> tokens, int start, int end)
    {
        return m_expressionEvaluator.Evaluate(tokens.Skip(start).Take(end - start).ToArray());
    }

    private IReadOnlyList<double> EvaluateArguments(IReadOnlyList<BasicToken> tokens, int start, int end)
    {
        var arguments = new List<double>();
        var argumentStart = start;
        var depth = 0;
        for (var i = start; i <= end; i++)
        {
            if (i < end && tokens[i].Text == "(")
                depth++;
            else if (i < end && tokens[i].Text == ")")
                depth--;

            if (i < end && (depth != 0 || tokens[i].Text != ","))
                continue;

            arguments.Add(Evaluate(tokens, argumentStart, i));
            argumentStart = i + 1;
        }
        return arguments;
    }

    private static BasicStatementResult RequireNoArguments(
        IReadOnlyList<BasicToken> tokens,
        BasicStatementResult? result = null)
    {
        if (tokens.Count != 1)
            throw new BasicSyntaxException($"{tokens[0].Text} does not take arguments.", ArgumentPosition(tokens));
        return result ?? BasicStatementResult.Continue;
    }

    private static int FindTopLevel(IReadOnlyList<BasicToken> tokens, int start, string separator)
    {
        var depth = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Text == "(")
                depth++;
            else if (tokens[i].Text == ")")
                depth--;
            else if (depth == 0 && tokens[i].Text == separator)
                return i;
        }
        return -1;
    }

    private static IReadOnlyList<IReadOnlyList<BasicToken>> SplitTopLevel(
        IReadOnlyList<BasicToken> tokens,
        int start,
        string separator)
    {
        var parts = new List<IReadOnlyList<BasicToken>>();
        var partStart = start;
        var depth = 0;
        for (var i = start; i <= tokens.Count; i++)
        {
            if (i < tokens.Count && tokens[i].Text == "(")
            {
                depth++;
            }
            else if (i < tokens.Count && tokens[i].Text == ")")
            {
                depth--;
            }

            if (i < tokens.Count && (depth != 0 || tokens[i].Text != separator))
            {
                continue;
            }

            parts.Add(tokens.Skip(partStart).Take(i - partStart).ToArray());
            partStart = i + 1;
        }

        return parts;
    }

    private static int FindNextPrintSeparator(IReadOnlyList<BasicToken> tokens, int start)
    {
        var semicolon = FindTopLevel(tokens, start, ";");
        var comma = FindTopLevel(tokens, start, ",");
        var apostrophe = FindTopLevel(tokens, start, "'");
        return new[] { semicolon, comma, apostrophe }
            .Where(position => position >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static int ToInteger(double value)
    {
        return checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static int ArgumentPosition(IReadOnlyList<BasicToken> tokens)
    {
        return tokens.Count > 1 ? tokens[1].Position : tokens[0].Text.Length;
    }
}
