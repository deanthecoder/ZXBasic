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

public class BasicExpressionEvaluatorTests
{
    [Test]
    public void ValStringEvaluatesBasicStoredInAString()
    {
        var runtime = new BasicRuntime(new SpectrumScreen());
        runtime.SetStringVariable("A$", "\"ZX\"+CHR$ 33");

        var value = new BasicExpressionEvaluator(runtime)
            .EvaluateString(BasicTokenizer.Tokenize("VAL$ A$"));

        Assert.That(value, Is.EqualTo("ZX!"));
    }

    [TestCase("2+3*4", 14)]
    [TestCase("(2+3)*4", 20)]
    [TestCase("10↑2", 100)]
    [TestCase("INT -1.2", -2)]
    [TestCase("SQR 81+ABS -3", 12)]
    [TestCase("2<3 AND 4<>5", 1)]
    [TestCase("2>3 OR 4=4", 1)]
    [TestCase("BIN 101010", 42)]
    [TestCase("7 AND 2", 7)]
    [TestCase("7 AND 0", 0)]
    [TestCase("7 OR 0", 7)]
    [TestCase("7 OR 2", 1)]
    public void EvaluatesNumericExpressions(string expression, double expected)
    {
        Assert.That(Evaluate(expression), Is.EqualTo(expected).Within(0.0000001));
    }

    [Test]
    public void ReadsVariablesWithoutCaseSensitivity()
    {
        var runtime = new BasicRuntime(new SpectrumScreen());
        runtime.SetVariable("Answer", 42);

        var value = new BasicExpressionEvaluator(runtime).Evaluate(BasicTokenizer.Tokenize("ANSWER/2"));

        Assert.That(value, Is.EqualTo(21));
    }

    [Test]
    public void ReadsLiveMouseVariables()
    {
        var runtime = new BasicRuntime(new SpectrumScreen());
        runtime.SetMouseState(100, 60, 0);
        var evaluator = new BasicExpressionEvaluator(runtime);
        evaluator.Evaluate(BasicTokenizer.Tokenize("_MX"));
        evaluator.Evaluate(BasicTokenizer.Tokenize("_MY"));
        runtime.SetMouseState(120, 75, 5);

        Assert.Multiple(() =>
        {
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("_MX")), Is.EqualTo(120));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("_MY")), Is.EqualTo(75));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("_MB")), Is.EqualTo(5));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("_OMX")), Is.EqualTo(100));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("_OMY")), Is.EqualTo(60));
        });
    }

    [TestCase("USR \"A\"", BasicRuntime.UdgAddress)]
    [TestCase("USR \"D\"+7", BasicRuntime.UdgAddress + 31)]
    public void ResolvesSpectrumUdgAddresses(string expression, int expected)
    {
        Assert.That(Evaluate(expression), Is.EqualTo(expected));
    }

    [Test]
    public void EvaluatesStringVariablesConcatenationAndConversions()
    {
        var runtime = new BasicRuntime(new SpectrumScreen());
        runtime.SetStringVariable("NAME$", "ZX");
        var evaluator = new BasicExpressionEvaluator(runtime);

        Assert.Multiple(() =>
        {
            Assert.That(evaluator.EvaluateString(BasicTokenizer.Tokenize("NAME$+CHR$ 33")), Is.EqualTo("ZX!"));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("LEN NAME$")), Is.EqualTo(2));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("CODE NAME$")), Is.EqualTo(90));
            Assert.That(evaluator.Evaluate(BasicTokenizer.Tokenize("VAL \"12.5\"")), Is.EqualTo(12.5));
            Assert.That(evaluator.EvaluateString(BasicTokenizer.Tokenize("STR$ 42")), Is.EqualTo("42"));
        });
    }

    [TestCase("A$(2)", "P")]
    [TestCase("A$(2 TO 4)", "PEC")]
    [TestCase("A$(TO 3)", "SPE")]
    [TestCase("A$(5 TO)", "TRUM")]
    public void EvaluatesSpectrumStringSlices(string expression, string expected)
    {
        var runtime = new BasicRuntime(new SpectrumScreen());
        runtime.SetStringVariable("A$", "SPECTRUM");

        var value = new BasicExpressionEvaluator(runtime).EvaluateString(BasicTokenizer.Tokenize(expression));

        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void ScreenStringRecognizesARomGlyphOnTheScreen()
    {
        var glyphs = new byte[96 * 8];
        Array.Fill(glyphs, (byte)0xAA, ('A' - 32) * 8, 8);
        var font = SpectrumFont.FromGlyphs(glyphs);
        var screen = new SpectrumScreen();
        screen.DrawText(3, 5, "A", font);
        var evaluator = new BasicExpressionEvaluator(new BasicRuntime(screen, font));

        var value = evaluator.EvaluateString(BasicTokenizer.Tokenize("SCREEN$(5,3)"));

        Assert.That(value, Is.EqualTo("A"));
    }

    [Test]
    public void InkeyStringReadsAndConsumesTheCurrentNativeKey()
    {
        var keys = new Queue<string>(["A", string.Empty]);
        var runtime = new BasicRuntime(new SpectrumScreen())
        {
            InkeyProvider = keys.Dequeue
        };
        var evaluator = new BasicExpressionEvaluator(runtime);

        Assert.Multiple(() =>
        {
            Assert.That(evaluator.EvaluateString(BasicTokenizer.Tokenize("INKEY$")), Is.EqualTo("A"));
            Assert.That(evaluator.EvaluateString(BasicTokenizer.Tokenize("INKEY$")), Is.Empty);
        });
    }

    [TestCase("\"A\"=\"A\"", 1)]
    [TestCase("\"A\"<>\"B\"", 1)]
    [TestCase("\"A\"<\"B\"", 1)]
    public void EvaluatesStringComparisons(string expression, double expected)
    {
        Assert.That(Evaluate(expression), Is.EqualTo(expected));
    }

    [Test]
    public void RndReturnsAUnitIntervalValue()
    {
        var value = Evaluate("RND");

        Assert.That(value, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
    }

    [Test]
    public void ReadsNumericArraysAndUserFunctions()
    {
        var runtime = new BasicRuntime(new SpectrumScreen());
        runtime.DefineArray("A", [3]);
        runtime.SetArrayValue("A", [2], 20);
        runtime.DefineFunction(new BasicUserFunction(
            "DOUBLE",
            ["X"],
            BasicTokenizer.Tokenize("X*2")));

        var value = new BasicExpressionEvaluator(runtime).Evaluate(
            BasicTokenizer.Tokenize("FN DOUBLE(A(2))+1"));

        Assert.That(value, Is.EqualTo(41));
    }

    [Test]
    public void AttrReadsTheScreenAttributeCell()
    {
        var screen = new SpectrumScreen();
        screen.Plot(0, 0, ink: 2, paper: 6, bright: true);
        var evaluator = new BasicExpressionEvaluator(new BasicRuntime(screen));

        var value = evaluator.Evaluate(BasicTokenizer.Tokenize("ATTR(21,0)"));

        Assert.That(value, Is.EqualTo(114));
    }

    [TestCase("1/0")]
    [TestCase("SQR -1")]
    [TestCase("2+")]
    public void RejectsInvalidNumericOperations(string expression)
    {
        Assert.That(() => Evaluate(expression), Throws.TypeOf<BasicSyntaxException>());
    }

    private static double Evaluate(string expression)
    {
        var evaluator = new BasicExpressionEvaluator(new BasicRuntime(new SpectrumScreen()));
        return evaluator.Evaluate(BasicTokenizer.Tokenize(expression));
    }
}
