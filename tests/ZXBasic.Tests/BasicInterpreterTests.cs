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
using ZXBasic.Emulation;

namespace ZXBasic.Tests;

public class BasicInterpreterTests
{
    [Test]
    public void RunExecutesLinesInNumberOrder()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("20 BORDER 6");
        program.Enter("10 BORDER 1:BORDER 2");

        new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program);

        Assert.That(screen.BorderColor, Is.EqualTo(6));
    }

    [Test]
    public void RunCanStartAtARequestedLineNumber()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("10 BORDER 1");
        program.Enter("20 BORDER 6");

        new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program, 20);

        Assert.That(screen.BorderColor, Is.EqualTo(6));
    }

    [Test]
    public void RunAcceptsGotoWithoutASpace()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("10 GOTO 30");
        program.Enter("20 BORDER 1");
        program.Enter("30 BORDER 6");

        new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program);

        Assert.That(screen.BorderColor, Is.EqualTo(6));
    }

    [Test]
    public void RunAcceptsGosubWithoutASpace()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("10 GOSUB 30");
        program.Enter("20 STOP");
        program.Enter("30 BORDER 6:RETURN");

        new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program);

        Assert.That(screen.BorderColor, Is.EqualTo(6));
    }

    [Test]
    public void RunAcceptsDefFnWithoutASpace()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        program.Enter("10 DEFFN S(X)=X+1");
        program.Enter("20 LET A=FN S(41)");

        new BasicInterpreter(executor).Run(program);

        Assert.That(executor.Runtime.GetVariable("A"), Is.EqualTo(42));
    }

    [Test]
    public async Task RunAsyncReportsProgressBeforeAProgramCompletes()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        program.Enter("10 FOR I=1 TO 600");
        program.Enter("20 LET A=A+1");
        program.Enter("30 NEXT I");
        program.Enter("40 STOP");
        var progressValues = new List<double>();

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => progressValues.Add(executor.Runtime.GetVariable("A")));

        Assert.Multiple(() =>
        {
            Assert.That(progressValues, Is.Not.Empty);
            Assert.That(progressValues, Has.All.LessThan(600));
            Assert.That(executor.Runtime.GetVariable("A"), Is.EqualTo(600));
        });
    }

    [Test]
    public void RunAsyncCanBeStoppedAtItsCurrentProgramLocation()
    {
        var program = new BasicProgram();
        program.Enter("10 GO TO 10");
        using var cancellation = new CancellationTokenSource();
        var interpreter = new BasicInterpreter(new BasicStatementExecutor(new SpectrumScreen()));

        var exception = Assert.ThrowsAsync<BasicRuntimeException>(async () =>
            await interpreter.RunAsync(program, cancellation.Cancel, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("BREAK INTO PROGRAM."));
            Assert.That(exception.LineNumber, Is.EqualTo(10));
            Assert.That(exception.StatementNumber, Is.EqualTo(1));
        });
    }

    [Test]
    public void ContinuousPrintCanBeStoppedAfterItHasScrolled()
    {
        var glyphs = Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray();
        var executor = new BasicStatementExecutor(new SpectrumScreen(), SpectrumFont.FromGlyphs(glyphs));
        var program = new BasicProgram();
        program.Enter("10 PRINT \"HELLO \";:GOTO 10");
        using var cancellation = new CancellationTokenSource();
        var progressCount = 0;

        Assert.ThrowsAsync<BasicRuntimeException>(async () =>
            await new BasicInterpreter(executor).RunAsync(
                program,
                () =>
                {
                    if (++progressCount == 16)
                    {
                        cancellation.Cancel();
                    }
                },
                cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(progressCount, Is.EqualTo(16));
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(SpectrumScreen.DrawingHeight / 8 - 1));
            Assert.That(executor.Runtime.PrintColumn, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public void RunStopsAtAnUnsupportedStatement()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("10 BORDER 1");
        program.Enter("20 PRINT 2");
        program.Enter("30 BORDER 3");

        var exception = Assert.Throws<BasicRuntimeException>(() =>
            new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program));

        Assert.Multiple(() =>
        {
            Assert.That(screen.BorderColor, Is.EqualTo(1));
            Assert.That(exception!.LineNumber, Is.EqualTo(20));
            Assert.That(exception.StatementNumber, Is.EqualTo(1));
        });
    }

    [Test]
    public void RunAcceptsAnEmptyProgram()
    {
        var interpreter = new BasicInterpreter(new BasicStatementExecutor(new SpectrumScreen()));

        Assert.That(() => interpreter.Run(new BasicProgram()), Throws.Nothing);
    }

    [Test]
    public void RunReportsInvalidArgumentsAtTheirProgramLocation()
    {
        var program = new BasicProgram();
        program.Enter("40 BORDER 8");

        var exception = Assert.Throws<BasicRuntimeException>(() =>
            new BasicInterpreter(new BasicStatementExecutor(new SpectrumScreen())).Run(program));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("BORDER NEEDS AN INTEGER FROM 0 TO 7."));
            Assert.That(exception.LineNumber, Is.EqualTo(40));
            Assert.That(exception.StatementNumber, Is.EqualTo(1));
        });
    }

    [Test]
    public void RunsTheSinePlotExample()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        var source = new[]
        {
            "10 BORDER 1",
            "20 FOR A=-99 TO 99",
            "30 LET V=10: LET Q=0: LET J=1",
            "40 LET K=V*INT (SQR ((10↑4)-(A*A)) /V)",
            "50 FOR T=K TO -K STEP -V",
            "60 LET S=INT (80+30*SIN ((SQR (A*A+T*T))/12)-.7*T)",
            "70 IF S<Q THEN GO TO 110",
            "80 LET Q=S",
            "90 PLOT A+110,S-15",
            "100 LET J=0",
            "110 NEXT T: NEXT A",
            "120 PAUSE 0"
        };
        foreach (var line in source)
            program.Enter(line);

        var result = new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program);

        var pixels = new uint[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];
        screen.Render(pixels);
        var blackPixels = 0;
        for (var y = SpectrumScreen.BorderY; y < SpectrumScreen.BorderY + SpectrumScreen.Height; y++)
        {
            for (var x = SpectrumScreen.BorderX; x < SpectrumScreen.BorderX + SpectrumScreen.Width; x++)
            {
                if (pixels[y * SpectrumScreen.FrameWidth + x] == SpectrumPalette.Bgra[0])
                    blackPixels++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.IsPaused, Is.True);
            Assert.That(screen.BorderColor, Is.EqualTo(1));
            Assert.That(blackPixels, Is.GreaterThan(100));
        });
    }

    [Test]
    public void RunsArraysSubroutinesAndUserFunctions()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        foreach (var line in new[]
                 {
                     "10 DIM A(3)",
                     "20 FOR I=1 TO 3",
                     "30 LET A(I)=I*I",
                     "40 NEXT I",
                     "50 DEF FN S(X)=X+1",
                     "60 LET TOTAL=FN S(A(3))",
                     "70 GO SUB 100",
                     "80 STOP",
                     "100 LET TOTAL=TOTAL+1",
                     "110 RETURN"
                 })
            program.Enter(line);

        new BasicInterpreter(executor).Run(program);

        Assert.That(executor.Runtime.GetVariable("TOTAL"), Is.EqualTo(11));
    }

    [Test]
    public void LoadsUdgDataThroughSafeUsrAddresses()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        program.Enter("9000 RESTORE 9100: FOR F=USR \"A\" TO USR \"D\"+7: READ A: POKE F,A: NEXT F");
        program.Enter("9010 STOP");
        program.Enter("9100 DATA 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32");

        new BasicInterpreter(executor).Run(program, 9000);

        Assert.That(
            Enumerable.Range(0, 32).Select(offset => executor.Runtime.Memory.Peek(BasicRuntime.UdgAddress + offset)),
            Is.EqualTo(Enumerable.Range(1, 32)));
    }

    [Test]
    public void NextAcceptsACommaSeparatedVariableList()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        program.Enter("10 FOR I=1 TO 2");
        program.Enter("20 FOR J=1 TO 3");
        program.Enter("30 LET N=N+1");
        program.Enter("40 NEXT J,I");

        new BasicInterpreter(executor).Run(program);

        Assert.That(executor.Runtime.GetVariable("N"), Is.EqualTo(6));
    }

    [Test]
    public void ReadsNumericDataIntoVariablesAndArraysAndRestoresByLine()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        foreach (var line in new[]
                 {
                     "10 DATA 2,-4",
                     "20 DATA 8",
                     "30 DIM A(2)",
                     "40 READ X,A(1)",
                     "50 RESTORE 20",
                     "60 READ A(2)",
                     "70 STOP"
                 })
        {
            program.Enter(line);
        }

        new BasicInterpreter(executor).Run(program);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.GetVariable("X"), Is.EqualTo(2));
            Assert.That(executor.Runtime.GetArrayValue("A", [1]), Is.EqualTo(-4));
            Assert.That(executor.Runtime.GetArrayValue("A", [2]), Is.EqualTo(8));
        });
    }

    [Test]
    public void ReadReportsWhenDataIsExhausted()
    {
        var program = new BasicProgram();
        program.Enter("10 DATA 1");
        program.Enter("20 READ A,B");

        var exception = Assert.Throws<BasicRuntimeException>(() =>
            new BasicInterpreter(new BasicStatementExecutor(new SpectrumScreen())).Run(program));

        Assert.That(exception!.Message, Is.EqualTo("OUT OF DATA."));
    }

    [Test]
    public void ReadsStringDataIntoStringVariables()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        program.Enter("10 DATA \"HELLO\",42");
        program.Enter("20 READ A$,B");

        new BasicInterpreter(executor).Run(program);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.GetStringVariable("A$"), Is.EqualTo("HELLO"));
            Assert.That(executor.Runtime.GetVariable("B"), Is.EqualTo(42));
        });
    }

    [Test]
    public void ReadReportsADataTypeMismatch()
    {
        var program = new BasicProgram();
        program.Enter("10 DATA \"HELLO\"");
        program.Enter("20 READ A");

        var exception = Assert.Throws<BasicRuntimeException>(() =>
            new BasicInterpreter(new BasicStatementExecutor(new SpectrumScreen())).Run(program));

        Assert.That(exception!.Message, Is.EqualTo("READ EXPECTED NUMERIC DATA."));
    }

    [Test]
    public void FalseIfSkipsTheRestOfItsPhysicalLine()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var program = new BasicProgram();
        program.Enter("10 LET A=0");
        program.Enter("20 IF A THEN LET B=1: LET C=1");
        program.Enter("30 STOP");

        new BasicInterpreter(executor).Run(program);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.GetVariable("B"), Is.Zero);
            Assert.That(executor.Runtime.GetVariable("C"), Is.Zero);
        });
    }

    [Test]
    public void IfCanCompareStringVariables()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("10 LET A$=\"ZX\"");
        program.Enter("20 IF A$=\"ZX\" THEN BORDER 3");

        new BasicInterpreter(new BasicStatementExecutor(screen)).Run(program);

        Assert.That(screen.BorderColor, Is.EqualTo(3));
    }

    [Test]
    public async Task InputReadsNumericAndStringValuesFromTheTerminal()
    {
        var executor = new BasicStatementExecutor(
            new SpectrumScreen(),
            SpectrumFont.FromGlyphs(new byte[96 * 8]));
        var program = new BasicProgram();
        program.Enter("10 INPUT \"AGE? \";A");
        program.Enter("20 INPUT N$");
        var answers = new Queue<string>(["21", "DEAN"]);
        var prompts = new List<string>();

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => { },
            inputProvider: (prompt, _) =>
            {
                prompts.Add(prompt);
                return Task.FromResult(answers.Dequeue());
            });

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.GetVariable("A"), Is.EqualTo(21));
            Assert.That(executor.Runtime.GetStringVariable("N$"), Is.EqualTo("DEAN"));
            Assert.That(prompts, Is.EqualTo(new[] { "AGE? ", "? " }));
        });
    }

    [Test]
    public async Task InputCanReadSeveralVariables()
    {
        var executor = new BasicStatementExecutor(
            new SpectrumScreen(),
            SpectrumFont.FromGlyphs(new byte[96 * 8]));
        var program = new BasicProgram();
        program.Enter("10 INPUT \"X? \";X,\"NAME? \";N$");
        var answers = new Queue<string>(["6*7", "DEAN"]);

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => { },
            inputProvider: (_, _) => Task.FromResult(answers.Dequeue()));

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.GetVariable("X"), Is.EqualTo(42));
            Assert.That(executor.Runtime.GetStringVariable("N$"), Is.EqualTo("DEAN"));
            Assert.That(answers, Is.Empty);
        });
    }

    [Test]
    public async Task InputLineReadsAStringVariable()
    {
        var executor = new BasicStatementExecutor(
            new SpectrumScreen(),
            SpectrumFont.FromGlyphs(new byte[96 * 8]));
        var program = new BasicProgram();
        program.Enter("10 INPUT LINE A$");

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => { },
            inputProvider: (_, _) => Task.FromResult("HELLO, WORLD"));

        Assert.That(executor.Runtime.GetStringVariable("A$"), Is.EqualTo("HELLO, WORLD"));
    }

    [Test]
    public async Task InputLineCanFollowAPrompt()
    {
        var executor = new BasicStatementExecutor(
            new SpectrumScreen(),
            SpectrumFont.FromGlyphs(new byte[96 * 8]));
        var program = new BasicProgram();
        program.Enter("10 INPUT \"LEVEL? (0-9) \"; LINE A$");
        string? receivedPrompt = null;

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => { },
            inputProvider: (prompt, _) =>
            {
                receivedPrompt = prompt;
                return Task.FromResult("WORLD");
            });

        Assert.Multiple(() =>
        {
            Assert.That(receivedPrompt, Is.EqualTo("LEVEL? (0-9) "));
            Assert.That(executor.Runtime.GetStringVariable("A$"), Is.EqualTo("WORLD"));
        });
    }

    [Test]
    public void InputLineRejectsANumericVariable()
    {
        var program = new BasicProgram();
        program.Enter("10 INPUT LINE A");

        var exception = Assert.ThrowsAsync<BasicRuntimeException>(async () =>
            await new BasicInterpreter(new BasicStatementExecutor(new SpectrumScreen())).RunAsync(
                program,
                () => { },
                inputProvider: (_, _) => Task.FromResult("42")));

        Assert.That(exception!.Message, Is.EqualTo("INPUT LINE NEEDS A STRING VARIABLE."));
    }

    [Test]
    public async Task InputCanWriteToANumericArray()
    {
        var executor = new BasicStatementExecutor(
            new SpectrumScreen(),
            SpectrumFont.FromGlyphs(new byte[96 * 8]));
        var program = new BasicProgram();
        program.Enter("10 DIM A(3)");
        program.Enter("20 INPUT A(2)");

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => { },
            inputProvider: (_, _) => Task.FromResult("21*2"));

        Assert.That(executor.Runtime.GetArrayValue("A", [2]), Is.EqualTo(42));
    }

    [Test]
    public async Task InputCanWriteToAStringArray()
    {
        var executor = new BasicStatementExecutor(
            new SpectrumScreen(),
            SpectrumFont.FromGlyphs(new byte[96 * 8]));
        var program = new BasicProgram();
        program.Enter("10 DIM A$(2,5)");
        program.Enter("20 INPUT A$(2)");

        await new BasicInterpreter(executor).RunAsync(
            program,
            () => { },
            inputProvider: (_, _) => Task.FromResult("ZX"));

        Assert.That(executor.Runtime.GetStringArrayValue("A$", [2]), Is.EqualTo("ZX   "));
    }

    [Test]
    public async Task PauseWaitsForAKeyOrFrameDelayThenContinuesTheProgram()
    {
        var screen = new SpectrumScreen();
        var program = new BasicProgram();
        program.Enter("10 PAUSE 25");
        program.Enter("20 BORDER 4");
        var requestedFrames = -1;

        var result = await new BasicInterpreter(new BasicStatementExecutor(screen)).RunAsync(
            program,
            () => { },
            pauseProvider: (frames, _) =>
            {
                requestedFrames = frames;
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(requestedFrames, Is.EqualTo(25));
            Assert.That(screen.BorderColor, Is.EqualTo(4));
            Assert.That(result.IsPaused, Is.False);
        });
    }
}
