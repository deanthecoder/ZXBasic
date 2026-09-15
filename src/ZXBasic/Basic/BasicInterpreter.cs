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
    private const double SpectrumDrawingSpeedMultiplier = 8.0 / 3.0;
    private const int ThrottleStatementCount = 16;
    private const int ProgressCheckStatementCount = 16;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(16);
    private readonly BasicStatementExecutor m_statementExecutor;
    private readonly BasicExpressionEvaluator m_expressionEvaluator;
    private readonly Func<TimeSpan, CancellationToken, Task> m_delay;
    private ContinueState? m_continueState;

    private int m_executionSpeed = (int)BasicExecutionSpeed.Unlimited;

    public BasicExecutionSpeed ExecutionSpeed
    {
        get => (BasicExecutionSpeed)Volatile.Read(ref m_executionSpeed);
        set => Volatile.Write(ref m_executionSpeed, (int)value);
    }

    public BasicInterpreter(BasicStatementExecutor statementExecutor)
        : this(statementExecutor, Task.Delay)
    {
    }

    internal BasicInterpreter(
        BasicStatementExecutor statementExecutor,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        m_statementExecutor = statementExecutor;
        m_expressionEvaluator = new BasicExpressionEvaluator(statementExecutor.Runtime);
        m_delay = delay;
    }

    public BasicRunResult Run(BasicProgram program, int? startLineNumber = null)
    {
        return RunCoreAsync(program, null, false, CancellationToken.None, startLineNumber, false).GetAwaiter().GetResult();
    }

    public BasicRunResult Continue(BasicProgram program)
    {
        return RunCoreAsync(program, null, false, CancellationToken.None, null, true).GetAwaiter().GetResult();
    }

    public Task<BasicRunResult> RunAsync(
        BasicProgram program,
        Action onProgress,
        CancellationToken cancellationToken = default,
        int? startLineNumber = null,
        Func<string, CancellationToken, Task<string>>? inputProvider = null,
        Func<int, CancellationToken, Task>? pauseProvider = null,
        Func<double, double, CancellationToken, Task>? beepProvider = null)
    {
        ArgumentNullException.ThrowIfNull(onProgress);
        return RunCoreAsync(
            program,
            onProgress,
            true,
            cancellationToken,
            startLineNumber,
            false,
            inputProvider,
            pauseProvider,
            beepProvider);
    }

    public Task<BasicRunResult> ContinueAsync(
        BasicProgram program,
        Action onProgress,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task<string>>? inputProvider = null,
        Func<int, CancellationToken, Task>? pauseProvider = null,
        Func<double, double, CancellationToken, Task>? beepProvider = null)
    {
        ArgumentNullException.ThrowIfNull(onProgress);
        return RunCoreAsync(
            program,
            onProgress,
            true,
            cancellationToken,
            null,
            true,
            inputProvider,
            pauseProvider,
            beepProvider);
    }

    private async Task<BasicRunResult> RunCoreAsync(
        BasicProgram program,
        Action? onProgress,
        bool yieldForProgress,
        CancellationToken cancellationToken,
        int? startLineNumber,
        bool continueExecution,
        Func<string, CancellationToken, Task<string>>? inputProvider = null,
        Func<int, CancellationToken, Task>? pauseProvider = null,
        Func<double, double, CancellationToken, Task>? beepProvider = null)
    {
        IReadOnlyList<Instruction> instructions;
        IReadOnlyDictionary<int, int> linePositions;
        List<ForLoop> loops;
        Stack<int> calls;
        int programCounter;
        var lastLineNumber = 0;
        var lastStatementNumber = 1;
        if (continueExecution)
        {
            var state = m_continueState;
            m_continueState = null;
            if (state == null || !state.Listing.SequenceEqual(program.GetListing()))
            {
                throw new BasicRuntimeException("CONTINUE without STOP", 0, 1);
            }

            instructions = state.Instructions;
            linePositions = state.LinePositions;
            loops = state.Loops;
            calls = state.Calls;
            programCounter = state.ProgramCounter;
            lastLineNumber = state.LineNumber;
            lastStatementNumber = state.StatementNumber;
        }
        else
        {
            m_continueState = null;
            m_statementExecutor.Runtime.ResetForRun();
            instructions = BuildInstructions(program);
            RegisterFunctions(instructions);
            RegisterData(instructions);
            linePositions = instructions
                .Select((instruction, index) => (instruction.LineNumber, index))
                .GroupBy(item => item.LineNumber)
                .ToDictionary(group => group.Key, group => group.First().index);
            loops = [];
            calls = [];
            programCounter = 0;
            if (startLineNumber.HasValue)
            {
                var targetPosition = FindTargetPosition(linePositions, startLineNumber.Value);
                if (!targetPosition.HasValue)
                {
                    throw new BasicRuntimeException("STATEMENT LOST", startLineNumber.Value, 1);
                }

                programCounter = targetPosition.Value;
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
                throw RuntimeError("BREAK", instruction);
            }

            executedStatements++;

            try
            {
                if (instruction.Tokens[0].Keyword == BasicKeyword.Input)
                {
                    await ExecuteInputAsync(instruction, inputProvider, cancellationToken);
                    lastLineNumber = instruction.LineNumber;
                    lastStatementNumber = instruction.StatementNumber;
                    programCounter++;
                    continue;
                }

                var execution = await ExecuteInstructionAsync(
                    instruction,
                    instructions,
                    linePositions,
                    loops,
                    calls,
                    programCounter,
                    yieldForProgress ? onProgress : null,
                    cancellationToken);
                var result = execution.Result;
                programCounter = execution.ProgramCounter;
                lastLineNumber = instruction.LineNumber;
                lastStatementNumber = instruction.StatementNumber;
                if (result.Flow == BasicStatementFlow.Stop)
                {
                    m_continueState = new ContinueState(
                        program.GetListing(),
                        instructions,
                        linePositions,
                        loops,
                        calls,
                        programCounter,
                        lastLineNumber,
                        lastStatementNumber);
                    return BasicRunResult.CompleteAt(lastLineNumber, lastStatementNumber);
                }
                if (result.Flow == BasicStatementFlow.Pause)
                {
                    if (pauseProvider == null)
                    {
                        return BasicRunResult.PausedAt(lastLineNumber, lastStatementNumber);
                    }

                    await pauseProvider(result.PauseFrames, cancellationToken);
                }
                else if (result.Flow == BasicStatementFlow.Beep)
                {
                    if (beepProvider == null)
                    {
                        await m_delay(TimeSpan.FromSeconds(result.BeepDuration), cancellationToken);
                    }
                    else
                    {
                        await beepProvider(result.BeepDuration, result.BeepPitch, cancellationToken);
                    }
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
            catch (OperationCanceledException)
            {
                throw RuntimeError("BREAK", instruction);
            }
            catch (BasicSyntaxException exception)
            {
                throw RuntimeError(exception.Message, instruction);
            }
        }

        return BasicRunResult.CompleteAt(lastLineNumber, lastStatementNumber);
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
        Func<string, CancellationToken, Task<string>>? inputProvider,
        CancellationToken cancellationToken)
    {
        if (inputProvider == null)
        {
            throw new BasicSyntaxException("INPUT needs terminal", instruction.Tokens[0].Position);
        }

        var tokens = instruction.Tokens;
        var position = 1;

        while (position < tokens.Count)
        {
            var prompt = "? ";
            if (tokens[position].Kind == BasicTokenKind.String)
            {
                prompt = tokens[position].Text[1..^1];
                position++;
                if (position >= tokens.Count || tokens[position].Text is not (";" or ","))
                {
                    throw new BasicSyntaxException("INPUT needs ; or ,", tokens[0].Position);
                }
                position++;
            }

            var lineInput = position < tokens.Count && tokens[position].Keyword == BasicKeyword.Line;
            if (lineInput)
            {
                position++;
            }

            if (position >= tokens.Count || tokens[position].Kind != BasicTokenKind.Identifier)
            {
                throw new BasicSyntaxException("INPUT needs variable", tokens[0].Position);
            }

            var variable = tokens[position++].Text;
            if (lineInput && !variable.EndsWith('$'))
            {
                throw new BasicSyntaxException("INPUT LINE needs str", tokens[0].Position);
            }

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
                    throw new BasicSyntaxException("INPUT array needs )", tokens[0].Position);
                }

                indices = EvaluateArguments(tokens, argumentStart, position - 1);
            }

            var entered = await inputProvider(prompt, cancellationToken);
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
            if (position == tokens.Count)
            {
                break;
            }

            if (tokens[position].Text is not ("," or ";"))
            {
                throw new BasicSyntaxException("INPUT needs separator", tokens[position].Position);
            }
            position++;
        }
    }

    private async ValueTask<(BasicStatementResult Result, int ProgramCounter)> ExecuteInstructionAsync(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, int> linePositions,
        List<ForLoop> loops,
        Stack<int> calls,
        int programCounter,
        Action? onProgress,
        CancellationToken cancellationToken)
    {
        var tokens = instruction.Tokens;
        if (tokens.Count == 0)
        {
            throw new BasicSyntaxException("Nonsense in BASIC", 0);
        }

        switch (tokens[0].Keyword)
        {
            case BasicKeyword.GoTo:
                programCounter = GetTargetPosition([.. tokens.Skip(1)], linePositions, instruction);
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.GoSub:
                calls.Push(programCounter + 1);
                programCounter = GetTargetPosition([.. tokens.Skip(1)], linePositions, instruction);
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.Return:
                if (calls.Count == 0)
                {
                    throw new BasicSyntaxException("RETURN without GO SUB", tokens[0].Position);
                }
                programCounter = calls.Pop();
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.Run:
                ExecuteRun(instruction, instructions, linePositions, loops, calls, out programCounter);
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.If:
                return await ExecuteIfAsync(
                    instruction,
                    instructions,
                    linePositions,
                    loops,
                    calls,
                    programCounter,
                    onProgress,
                    cancellationToken);
            case BasicKeyword.For:
                ExecuteFor(instruction, instructions, loops, ref programCounter);
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.Next:
                ExecuteNext(instruction, loops, ref programCounter);
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.DefFn:
                programCounter++;
                return (BasicStatementResult.Continue, programCounter);
            case BasicKeyword.Clear:
                var clearResult = m_statementExecutor.Execute(tokens);
                loops.Clear();
                calls.Clear();
                programCounter++;
                return (clearResult, programCounter);
        }

        var result = onProgress == null
            ? m_statementExecutor.Execute(tokens)
            : await m_statementExecutor.ExecuteAsync(
                tokens,
                (milliseconds, token) => PaceDrawingAsync(milliseconds, onProgress, token),
                cancellationToken);
        if (!result.Handled)
        {
            throw RuntimeError("Not implemented", instruction);
        }
        programCounter++;
        return (result, programCounter);
    }

    private async ValueTask<(BasicStatementResult Result, int ProgramCounter)> ExecuteIfAsync(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, int> linePositions,
        List<ForLoop> loops,
        Stack<int> calls,
        int programCounter,
        Action? onProgress,
        CancellationToken cancellationToken)
    {
        var thenIndex = FindKeyword(instruction.Tokens, BasicKeyword.Then, 1);
        if (thenIndex < 0 || thenIndex == instruction.Tokens.Count - 1)
            throw new BasicSyntaxException("IF needs THEN stmt", instruction.Tokens[0].Position);

        var condition = Evaluate(instruction.Tokens, 1, thenIndex);
        if (condition == 0)
        {
            do
            {
                programCounter++;
            } while (programCounter < instructions.Count &&
                     instructions[programCounter].LineNumber == instruction.LineNumber);
            return (BasicStatementResult.Continue, programCounter);
        }

        var consequent = instruction.Tokens.Skip(thenIndex + 1).ToArray();
        if (consequent[0].Kind == BasicTokenKind.Number)
        {
            programCounter = GetTargetPosition(consequent, linePositions, instruction);
            return (BasicStatementResult.Continue, programCounter);
        }

        var consequentInstruction = instruction with { Tokens = consequent };
        return await ExecuteInstructionAsync(
            consequentInstruction,
            instructions,
            linePositions,
            loops,
            calls,
            programCounter,
            onProgress,
            cancellationToken);
    }

    private async ValueTask PaceDrawingAsync(
        double spectrumMilliseconds,
        Action onProgress,
        CancellationToken cancellationToken)
    {
        var speed = ExecutionSpeed;
        if (speed != BasicExecutionSpeed.Spectrum)
        {
            return;
        }

        onProgress();
        await m_delay(
            TimeSpan.FromMilliseconds(spectrumMilliseconds / SpectrumDrawingSpeedMultiplier),
            cancellationToken);
    }

    private void ExecuteRun(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, int> linePositions,
        List<ForLoop> loops,
        Stack<int> calls,
        out int programCounter)
    {
        var targetPosition = instruction.Tokens.Count == 1
            ? 0
            : GetTargetPosition([.. instruction.Tokens.Skip(1)], linePositions, instruction);

        m_statementExecutor.Runtime.ResetForRun();
        RegisterFunctions(instructions);
        RegisterData(instructions);
        loops.Clear();
        calls.Clear();
        programCounter = targetPosition;
    }

    private void ExecuteFor(
        Instruction instruction,
        IReadOnlyList<Instruction> instructions,
        List<ForLoop> loops,
        ref int programCounter)
    {
        var tokens = instruction.Tokens;
        if (tokens.Count < 6 || tokens[1].Kind != BasicTokenKind.Identifier || tokens[2].Text != "=")
            throw new BasicSyntaxException("FOR needs var=...TO", tokens[0].Position);

        var toIndex = FindKeyword(tokens, BasicKeyword.To, 3);
        var stepIndex = FindKeyword(tokens, BasicKeyword.Step, toIndex + 1);
        if (toIndex < 0)
            throw new BasicSyntaxException("FOR needs TO", tokens[0].Position);

        var start = Evaluate(tokens, 3, toIndex);
        var limit = Evaluate(tokens, toIndex + 1, stepIndex < 0 ? tokens.Count : stepIndex);
        var step = stepIndex < 0 ? 1 : Evaluate(tokens, stepIndex + 1, tokens.Count);
        if (step == 0)
            throw new BasicSyntaxException("FOR STEP 0 invalid", tokens[0].Position);

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
            throw new BasicSyntaxException("NEXT needs var", tokens[0].Position);
        if (loops.Count == 0)
            throw new BasicSyntaxException("NEXT without FOR", tokens[0].Position);

        var loop = loops[^1];
        if (tokens.Count == 2 && !tokens[1].Text.Equals(loop.Variable, StringComparison.OrdinalIgnoreCase))
            throw new BasicSyntaxException("NEXT var mismatch", tokens[1].Position);

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
        var targetPosition = FindTargetPosition(linePositions, lineNumber);
        if (!targetPosition.HasValue)
            throw RuntimeError("Statement lost", instruction);
        return targetPosition.Value;
    }

    private static int? FindTargetPosition(IReadOnlyDictionary<int, int> linePositions, int lineNumber)
    {
        var targetLineNumber = linePositions.Keys
            .Where(candidate => candidate >= lineNumber)
            .DefaultIfEmpty(-1)
            .Min();
        if (targetLineNumber < 0)
            return null;
        return linePositions[targetLineNumber];
    }

    private static int FindAfterMatchingNext(
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

        throw RuntimeError("FOR without NEXT", instructions[forPosition]);
    }

    private double Evaluate(IReadOnlyList<BasicToken> tokens, int start, int end)
    {
        return m_expressionEvaluator.Evaluate([.. tokens.Skip(start).Take(end - start)]);
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
        return [.. values];
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
        if (tokens.Count < 4 || tokens[0].Keyword != BasicKeyword.Next || tokens.Skip(1).Where((_, index) => index % 2 == 0).Any(token => token.Kind != BasicTokenKind.Identifier) || tokens.Skip(2).Where((_, index) => index % 2 == 0).Any(token => token.Text != ","))
        {
            yield return tokens;
            yield break;
        }

        for (var i = 1; i < tokens.Count; i += 2)
        {
            yield return [tokens[0], tokens[i]];
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
                    throw RuntimeError("Empty DATA", instruction);
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
            throw RuntimeError("Invalid DEF FN", instruction);

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
            throw RuntimeError("Invalid DEF FN", instruction);

        var parameters = new List<string>();
        for (var i = 3; i < closeParenthesis; i += 2)
        {
            if (tokens[i].Kind != BasicTokenKind.Identifier ||
                i + 1 < closeParenthesis && tokens[i + 1].Text != ",")
                throw RuntimeError("Invalid FN params", instruction);
            parameters.Add(tokens[i].Text);
        }

        return new BasicUserFunction(
            tokens[1].Text,
            parameters,
            [.. tokens.Skip(closeParenthesis + 2)]);
    }

    private static IReadOnlyList<IReadOnlyList<BasicToken>> SplitStatements(IReadOnlyList<BasicToken> tokens)
    {
        var statements = new List<IReadOnlyList<BasicToken>>();
        var start = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is not { Kind: BasicTokenKind.Separator, Text: ":" })
                continue;
            statements.Add([.. tokens.Skip(start).Take(i - start)]);
            start = i + 1;
        }
        statements.Add([.. tokens.Skip(start)]);
        return statements;
    }

    private static BasicRuntimeException RuntimeError(string message, Instruction instruction)
    {
        return new BasicRuntimeException(message.ToUpperInvariant(), instruction.LineNumber, instruction.StatementNumber);
    }

    private sealed record Instruction(int LineNumber, int StatementNumber, IReadOnlyList<BasicToken> Tokens);
    private sealed record ForLoop(string Variable, double Limit, double Step, int BodyPosition);
    private sealed record ContinueState(
        IReadOnlyList<string> Listing,
        IReadOnlyList<Instruction> Instructions,
        IReadOnlyDictionary<int, int> LinePositions,
        List<ForLoop> Loops,
        Stack<int> Calls,
        int ProgramCounter,
        int LineNumber,
        int StatementNumber);
}
