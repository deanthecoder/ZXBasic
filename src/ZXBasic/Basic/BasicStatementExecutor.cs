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
    private const double PlotMilliseconds = 7.27;
    private const double DrawSetupMilliseconds = 7.27;
    private const double HorizontalDrawMillisecondsPerPixel = (55.16 - DrawSetupMilliseconds) / 255;
    private const double VerticalDrawMillisecondsPerPixel = (26.89 - DrawSetupMilliseconds) / 175;
    private const double CircleSetupMilliseconds = 7.2;
    private const double CircleMillisecondsPerRadius = 15.0;
    private const double ArcMillisecondsPerPixel = 2.4;
    private const double DrawingRefreshMilliseconds = 40.0;

    private readonly record struct DrawingAttributes(
        byte Ink,
        byte Paper,
        bool Bright,
        bool Flash,
        bool Inverse,
        bool Over,
        bool PreserveAttributes);

    private readonly record struct DrawCommand(
        int StartX,
        int StartY,
        int EndX,
        int EndY,
        double? Angle,
        int Position,
        DrawingAttributes Attributes);

    private readonly record struct CircleCommand(
        int X,
        int Y,
        int Radius,
        DrawingAttributes Attributes);

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
            throw new BasicSyntaxException("Empty statement", 0);
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

    public async ValueTask<BasicStatementResult> ExecuteSequenceAsync(
        IReadOnlyList<BasicToken> tokens,
        Func<double, double, CancellationToken, Task> beepProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beepProvider);
        var statements = SplitTopLevel(tokens, 0, ":");
        if (statements.Any(statement => statement.Count == 0))
        {
            throw new BasicSyntaxException("Empty statement", 0);
        }

        foreach (var statement in statements)
        {
            var result = Execute(statement);
            if (!result.Handled)
            {
                return result;
            }
            if (result.Flow == BasicStatementFlow.Beep)
            {
                await beepProvider(result.BeepDuration, result.BeepPitch, cancellationToken);
                continue;
            }
            if (result.Flow != BasicStatementFlow.Continue)
            {
                return result;
            }
        }
        return BasicStatementResult.Continue;
    }

    public BasicStatementResult Execute(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count == 0 || tokens[0].Kind != BasicTokenKind.Keyword)
            throw new BasicSyntaxException("Command expected", 0);

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
            BasicKeyword.Fill => ExecuteFill(tokens),
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

    public async ValueTask<BasicStatementResult> ExecuteAsync(
        IReadOnlyList<BasicToken> tokens,
        Func<double, CancellationToken, ValueTask> drawingProgress,
        CancellationToken cancellationToken)
    {
        if (tokens.Count == 0 || tokens[0].Kind != BasicTokenKind.Keyword)
        {
            throw new BasicSyntaxException("Command expected", 0);
        }

        return tokens[0].Keyword switch
        {
            BasicKeyword.Plot => await ExecutePlotAsync(tokens, drawingProgress, cancellationToken),
            BasicKeyword.Draw => await ExecuteDrawAsync(tokens, drawingProgress, cancellationToken),
            BasicKeyword.Circle => await ExecuteCircleAsync(tokens, drawingProgress, cancellationToken),
            _ => Execute(tokens)
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
            throw new BasicSyntaxException($"{command} needs 0-{maximum}", ArgumentPosition(tokens));

        assign((byte)value);
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteInk(IReadOnlyList<BasicToken> tokens)
    {
        var value = ToInteger(Evaluate(tokens, 1));
        if (value is < 0 or > 9)
        {
            throw new BasicSyntaxException("INK 0-9 only", ArgumentPosition(tokens));
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
            throw new BasicSyntaxException($"{command} 0/1 only", ArgumentPosition(tokens));

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
            throw new BasicSyntaxException("LET needs var=value", ArgumentPosition(tokens));

        var equals = FindTopLevel(tokens, 2, "=");
        if (equals < 0 || equals == tokens.Count - 1)
            throw new BasicSyntaxException("LET needs value", ArgumentPosition(tokens));

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
                throw new BasicSyntaxException("Bad string array LET", tokens[1].Position);
            }

            var indices = EvaluateArguments(tokens, 3, equals - 1).Select(ToInteger).ToArray();
            if (Runtime.TryGetStringArrayRank(tokens[1].Text, out var rank) && rank > 0)
            {
                Runtime.SetStringArrayValue(tokens[1].Text, indices, stringValue);
            }
            else if (indices.Length == 1)
            {
                Runtime.SetStringCharacter(tokens[1].Text, indices[0], stringValue);
            }
            else
            {
                throw new BasicSyntaxException("Bad string slice LET", tokens[1].Position);
            }
            return BasicStatementResult.Continue;
        }

        var value = Evaluate(tokens, equals + 1, tokens.Count);
        if (equals == 2)
            Runtime.SetVariable(tokens[1].Text, value);
        else
        {
            if (tokens[2].Text != "(" || tokens[equals - 1].Text != ")")
                throw new BasicSyntaxException("Bad array LET", tokens[2].Position);
            var indices = EvaluateArguments(tokens, 3, equals - 1).Select(ToInteger).ToArray();
            Runtime.SetArrayValue(tokens[1].Text, indices, value);
        }
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteDim(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count < 5 || tokens[1].Kind != BasicTokenKind.Identifier ||
            tokens[2].Text != "(" || tokens[^1].Text != ")")
            throw new BasicSyntaxException("DIM needs array()", ArgumentPosition(tokens));

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
            throw new BasicSyntaxException("ERASE needs array", ArgumentPosition(tokens));
        }

        foreach (var name in names)
        {
            Runtime.EraseArray(name[0].Text);
        }
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecutePlot(IReadOnlyList<BasicToken> tokens)
    {
        var attributes = ParseDrawingAttributes(tokens, "PLOT", out var position);

        var comma = FindTopLevel(tokens, position, ",");
        if (comma < 0)
            throw new BasicSyntaxException("PLOT needs x,y", ArgumentPosition(tokens));

        var x = ToInteger(Evaluate(tokens, position, comma));
        var y = ToInteger(Evaluate(tokens, comma + 1, tokens.Count));
        if (x is < 0 or >= SpectrumScreen.Width || y is < 0 or >= 176)
            throw new BasicSyntaxException("Coords out of range", tokens[position].Position);

        Runtime.Screen.Plot(
            x,
            y,
            attributes.Ink,
            attributes.Paper,
            attributes.Bright,
            attributes.Flash,
            attributes.Inverse,
            attributes.Over,
            attributes.PreserveAttributes);
        Runtime.PlotX = x;
        Runtime.PlotY = y;
        return BasicStatementResult.Continue;
    }

    private async ValueTask<BasicStatementResult> ExecutePlotAsync(
        IReadOnlyList<BasicToken> tokens,
        Func<double, CancellationToken, ValueTask> drawingProgress,
        CancellationToken cancellationToken)
    {
        var result = ExecutePlot(tokens);
        await drawingProgress(PlotMilliseconds, cancellationToken);
        return result;
    }

    private BasicStatementResult ExecuteFill(IReadOnlyList<BasicToken> tokens)
    {
        var attributes = ParseDrawingAttributes(tokens, "FILL", out var position);

        var comma = FindTopLevel(tokens, position, ",");
        if (comma < 0)
        {
            throw new BasicSyntaxException("FILL needs x,y", ArgumentPosition(tokens));
        }

        var x = ToInteger(Evaluate(tokens, position, comma));
        var y = ToInteger(Evaluate(tokens, comma + 1, tokens.Count));
        if (x is < 0 or >= SpectrumScreen.Width || y is < 0 or >= SpectrumScreen.DrawingHeight)
        {
            throw new BasicSyntaxException("Coords out of range", tokens[position].Position);
        }

        Runtime.Screen.FloodFill(
            x,
            y,
            attributes.Ink,
            attributes.Paper,
            attributes.Bright,
            attributes.Flash,
            attributes.Inverse,
            attributes.Over,
            attributes.PreserveAttributes);
        Runtime.PlotX = x;
        Runtime.PlotY = y;
        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteDraw(IReadOnlyList<BasicToken> tokens)
    {
        var command = ParseDraw(tokens);

        if (!command.Angle.HasValue)
        {
            DrawLine(command.StartX, command.StartY, command.EndX, command.EndY, command.Attributes);
        }
        else
        {
            DrawArc(
                command.StartX,
                command.StartY,
                command.EndX,
                command.EndY,
                command.Angle.Value,
                command.Position,
                command.Attributes);
        }
        Runtime.PlotX = command.EndX;
        Runtime.PlotY = command.EndY;
        return BasicStatementResult.Continue;
    }

    private async ValueTask<BasicStatementResult> ExecuteDrawAsync(
        IReadOnlyList<BasicToken> tokens,
        Func<double, CancellationToken, ValueTask> drawingProgress,
        CancellationToken cancellationToken)
    {
        var command = ParseDraw(tokens);
        if (!command.Angle.HasValue)
        {
            var points = GetLinePoints(command.StartX, command.StartY, command.EndX, command.EndY).ToArray();
            var dx = Math.Abs(command.EndX - command.StartX);
            var dy = Math.Abs(command.EndY - command.StartY);
            var steps = Math.Max(dx, dy);
            var horizontalRatio = steps == 0 ? 0 : dx / (double)steps;
            var millisecondsPerPixel = VerticalDrawMillisecondsPerPixel +
                                       (HorizontalDrawMillisecondsPerPixel - VerticalDrawMillisecondsPerPixel) *
                                       horizontalRatio;
            var milliseconds = DrawSetupMilliseconds + steps * millisecondsPerPixel;
            await PlotPointsAsync(
                points,
                milliseconds,
                command.Attributes,
                drawingProgress,
                cancellationToken);
        }
        else
        {
            var arcControlPoints = GetArcPoints(
                command.StartX,
                command.StartY,
                command.EndX,
                command.EndY,
                command.Angle.Value,
                command.Position);
            var points = GetConnectedLinePoints(arcControlPoints).ToArray();
            var milliseconds = DrawSetupMilliseconds + points.Length * ArcMillisecondsPerPixel;
            await PlotPointsAsync(
                points,
                milliseconds,
                command.Attributes,
                drawingProgress,
                cancellationToken);
        }

        Runtime.PlotX = command.EndX;
        Runtime.PlotY = command.EndY;
        return BasicStatementResult.Continue;
    }

    private DrawCommand ParseDraw(IReadOnlyList<BasicToken> tokens)
    {
        var attributes = ParseDrawingAttributes(tokens, "DRAW", out var position);
        var values = EvaluateArguments(tokens, position, tokens.Count);
        if (values.Count is < 2 or > 3)
        {
            throw new BasicSyntaxException("DRAW needs 2/3 args", ArgumentPosition(tokens));
        }

        var endX = Runtime.PlotX + ToInteger(values[0]);
        var endY = Runtime.PlotY + ToInteger(values[1]);
        if (endX is < 0 or >= SpectrumScreen.Width || endY is < 0 or >= 176)
        {
            throw new BasicSyntaxException($"DRAW {endX},{endY} invalid", ArgumentPosition(tokens));
        }

        double? angle = values.Count == 3 && Math.Abs(values[2]) >= 1e-10 ? values[2] : null;
        return new DrawCommand(Runtime.PlotX, Runtime.PlotY, endX, endY, angle, position, attributes);
    }

    private void DrawArc(
        int startX,
        int startY,
        int endX,
        int endY,
        double angle,
        int position,
        DrawingAttributes attributes)
    {
        var points = GetArcPoints(startX, startY, endX, endY, angle, position);
        for (var segment = 1; segment < points.Length; segment++)
        {
            DrawLine(points[segment - 1].X, points[segment - 1].Y, points[segment].X, points[segment].Y, attributes);
        }
    }

    private static (int X, int Y)[] GetArcPoints(
        int startX,
        int startY,
        int endX,
        int endY,
        double angle,
        int position)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var chord = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var tangent = Math.Tan(angle / 2);
        if (chord == 0 || Math.Abs(tangent) < 1e-10)
        {
            return [(startX, startY), (endX, endY)];
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
                throw new BasicSyntaxException("DRAW arc out of range", position);
            }
        }

        points[0] = (startX, startY);
        points[^1] = (endX, endY);
        return points;
    }

    private void DrawLine(int startX, int startY, int endX, int endY, DrawingAttributes attributes)
    {
        Runtime.Screen.DrawLine(
            startX,
            startY,
            endX,
            endY,
            attributes.Ink,
            attributes.Paper,
            attributes.Bright,
            attributes.Flash,
            attributes.Inverse,
            attributes.Over,
            attributes.PreserveAttributes);
    }

    private BasicStatementResult ExecuteCircle(IReadOnlyList<BasicToken> tokens)
    {
        var command = ParseCircle(tokens);

        Runtime.Screen.DrawCircle(
            command.X,
            command.Y,
            command.Radius,
            command.Attributes.Ink,
            command.Attributes.Paper,
            command.Attributes.Bright,
            command.Attributes.Flash,
            command.Attributes.Inverse,
            command.Attributes.Over);
        Runtime.PlotX = command.X + command.Radius;
        Runtime.PlotY = command.Y;
        return BasicStatementResult.Continue;
    }

    private async ValueTask<BasicStatementResult> ExecuteCircleAsync(
        IReadOnlyList<BasicToken> tokens,
        Func<double, CancellationToken, ValueTask> drawingProgress,
        CancellationToken cancellationToken)
    {
        var command = ParseCircle(tokens);
        var points = GetCirclePoints(command.X, command.Y, command.Radius).ToArray();
        var totalMilliseconds = CircleSetupMilliseconds + CircleMillisecondsPerRadius * command.Radius;
        await PlotPointsAsync(
            points,
            totalMilliseconds,
            command.Attributes,
            drawingProgress,
            cancellationToken);

        Runtime.PlotX = command.X + command.Radius;
        Runtime.PlotY = command.Y;
        return BasicStatementResult.Continue;
    }

    private CircleCommand ParseCircle(IReadOnlyList<BasicToken> tokens)
    {
        var attributes = ParseDrawingAttributes(tokens, "CIRCLE", out var position);
        var values = EvaluateArguments(tokens, position, tokens.Count);
        if (values.Count != 3)
        {
            throw new BasicSyntaxException("CIRCLE needs x,y,r", ArgumentPosition(tokens));
        }

        var x = ToInteger(values[0]);
        var y = ToInteger(values[1]);
        var radius = ToInteger(values[2]);
        if (radius < 0 || x - radius < 0 || x + radius >= SpectrumScreen.Width ||
            y - radius < 0 || y + radius >= 176)
        {
            throw new BasicSyntaxException("CIRCLE out of range", ArgumentPosition(tokens));
        }

        return new CircleCommand(x, y, radius, attributes);
    }

    private async ValueTask PlotPointsAsync(
        IReadOnlyList<(int X, int Y)> points,
        double totalMilliseconds,
        DrawingAttributes attributes,
        Func<double, CancellationToken, ValueTask> drawingProgress,
        CancellationToken cancellationToken)
    {
        var millisecondsPerPoint = totalMilliseconds / points.Count;
        var pendingMilliseconds = 0.0;
        foreach (var point in points)
        {
            Runtime.Screen.Plot(
                point.X,
                point.Y,
                attributes.Ink,
                attributes.Paper,
                attributes.Bright,
                attributes.Flash,
                attributes.Inverse,
                attributes.Over,
                attributes.PreserveAttributes);
            pendingMilliseconds += millisecondsPerPoint;
            if (pendingMilliseconds >= DrawingRefreshMilliseconds)
            {
                await drawingProgress(pendingMilliseconds, cancellationToken);
                pendingMilliseconds = 0;
            }
        }

        if (pendingMilliseconds > 0)
        {
            await drawingProgress(pendingMilliseconds, cancellationToken);
        }
    }

    private static IEnumerable<(int X, int Y)> GetConnectedLinePoints(IReadOnlyList<(int X, int Y)> controlPoints)
    {
        for (var segment = 1; segment < controlPoints.Count; segment++)
        {
            var points = GetLinePoints(
                controlPoints[segment - 1].X,
                controlPoints[segment - 1].Y,
                controlPoints[segment].X,
                controlPoints[segment].Y);
            foreach (var point in points)
            {
                yield return point;
            }
        }
    }

    private static IEnumerable<(int X, int Y)> GetLinePoints(int x0, int y0, int x1, int y1)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

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

    private static IEnumerable<(int X, int Y)> GetCirclePoints(int centerX, int centerY, int radius)
    {
        var points = new HashSet<(int X, int Y)>();
        var x = radius;
        var y = 0;
        var error = 1 - radius;
        while (x >= y)
        {
            points.Add((centerX + x, centerY + y));
            points.Add((centerX + y, centerY + x));
            points.Add((centerX - y, centerY + x));
            points.Add((centerX - x, centerY + y));
            points.Add((centerX - x, centerY - y));
            points.Add((centerX - y, centerY - x));
            points.Add((centerX + y, centerY - x));
            points.Add((centerX + x, centerY - y));
            y++;
            if (error < 0)
            {
                error += 2 * y + 1;
            }
            else
            {
                x--;
                error += 2 * (y - x) + 1;
            }
        }

        return points.OrderBy(point => GetCircleAngle(point.X - centerX, point.Y - centerY));
    }

    private static double GetCircleAngle(int x, int y)
    {
        var angle = Math.Atan2(y, x);
        return angle < 0 ? angle + Math.PI * 2 : angle;
    }

    private DrawingAttributes ParseDrawingAttributes(
        IReadOnlyList<BasicToken> tokens,
        string command,
        out int position)
    {
        var attributes = new DrawingAttributes(
            Runtime.Ink,
            Runtime.Paper,
            Runtime.Bright,
            Runtime.Flash,
            Runtime.Inverse,
            Runtime.Over,
            false);
        position = 1;

        while (position < tokens.Count && tokens[position].Keyword is
               BasicKeyword.Ink or BasicKeyword.Paper or BasicKeyword.Bright or
               BasicKeyword.Flash or BasicKeyword.Inverse or BasicKeyword.Over or BasicKeyword.Erase)
        {
            var modifierPosition = tokens[position].Position;
            var modifier = tokens[position++].Keyword!.Value;
            var semicolon = FindTopLevel(tokens, position, ";");
            if (semicolon < 0)
            {
                throw new BasicSyntaxException(
                    $"{command} color needs ;",
                    modifierPosition);
            }

            if (modifier == BasicKeyword.Erase)
            {
                if (semicolon != position)
                {
                    throw new BasicSyntaxException($"{command} ERASE needs ;", modifierPosition);
                }

                attributes = attributes with
                {
                    Inverse = true,
                    Over = false,
                    PreserveAttributes = true
                };
                position++;
                continue;
            }

            var value = ToInteger(Evaluate(tokens, position, semicolon));
            attributes = ApplyDrawingAttribute(attributes, modifier, value, modifierPosition);
            position = semicolon + 1;
        }

        return attributes;
    }

    private static DrawingAttributes ApplyDrawingAttribute(
        DrawingAttributes attributes,
        BasicKeyword modifier,
        int value,
        int position)
    {
        if (modifier is BasicKeyword.Ink or BasicKeyword.Paper)
        {
            if (value is < 0 or > 7)
            {
                throw new BasicSyntaxException("Color 0-7 only", position);
            }

            return modifier == BasicKeyword.Ink
                ? attributes with { Ink = (byte)value }
                : attributes with { Paper = (byte)value };
        }

        if (value is < 0 or > 1)
        {
            throw new BasicSyntaxException(
                $"{modifier.ToString().ToUpperInvariant()} 0/1 only",
                position);
        }

        return modifier switch
        {
            BasicKeyword.Bright => attributes with { Bright = value == 1 },
            BasicKeyword.Flash => attributes with { Flash = value == 1 },
            BasicKeyword.Inverse => attributes with { Inverse = value == 1 },
            BasicKeyword.Over => attributes with { Over = value == 1 },
            _ => attributes
        };
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
                    throw new BasicSyntaxException("PRINT AT needs coords", tokens[position].Position);
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
                throw new BasicSyntaxException("PRINT needs value", tokens[position].Position);

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
            throw new BasicSyntaxException("PAUSE 0-65535 only", ArgumentPosition(tokens));
        return BasicStatementResult.Pause(frames);
    }

    private BasicStatementResult ExecuteBeep(IReadOnlyList<BasicToken> tokens)
    {
        var comma = FindTopLevel(tokens, 1, ",");
        if (comma < 0)
            throw new BasicSyntaxException("BEEP needs 2 values", ArgumentPosition(tokens));
        var duration = Evaluate(tokens, 1, comma);
        var pitch = Evaluate(tokens, comma + 1, tokens.Count);
        if (duration is < 0 or > 10 || pitch is < -60 or > 69)
        {
            throw new BasicSyntaxException("BEEP out of range", ArgumentPosition(tokens));
        }

        return duration == 0 ? BasicStatementResult.Continue : BasicStatementResult.Beep(duration, pitch);
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
            throw new BasicSyntaxException("POKE needs addr,value", ArgumentPosition(tokens));
        }

        var address = ToInteger(Evaluate(tokens, 1, comma));
        var value = ToInteger(Evaluate(tokens, comma + 1, tokens.Count));
        if (value is < 0 or > 255)
        {
            throw new BasicSyntaxException("POKE value 0-255 only", ArgumentPosition(tokens));
        }

        try
        {
            Runtime.Memory.Poke(address, (byte)value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new BasicSyntaxException("POKE address invalid", ArgumentPosition(tokens));
        }

        return BasicStatementResult.Continue;
    }

    private BasicStatementResult ExecuteRead(IReadOnlyList<BasicToken> tokens)
    {
        var targets = SplitTopLevel(tokens, 1, ",");
        if (targets.Count == 0)
        {
            throw new BasicSyntaxException("READ needs variable", ArgumentPosition(tokens));
        }

        foreach (var target in targets)
        {
            if (target.Count == 0 || target[0].Kind != BasicTokenKind.Identifier)
            {
                throw new BasicSyntaxException("READ needs variable", ArgumentPosition(tokens));
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
                    throw new BasicSyntaxException("Bad READ target", target[0].Position);
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
                throw new BasicSyntaxException("Bad READ target", target[0].Position);
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
            throw new BasicSyntaxException("RESTORE 0-9999 only", ArgumentPosition(tokens));
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
                throw new BasicSyntaxException("CLEAR address invalid", ArgumentPosition(tokens));
            }
        }

        Runtime.ClearVariables();
        Runtime.RestoreData();
        Runtime.ClearScreen();
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
            throw new BasicSyntaxException($"{tokens[0].Text} no args", ArgumentPosition(tokens));
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
