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

public class BasicStatementExecutorTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(7)]
    public void BorderChangesTheScreenBorder(byte color)
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        var didExecute = executor.TryExecute(BasicTokenizer.Tokenize($"BORDER {color}"));

        Assert.Multiple(() =>
        {
            Assert.That(didExecute, Is.True);
            Assert.That(screen.BorderColor, Is.EqualTo(color));
        });
    }

    [TestCase("BORDER")]
    [TestCase("BORDER -1")]
    [TestCase("BORDER 8")]
    [TestCase("BORDER 1, 2")]
    public void BorderRejectsInvalidArguments(string statement)
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        Assert.That(
            () => executor.TryExecute(BasicTokenizer.Tokenize(statement)),
            Throws.TypeOf<BasicSyntaxException>());
    }

    [Test]
    public void ReportsAnUnsupportedStatementWithoutExecutingIt()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        Assert.That(executor.TryExecute(BasicTokenizer.Tokenize("INPUT A")), Is.False);
    }

    [Test]
    public void BorderAcceptsAnExpression()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        executor.TryExecute(BasicTokenizer.Tokenize("BORDER 1+2"));

        Assert.That(screen.BorderColor, Is.EqualTo(3));
    }

    [Test]
    public void InkAcceptsSpectrumTransparentAndContrastValues()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen())
        {
            Runtime =
            {
                Ink = 3
            }
        };

        executor.TryExecute(BasicTokenizer.Tokenize("INK 7.6"));
        Assert.That(executor.Runtime.Ink, Is.EqualTo(3), "INK 8 should retain the current ink");

        executor.Runtime.Paper = 6;
        executor.TryExecute(BasicTokenizer.Tokenize("INK 9"));
        Assert.That(executor.Runtime.Ink, Is.Zero, "INK 9 should contrast with a light paper");
    }

    [Test]
    public void ExecutesColonSeparatedDirectStatements()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        var result = executor.ExecuteSequence(BasicTokenizer.Tokenize("BORDER 3:PAPER 2:CLS"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(screen.BorderColor, Is.EqualTo(3));
            Assert.That(executor.Runtime.Paper, Is.EqualTo(2));
        });
    }

    [Test]
    public void LetAssignsANumericVariable()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        executor.TryExecute(BasicTokenizer.Tokenize("LET TOTAL=6*7"));

        Assert.That(executor.Runtime.GetVariable("TOTAL"), Is.EqualTo(42));
    }

    [Test]
    public void LetAssignsAndPrintsAStringVariable()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("LET A$=\"HEL\"+\"LO\""));
        executor.TryExecute(BasicTokenizer.Tokenize("PRINT A$;"));

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.GetStringVariable("A$"), Is.EqualTo("HELLO"));
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(5));
        });
    }

    [Test]
    public void StringArraysHaveFixedWidthRows()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("DIM A$(2,5)"));

        executor.TryExecute(BasicTokenizer.Tokenize("LET A$(2)=\"ZX\""));

        var value = new BasicExpressionEvaluator(executor.Runtime)
            .EvaluateString(BasicTokenizer.Tokenize("A$(2)"));
        Assert.That(value, Is.EqualTo("ZX   "));
    }

    [Test]
    public void DimensionedStringHasAFixedWidth()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("DIM A$(4)"));

        executor.TryExecute(BasicTokenizer.Tokenize("LET A$=\"HELLO\""));

        Assert.That(executor.Runtime.GetStringVariable("A$"), Is.EqualTo("HELL"));
    }

    [Test]
    public void EraseRemovesNumericAndStringArrays()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("DIM A(2)"));
        executor.TryExecute(BasicTokenizer.Tokenize("DIM B$(2,4)"));

        executor.TryExecute(BasicTokenizer.Tokenize("ERASE A,B$"));

        Assert.Multiple(() =>
        {
            Assert.That(() => executor.Runtime.GetArrayValue("A", [1]), Throws.TypeOf<BasicSyntaxException>());
            Assert.That(() => executor.Runtime.GetStringArrayValue("B$", [1]), Throws.TypeOf<BasicSyntaxException>());
        });
    }

    [Test]
    public void PlotUsesCurrentColorsAndBrightness()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);
        executor.TryExecute(BasicTokenizer.Tokenize("INK 2"));
        executor.TryExecute(BasicTokenizer.Tokenize("PAPER 6"));
        executor.TryExecute(BasicTokenizer.Tokenize("BRIGHT 1"));

        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 10,20"));

        var pixels = new uint[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];
        screen.Render(pixels);
        var pixel = (SpectrumScreen.BorderY + SpectrumScreen.DrawingHeight - 1 - 20) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX + 10;
        Assert.That(pixels[pixel], Is.EqualTo(SpectrumPalette.Bgra[10]));
    }

    [Test]
    public void PlotAcceptsTemporaryColorItems()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        executor.TryExecute(BasicTokenizer.Tokenize("PLOT BRIGHT 1;PAPER 4;INK 1;12,30"));

        var pixels = new uint[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];
        screen.Render(pixels);
        var pixel = (SpectrumScreen.BorderY + SpectrumScreen.DrawingHeight - 1 - 30) * SpectrumScreen.FrameWidth + SpectrumScreen.BorderX + 12;
        Assert.That(pixels[pixel], Is.EqualTo(SpectrumPalette.Bgra[9]));
    }

    [Test]
    public void InverseErasesAndOverTogglesPlottedPixels()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 10,20"));
        executor.TryExecute(BasicTokenizer.Tokenize("INVERSE 1"));
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 10,20"));
        var erased = screen.IsPixelSet(10, 20);
        executor.TryExecute(BasicTokenizer.Tokenize("INVERSE 0"));
        executor.TryExecute(BasicTokenizer.Tokenize("OVER 1"));
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 10,20"));
        var toggledOn = screen.IsPixelSet(10, 20);
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 10,20"));

        Assert.Multiple(() =>
        {
            Assert.That(erased, Is.False);
            Assert.That(toggledOn, Is.True);
            Assert.That(screen.IsPixelSet(10, 20), Is.False);
        });
    }

    [Test]
    public void PlotAcceptsTemporaryFlashInverseAndOverItems()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        executor.TryExecute(BasicTokenizer.Tokenize("PLOT FLASH 1;OVER 1;INVERSE 0;12,30"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsPixelSet(12, 30), Is.True);
            Assert.That(screen.GetAttribute(18, 1) & 0x80, Is.EqualTo(0x80));
        });
    }

    [Test]
    public void PrintWritesTextAndHonorsATrailingSemicolon()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT \"HI\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(8, 0), Is.True);
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(2));
            Assert.That(executor.Runtime.PrintRow, Is.Zero);
        });
    }

    [Test]
    public void PrintSupportsAtAndTabPositioning()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 2,3;\"X\";"));
        executor.TryExecute(BasicTokenizer.Tokenize("PRINT TAB 10;\"Y\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(24, 16), Is.True);
            Assert.That(screen.IsScreenPixelSet(80, 16), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(2));
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(11));
        });
    }

    [Test]
    public void PrintAtUsesTheMagnitudeOfNegativeCoordinates()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT -1,-10;\"X\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(80, 8), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(1));
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(11));
        });
    }

    [Test]
    public void PrintAtBottomRightWithATrailingSeparatorDefersScrolling()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 21,31;\"*\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(255, 175), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(21));
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(32));
        });
    }

    [Test]
    public void PrintAtBottomRowDefersScrollingUntilMoreTextIsPrinted()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);
        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 0,0;\"T\";"));

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 21,0;\"B\""));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, 168), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(22));
        });

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT \"N\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.False);
            Assert.That(screen.IsScreenPixelSet(0, 160), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, 168), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(21));
        });
    }

    [Test]
    public void PrintAtRepositionsFromBelowBottomRowWithoutScrolling()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);
        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 0,0;\"T\";"));
        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 21,0;\"B\""));

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 10,0;\"X\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, 80), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, 168), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(10));
        });
    }

    [Test]
    public void PrintUsesUserDefinedGraphicsFromSpectrumMemory()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(new byte[96 * 8]);
        var executor = new BasicStatementExecutor(screen, font);
        executor.Runtime.Memory.Poke(BasicRuntime.UdgAddress, 0x80);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT CHR$ 144;"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(1, 0), Is.False);
        });
    }

    [Test]
    public void PrintUsesSpectrumBlockGraphics()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(new byte[96 * 8]);
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT CHR$ 135;CHR$ 143;"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, 4), Is.False);
            Assert.That(screen.IsScreenPixelSet(7, 4), Is.True);
            Assert.That(screen.IsScreenPixelSet(8, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(8, 7), Is.True);
        });
    }

    [Test]
    public void PrintAllowsATerminalAtPositioningItem()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 15,0"));

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(15));
            Assert.That(executor.Runtime.PrintColumn, Is.Zero);
        });
    }

    [Test]
    public void PrintCommaAdvancesToTheNextSixteenColumnZone()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT \"X\","));

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.PrintRow, Is.Zero);
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(16));
        });
    }

    [Test]
    public void PrintApostropheStartsANewLine()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT \"A\"'\"B\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, 8), Is.True);
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(1));
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(1));
        });
    }

    [Test]
    public void PrintAcceptsEmptyItems()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT ,;'"));

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(1));
            Assert.That(executor.Runtime.PrintColumn, Is.Zero);
        });
    }

    [Test]
    public void PrintSupportsTemporaryColorAndFlashControls()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT INK 2;PAPER 4;BRIGHT 1;FLASH 1;\"X\";"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.GetAttribute(0, 0), Is.EqualTo(226));
            Assert.That(executor.Runtime.Ink, Is.Zero);
            Assert.That(executor.Runtime.Paper, Is.EqualTo(7));
            Assert.That(executor.Runtime.Bright, Is.False);
            Assert.That(executor.Runtime.Flash, Is.False);
        });
    }

    [Test]
    public void PrintAllowsATerminalColorControl()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        executor.TryExecute(BasicTokenizer.Tokenize("PRINT AT 0,0; INK 2; PAPER 4;42; BRIGHT 0"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.GetAttribute(0, 0), Is.EqualTo(34));
            Assert.That(executor.Runtime.PrintColumn, Is.EqualTo(3));
        });
    }

    [Test]
    public void LetCanUseTheKempstonJoystickPort()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen())
        {
            Runtime =
            {
                JoystickProvider = () => 1
            }
        };
        executor.Runtime.SetVariable("B", 15);

        executor.TryExecute(BasicTokenizer.Tokenize(
            "LET B=B+((INKEY$=\"8\" OR IN 31=1) AND B<26)-((INKEY$=\"5\" OR IN 31=2) AND B>5)"));

        Assert.That(executor.Runtime.GetVariable("B"), Is.EqualTo(16));
    }

    [Test]
    public void BeepIsSilentButReturnsItsDurationInFiftiethsOfASecond()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        var result = executor.Execute(BasicTokenizer.Tokenize("BEEP 0.5,12"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Flow, Is.EqualTo(BasicStatementFlow.Pause));
            Assert.That(result.PauseFrames, Is.EqualTo(25));
        });
    }

    [Test]
    public void PrintScrollsWithoutPausingAtAScreenful()
    {
        var screen = new SpectrumScreen();
        var font = SpectrumFont.FromGlyphs(Enumerable.Repeat((byte)0xFF, 96 * 8).ToArray());
        var executor = new BasicStatementExecutor(screen, font);

        for (var line = 0; line < 30; line++)
        {
            executor.TryExecute(BasicTokenizer.Tokenize("PRINT \"X\""));
        }

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(SpectrumScreen.DrawingHeight / 8));
            Assert.That(screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(screen.IsScreenPixelSet(0, SpectrumScreen.DrawingHeight - 1), Is.True);
        });
    }

    [Test]
    public void ContinuousPrintScrollsTheUpperScreen()
    {
        var glyphs = new byte[96 * 8];
        Array.Fill(glyphs, (byte)0xFF, ('A' - 32) * 8, 8);
        var executor = new BasicStatementExecutor(new SpectrumScreen(), SpectrumFont.FromGlyphs(glyphs));

        for (var i = 0; i < 130; i++)
        {
            executor.TryExecute(BasicTokenizer.Tokenize("PRINT \"AAAAAA\";"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.PrintRow, Is.EqualTo(SpectrumScreen.DrawingHeight / 8 - 1));
            Assert.That(executor.Runtime.Screen.IsScreenPixelSet(0, 0), Is.True);
            Assert.That(executor.Runtime.Screen.IsScreenPixelSet(0, SpectrumScreen.DrawingHeight), Is.False);
        });
    }

    [Test]
    public void DrawCircleAndPointShareTheBitmap()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 20,20"));
        executor.TryExecute(BasicTokenizer.Tokenize("DRAW 10,5"));
        executor.TryExecute(BasicTokenizer.Tokenize("CIRCLE 50,50,10"));

        var evaluator = new BasicExpressionEvaluator(executor.Runtime);

        Assert.Multiple(() =>
        {
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("POINT(30,25)")), Is.EqualTo(1));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("POINT(60,50)")), Is.EqualTo(1));
        });
    }

    [Test]
    public void DrawAcceptsTemporaryColorItems()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        executor.ExecuteSequence(BasicTokenizer.Tokenize("PLOT PAPER 0;7,40:DRAW PAPER 2;INK 6;BRIGHT 1;FLASH 1;241,0"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.GetAttribute(16, 31), Is.EqualTo(0xD6));
            Assert.That(executor.Runtime.Ink, Is.Zero);
            Assert.That(executor.Runtime.Paper, Is.EqualTo(7));
            Assert.That(executor.Runtime.Bright, Is.False);
            Assert.That(executor.Runtime.Flash, Is.False);
        });
    }

    [Test]
    public void CircleAcceptsTemporaryColorItems()
    {
        var screen = new SpectrumScreen();
        var executor = new BasicStatementExecutor(screen);

        executor.TryExecute(BasicTokenizer.Tokenize("CIRCLE PAPER 4;INK 2;BRIGHT 1;FLASH 1;30,30,5"));

        Assert.Multiple(() =>
        {
            Assert.That(screen.GetAttribute(18, 4), Is.EqualTo(0xE2));
            Assert.That(executor.Runtime.Ink, Is.Zero);
            Assert.That(executor.Runtime.Paper, Is.EqualTo(7));
            Assert.That(executor.Runtime.Bright, Is.False);
            Assert.That(executor.Runtime.Flash, Is.False);
        });
    }

    [Test]
    public void DrawSupportsACircularArc()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 50,50"));

        executor.TryExecute(BasicTokenizer.Tokenize("DRAW 20,0,PI"));

        var evaluator = new BasicExpressionEvaluator(executor.Runtime);
        Assert.Multiple(() =>
        {
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("POINT(50,50)")), Is.EqualTo(1));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("POINT(60,40)")), Is.EqualTo(1));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("POINT(70,50)")), Is.EqualTo(1));
        });
    }

    [Test]
    public void DrawReportsItsOutOfBoundsEndpoint()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 10,20"));

        var exception = Assert.Throws<BasicSyntaxException>(() =>
            executor.TryExecute(BasicTokenizer.Tokenize("DRAW 246,1")));

        Assert.That(exception!.Message, Is.EqualTo("DRAW 256,21 invalid"));
    }

    [Test]
    public void PokeAndPeekUsePersistent48KRam()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        var evaluator = new BasicExpressionEvaluator(executor.Runtime);

        executor.TryExecute(BasicTokenizer.Tokenize("POKE 32768,42"));
        executor.Runtime.ResetForRun();

        Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("PEEK 32768")), Is.EqualTo(42));
    }

    [TestCase("POKE 16383,1")]
    [TestCase("POKE 65536,1")]
    [TestCase("POKE 32768,256")]
    public void PokeRejectsValuesOutsideSpectrumRam(string statement)
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());

        Assert.That(
            () => executor.TryExecute(BasicTokenizer.Tokenize(statement)),
            Throws.TypeOf<BasicSyntaxException>());
    }

    [Test]
    public void ClearRemovesVariablesAndArraysButPreservesRam()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("LET A=42"));
        executor.TryExecute(BasicTokenizer.Tokenize("DIM B(2)"));
        executor.TryExecute(BasicTokenizer.Tokenize("LET B(1)=7"));
        executor.TryExecute(BasicTokenizer.Tokenize("POKE 32768,99"));
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 20,30"));
        executor.Runtime.SetData([
            (10, BasicDataValue.FromNumber(1)),
            (20, BasicDataValue.FromNumber(2))
        ]);
        Assert.That(executor.Runtime.ReadData(), Is.EqualTo(1));

        executor.TryExecute(BasicTokenizer.Tokenize("CLEAR 65535"));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => executor.Runtime.GetVariable("A"),
                Throws.TypeOf<BasicVariableNotFoundException>().With.Message.EqualTo("Variable not found"));
            Assert.That(() => executor.Runtime.GetArrayValue("B", [1]), Throws.TypeOf<BasicSyntaxException>());
            Assert.That(executor.Runtime.Memory.Peek(32768), Is.EqualTo(99));
            Assert.That(executor.Runtime.PlotX, Is.Zero);
            Assert.That(executor.Runtime.PlotY, Is.Zero);
            Assert.That(executor.Runtime.Screen.IsPixelSet(20, 30), Is.False);
            Assert.That(executor.Runtime.ReadData(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ResetForNewClearsVariablesAndPlotPositionButPreservesPaper()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("LET A=42"));
        executor.TryExecute(BasicTokenizer.Tokenize("PAPER 3"));
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 20,30"));

        executor.Runtime.ResetForNew();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => executor.Runtime.GetVariable("A"),
                Throws.TypeOf<BasicVariableNotFoundException>().With.Message.EqualTo("Variable not found"));
            Assert.That(executor.Runtime.Paper, Is.EqualTo(3));
            Assert.That(executor.Runtime.PlotX, Is.Zero);
            Assert.That(executor.Runtime.PlotY, Is.Zero);
        });
    }

    [Test]
    public void ClearScreenResetsThePlotPosition()
    {
        var executor = new BasicStatementExecutor(new SpectrumScreen());
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 20,30"));

        executor.TryExecute(BasicTokenizer.Tokenize("CLS"));

        Assert.Multiple(() =>
        {
            Assert.That(executor.Runtime.PlotX, Is.Zero);
            Assert.That(executor.Runtime.PlotY, Is.Zero);
        });
    }

    [Test]
    public void MachineResetRestoresTheDefaultColorsAndPlotPosition()
    {
        var screen = new SpectrumScreen { BorderColor = 2 };
        var executor = new BasicStatementExecutor(screen);
        executor.TryExecute(BasicTokenizer.Tokenize("PAPER 3"));
        executor.TryExecute(BasicTokenizer.Tokenize("PLOT 20,30"));

        executor.Runtime.ResetMachine();

        Assert.Multiple(() =>
        {
            Assert.That(screen.BorderColor, Is.EqualTo(7));
            Assert.That(executor.Runtime.Paper, Is.EqualTo(7));
            Assert.That(executor.Runtime.PlotX, Is.Zero);
            Assert.That(executor.Runtime.PlotY, Is.Zero);
        });
    }
}
