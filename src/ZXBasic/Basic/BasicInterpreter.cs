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

public sealed class BasicInterpreter
{
    private const int ThrottleStatementCount = 16;
    private const int ProgressCheckStatementCount = 16;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(16);
    private readonly BasicStatementExecutor m_statementExecutor;
    private readonly BasicExpressionEvaluator m_expressionEvaluator;

    private int m_executionSpeed = (int)BasicExecutionSpeed.Unlimited;

    public BasicExecutionSpeed ExecutionSpeed
    {
        get => (BasicExecutionSpeed)Volatile.Read(ref m_executionSpeed);
        set => Volatile.Write(ref m_executionSpeed, (int)value);
    }

    public BasicInterpreter(BasicStatementExecutor statementExecutor)
    {
        m_statementExecutor = statementExecutor;
        m_expressionEvaluator = new BasicExpressionEvaluator(statementExecutor.Runtime);
    }

    public BasicRunResult Run(BasicProgram program, int? startLineNumber = null)
    {
        return RunCoreAsync(program, null, false, CancellationToken.None, startLineNumber).GetAwaiter().GetResult();
    }

    public Task<BasicRunResult> RunAsync(
        BasicProgram program,
        Action onProgress,
        CancellationToken cancellationToken = default,
        int? startLineNumber = null,
        Func<CancellationToken, Task<string>>? inputProvider = null,
        Func<int, CancellationToken, Task>? pauseProvider = null)
    {
        ArgumentNullException.ThrowIfNull(onProgress);
        return RunCoreAsync(program, onProgress, true, cancellationToken, startLineNumber, inputProvider, pauseProvider);
    }

    private async Task<BasicRunResult> RunCoreAsync(
        BasicProgram program,
        Action? onProgress,
        bool yieldForProgress,
        CancellationToken cancellationToken,
        int? startLineNumber,
        Func<CancellationToken, Task<string>>? inputProvider = null,
        Func<int, CancellationToken, Task>? pauseProvider = null)
    {
        m_statementExecutor.Runtime.ResetForRun();
        var instructions = BuildInstructions(program);
        RegisterFunctions(instructions);
        RegisterData(instructions);
        var linePositions = instructions
            .Select((instruction, index) => (instruction.LineNumber, index))
            .GroupBy(item => item.LineNumber)
            .ToDictionary(group => group.Key, group => group.First().index);
        var loops = new List<ForLoop>();
        var calls = new Stack<int>();
        var programCounter = 0;
        if (startLineNumber.HasValue)
        {
            if (!linePositions.TryGetValue(startLineNumber.Value, out programCounter))
            {
                throw new BasicRuntimeException("STATEMENT LOST.", startLineNumber.Value, 1);
            }
        }
        var executedStatements = 0;
        var lastProgressAt = Environment.TickCount64;
        var hasReportedProgress = false;
        var executionPacer = new BasicExecutionPacer(ExecutionSpeed, 0, Environment.TickCount64);

        while (programCounter < instructions.Count)
        {
            var instruction = instructions[programCounter];
            if (cancellationToken.IsCancellationRequested)
            {
                throw RuntimeError("Break into program.", instruction);
            }

            executedStatements++;

            try
            {
                if (instruction.Tokens[0].Keyword == BasicKeyword.Input)
                {
                    await ExecuteInputAsync(instruction, inputProvider, cancellationToken);
                    programCounter++;
                    continue;
                }

                var result = ExecuteInstruction(instruction, instructions, linePositions, loops, calls, ref programCounter);
                if (result.Flow == BasicStatementFlow.Stop)
                    return BasicRunResult.Complete;
                if (result.Flow == BasicStatementFlow.Pause)
                {
                    if (pauseProvider == null)
                    {
                        return BasicRunResult.Paused;
                    }

                    await pauseProvider(result.PauseFrames, cancellationToken);
                }

                if (yieldForProgress && executedStatements % ThrottleStatementCount == 0)
                {
                    await ThrottleAsync(executionPacer, executedStatements, cancellationToken);
                }

                if (yieldForProgress && executedStatements % ProgressCheckStatementCount == 0)
                {
                    var now = Environment.TickCount64;
                    if (!hasReportedProgress || now - lastProgressAt >= ProgressInterval.TotalMilliseconds)
                    {
                        onProgress!();
                        hasReportedProgress = true;
                        lastProgressAt = now;
                        await Task.Delay(1);
                    }
                }
            }
            catch (BasicRuntimeException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw RuntimeError("Break into program.", instruction);
            }
            catch (BasicSyntaxException exception)
            {
                throw RuntimeError(exception.Message, instruction);
            }
        }

        return BasicRunResult.Complete;
    }

    private async Task ThrottleAsync(
        BasicExecutionPacer executionPacer,
        int executedStatements,
        CancellationToken cancellationToken)
    {
        var delayMilliseconds = executionPacer.GetDelayMilliseconds(
            ExecutionSpeed,
            executedStatements,
            Environment.TickCount64);
        if (delayMilliseconds >= 1)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
        }
    }

    private async Task ExecuteInputAsync(
        Instruction instruction,
        Func<CancellationToken, Task<string>>? inputProvider,
        CancellationToken cancellationToken)
    {
        if (inputProvider == null)
        {
            throw new BasicSyntaxException("INPUT needs an interactive terminal.", instruction.Tokens[0].Position);
        }

        var tokens = instruction.Tokens;
        var position = 1;
        if (position < tokens.Count && tokens[position].Keyword == BasicKeyword.Line)
        {
            position++;
        }

        while (position < tokens.Count)
        {
            var prompt = "? ";
            if (tokens[position].Kind == BasicTokenKind.String)
            {
                prompt = tokens[position].Text[1..^1];
                position++;
                if (position >= tokens.Count || tokens[position].Text is not (";" or ","))
                {
                    throw new BasicSyntaxException("An INPUT prompt needs ';' or ','.", tokens[0].Position);
                }
                position++;
            }

            if (position >= tokens.Count || tokens[position].Kind != BasicTokenKind.Identifier)
            {
                throw new BasicSyntaxException("INPUT needs a variable.", tokens[0].Position);
            }

            var variable = tokens[position++].Text;
            int[]? indices = null;
            if (position < tokens.Count && tokens[position].Text == "(")
            {
                var argumentStart = ++position;
                var depth = 1;
                while (position < tokens.Count && depth > 0)
                {
                    if (tokens[position].Text == "(")
                    {
                        depth++;
                    }
                    else if (tokens[position].Text == ")")
                    {
                        depth--;
                    }
                    position++;
                }

                if (depth != 0)
                {
                    throw new BasicSyntaxException("INPUT array target needs ')'.", tokens[0].Position);
                }

                indices = EvaluateArguments(tokens, argumentStart, position - 1);
            }

            m_statementExecutor.Runtime.Write(prompt);
            var entered = await inputProvider(cancellationToken);
            if (variable.EndsWith('$'))
            {
                if (indices == null)
                {
                    m_statementExecutor.Runtime.SetStringVariable(variable, entered);
                }
                else
                {
                    m_statementExecutor.Runtime.SetStringArrayValue(variable, indices, entered);
                }
            }
            else
            {
                var enteredTokens = BasicTokenizer.Tokenize(entered);
                var value = m_expressionEvaluator.Evaluate(enteredTokens);
                if (indices == null)
                {
                    m_statementExecutor.Runtime.SetVariable(variable, value);
                }
                else
                {
                    m_statementExecutor.Runtime.SetArrayValue(variable, indices, value);
                }
            }
            m_statementExecutor.Runtime.NewLine();

            if (position == tokens.Count)
            {
                break;
            }

            if (tokens[position].Text is not ("," or ";"))
            {
                throw new BasicSyntaxException("INPUT variables need a separator.", tokens[position].Position);
            }
            position++;
        }
    }

    private BasicStatementResult ExecuteInstruction(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, int> linePositions,
        List<ForLoop> loops,
        Stack<int> calls,
        ref int programCounter)
    {
        var tokens = instruction.Tokens;
        if (tokens.Count == 0)
            throw new BasicSyntaxException("Nonsense in BASIC.", 0);

        switch (tokens[0].Keyword)
        {
            case BasicKeyword.GoTo:
                programCounter = GetTargetPosition(tokens.Skip(1).ToArray(), linePositions, instruction);
                return BasicStatementResult.Continue;
            case BasicKeyword.GoSub:
                calls.Push(programCounter + 1);
                programCounter = GetTargetPosition(tokens.Skip(1).ToArray(), linePositions, instruction);
                return BasicStatementResult.Continue;
            case BasicKeyword.Return:
                if (calls.Count == 0)
                    throw new BasicSyntaxException("RETURN without GO SUB.", tokens[0].Position);
                programCounter = calls.Pop();
                return BasicStatementResult.Continue;
            case BasicKeyword.If:
                return ExecuteIf(instruction, instructions, linePositions, calls, ref programCounter);
            case BasicKeyword.For:
                ExecuteFor(instruction, instructions, loops, ref programCounter);
                return BasicStatementResult.Continue;
            case BasicKeyword.Next:
                ExecuteNext(instruction, loops, ref programCounter);
                return BasicStatementResult.Continue;
            case BasicKeyword.DefFn:
                programCounter++;
                return BasicStatementResult.Continue;
        }

        var result = m_statementExecutor.Execute(tokens);
        if (!result.Handled)
            throw RuntimeError("Not implemented.", instruction);
        programCounter++;
        return result;
    }

    private BasicStatementResult ExecuteIf(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, int> linePositions,
        Stack<int> calls,
        ref int programCounter)
    {
        var thenIndex = FindKeyword(instruction.Tokens, BasicKeyword.Then, 1);
        if (thenIndex < 0 || thenIndex == instruction.Tokens.Count - 1)
            throw new BasicSyntaxException("IF needs THEN and a statement.", instruction.Tokens[0].Position);

        var condition = Evaluate(instruction.Tokens, 1, thenIndex);
        if (condition == 0)
        {
            do
            {
                programCounter++;
            } while (programCounter < instructions.Count &&
                     instructions[programCounter].LineNumber == instruction.LineNumber);
            return BasicStatementResult.Continue;
        }

        var consequent = instruction.Tokens.Skip(thenIndex + 1).ToArray();
        if (consequent[0].Kind == BasicTokenKind.Number)
        {
            programCounter = GetTargetPosition(consequent, linePositions, instruction);
            return BasicStatementResult.Continue;
        }

        if (consequent[0].Keyword == BasicKeyword.GoTo)
        {
            programCounter = GetTargetPosition(consequent.Skip(1).ToArray(), linePositions, instruction);
            return BasicStatementResult.Continue;
        }

        if (consequent[0].Keyword == BasicKeyword.GoSub)
        {
            calls.Push(programCounter + 1);
            programCounter = GetTargetPosition(consequent.Skip(1).ToArray(), linePositions, instruction);
            return BasicStatementResult.Continue;
        }

        var result = m_statementExecutor.Execute(consequent);
        if (!result.Handled)
            throw new BasicSyntaxException("The THEN statement is not implemented.", consequent[0].Position);
        programCounter++;
        return result;
    }

    private void ExecuteFor(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        List<ForLoop> loops,
        ref int programCounter)
    {
        var tokens = instruction.Tokens;
        if (tokens.Count < 6 || tokens[1].Kind != BasicTokenKind.Identifier || tokens[2].Text != "=")
            throw new BasicSyntaxException("FOR needs a variable, start and limit.", tokens[0].Position);

        var toIndex = FindKeyword(tokens, BasicKeyword.To, 3);
        var stepIndex = FindKeyword(tokens, BasicKeyword.Step, toIndex + 1);
        if (toIndex < 0)
            throw new BasicSyntaxException("FOR needs TO.", tokens[0].Position);

        var start = Evaluate(tokens, 3, toIndex);
        var limit = Evaluate(tokens, toIndex + 1, stepIndex < 0 ? tokens.Count : stepIndex);
        var step = stepIndex < 0 ? 1 : Evaluate(tokens, stepIndex + 1, tokens.Count);
        if (step == 0)
            throw new BasicSyntaxException("FOR STEP cannot be zero.", tokens[0].Position);

        var variable = tokens[1].Text;
        m_statementExecutor.Runtime.SetVariable(variable, start);
        if (step > 0 && start > limit || step < 0 && start < limit)
        {
            programCounter = FindAfterMatchingNext(instructions, programCounter, variable);
            return;
        }

        loops.Add(new ForLoop(variable, limit, step, programCounter + 1));
        programCounter++;
    }

    private void ExecuteNext(Instruction instruction, List<ForLoop> loops, ref int programCounter)
    {
        var tokens = instruction.Tokens;
        if (tokens.Count > 2 || tokens.Count == 2 && tokens[1].Kind != BasicTokenKind.Identifier)
            throw new BasicSyntaxException("NEXT takes an optional loop variable.", tokens[0].Position);
        if (loops.Count == 0)
            throw new BasicSyntaxException("NEXT without FOR.", tokens[0].Position);

        var loop = loops[^1];
        if (tokens.Count == 2 && !tokens[1].Text.Equals(loop.Variable, StringComparison.OrdinalIgnoreCase))
            throw new BasicSyntaxException("NEXT variable does not match FOR.", tokens[1].Position);

        var value = m_statementExecutor.Runtime.GetVariable(loop.Variable) + loop.Step;
        m_statementExecutor.Runtime.SetVariable(loop.Variable, value);
        var shouldContinue = loop.Step > 0 ? value <= loop.Limit : value >= loop.Limit;
        if (shouldContinue)
            programCounter = loop.BodyPosition;
        else
        {
            loops.RemoveAt(loops.Count - 1);
            programCounter++;
        }
    }

    private int GetTargetPosition(
        IReadOnlyList<BasicToken> expression,
        IReadOnlyDictionary<int, int> linePositions,
        Instruction instruction)
    {
        var lineNumber = checked((int)Math.Round(m_expressionEvaluator.Evaluate(expression), MidpointRounding.AwayFromZero));
        if (!linePositions.TryGetValue(lineNumber, out var target))
            throw RuntimeError("Statement lost.", instruction);
        return target;
    }

    private int FindAfterMatchingNext(
        IReadOnlyList<Instruction> instructions,
        int forPosition,
        string variable)
    {
        var depth = 0;
        for (var i = forPosition + 1; i < instructions.Count; i++)
        {
            if (instructions[i].Tokens[0].Keyword == BasicKeyword.For)
                depth++;
            else if (instructions[i].Tokens[0].Keyword == BasicKeyword.Next)
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                var tokens = instructions[i].Tokens;
                if (tokens.Count == 1 || tokens[1].Text.Equals(variable, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
        }

        throw RuntimeError("FOR without NEXT.", instructions[forPosition]);
    }

    private double Evaluate(IReadOnlyList<BasicToken> tokens, int start, int end)
    {
        return m_expressionEvaluator.Evaluate(tokens.Skip(start).Take(end - start).ToArray());
    }

    private int[] EvaluateArguments(IReadOnlyList<BasicToken> tokens, int start, int end)
    {
        var values = new List<int>();
        var argumentStart = start;
        var depth = 0;
        for (var i = start; i <= end; i++)
        {
            if (i < end && tokens[i].Text == "(")
            {
                depth++;
            }
            else if (i < end && tokens[i].Text == ")")
            {
                depth--;
            }

            if (i < end && (depth != 0 || tokens[i].Text != ","))
            {
                continue;
            }

            values.Add(checked((int)Math.Round(Evaluate(tokens, argumentStart, i), MidpointRounding.AwayFromZero)));
            argumentStart = i + 1;
        }
        return values.ToArray();
    }

    private static int FindKeyword(IReadOnlyList<BasicToken> tokens, BasicKeyword keyword, int start)
    {
        for (var i = Math.Max(0, start); i < tokens.Count; i++)
        {
            if (tokens[i].Keyword == keyword)
                return i;
        }
        return -1;
    }

    private static IReadOnlyList<Instruction> BuildInstructions(BasicProgram program)
    {
        var instructions = new List<Instruction>();
        foreach (var line in program.Lines)
        {
            var statements = SplitStatements(line.Tokens);
            for (var i = 0; i < statements.Count; i++)
            {
                foreach (var statement in ExpandNextStatement(statements[i]))
                {
                    instructions.Add(new Instruction(line.Number, i + 1, statement));
                }
            }
        }
        return instructions;
    }

    private static IEnumerable<IReadOnlyList<BasicToken>> ExpandNextStatement(IReadOnlyList<BasicToken> tokens)
    {
        if (tokens.Count < 4 || tokens[0].Keyword != BasicKeyword.Next ||
            !tokens.Skip(1).Where((_, index) => index % 2 == 0).All(token => token.Kind == BasicTokenKind.Identifier) ||
            !tokens.Skip(2).Where((_, index) => index % 2 == 0).All(token => token.Text == ","))
        {
            yield return tokens;
            yield break;
        }

        for (var i = 1; i < tokens.Count; i += 2)
        {
            yield return new[] { tokens[0], tokens[i] };
        }
    }

    private void RegisterFunctions(IReadOnlyList<Instruction> instructions)
    {
        foreach (var instruction in instructions.Where(item => item.Tokens[0].Keyword == BasicKeyword.DefFn))
            m_statementExecutor.Runtime.DefineFunction(ParseFunction(instruction));
    }

    private void RegisterData(IReadOnlyList<Instruction> instructions)
    {
        var values = new List<(int LineNumber, BasicDataValue Value)>();
        foreach (var instruction in instructions.Where(item => item.Tokens[0].Keyword == BasicKeyword.Data))
        {
            var tokens = instruction.Tokens;
            var start = 1;
            for (var i = 1; i <= tokens.Count; i++)
            {
                if (i < tokens.Count && tokens[i].Text != ",")
                {
                    continue;
                }

                if (i == start)
                {
                    throw RuntimeError("DATA contains an empty value.", instruction);
                }

                try
                {
                    var expression = tokens.Skip(start).Take(i - start).ToArray();
                    var value = BasicExpressionEvaluator.IsStringExpression(expression)
                        ? BasicDataValue.FromString(m_expressionEvaluator.EvaluateString(expression))
                        : BasicDataValue.FromNumber(m_expressionEvaluator.Evaluate(expression));
                    values.Add((instruction.LineNumber, value));
                }
                catch (BasicSyntaxException exception)
                {
                    throw RuntimeError(exception.Message, instruction);
                }

                start = i + 1;
            }
        }

        m_statementExecutor.Runtime.SetData(values);
    }

    private static BasicUserFunction ParseFunction(Instruction instruction)
    {
        var tokens = instruction.Tokens;
        if (tokens.Count < 7 || tokens[1].Kind != BasicTokenKind.Identifier || tokens[2].Text != "(")
            throw RuntimeError("Invalid DEF FN.", instruction);

        var closeParenthesis = -1;
        for (var i = 3; i < tokens.Count; i++)
        {
            if (tokens[i].Text == ")")
            {
                closeParenthesis = i;
                break;
            }
        }
        if (closeParenthesis < 0 || closeParenthesis + 2 >= tokens.Count || tokens[closeParenthesis + 1].Text != "=")
            throw RuntimeError("Invalid DEF FN.", instruction);

        var parameters = new List<string>();
        for (var i = 3; i < closeParenthesis; i += 2)
        {
            if (tokens[i].Kind != BasicTokenKind.Identifier ||
                i + 1 < closeParenthesis && tokens[i + 1].Text != ",")
                throw RuntimeError("Invalid DEF FN parameters.", instruction);
            parameters.Add(tokens[i].Text);
        }

        return new BasicUserFunction(
            tokens[1].Text,
            parameters,
            tokens.Skip(closeParenthesis + 2).ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<BasicToken>> SplitStatements(IReadOnlyList<BasicToken> tokens)
    {
        var statements = new List<IReadOnlyList<BasicToken>>();
        var start = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is not { Kind: BasicTokenKind.Separator, Text: ":" })
                continue;
            statements.Add(tokens.Skip(start).Take(i - start).ToArray());
            start = i + 1;
        }
        statements.Add(tokens.Skip(start).ToArray());
        return statements;
    }

    private static BasicRuntimeException RuntimeError(string message, Instruction instruction)
    {
        return new BasicRuntimeException(message.ToUpperInvariant(), instruction.LineNumber, instruction.StatementNumber);
    }

    private sealed record Instruction(int LineNumber, int StatementNumber, IReadOnlyList<BasicToken> Tokens);
    private sealed record ForLoop(string Variable, double Limit, double Step, int BodyPosition);
}
