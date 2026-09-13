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

namespace ZXBasic.Tests;

public class BasicTokenizerTests
{
    [Test]
    public void UsesOriginalSpectrumKeywordValues()
    {
        var tokens = BasicTokenizer.Tokenize("PLOT BRIGHT 1; A+110, S-15");

        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Keyword, Is.EqualTo(BasicKeyword.Plot));
            Assert.That((byte)tokens[0].Keyword!, Is.EqualTo(246));
            Assert.That(tokens[1].Keyword, Is.EqualTo(BasicKeyword.Bright));
            Assert.That((byte)tokens[1].Keyword!, Is.EqualTo(220));
        });
    }

    [Test]
    public void RecognizesCompoundKeywordsAndPowerOperator()
    {
        var tokens = BasicTokenizer.Tokenize("IF A<>0 THEN GO TO 10↑2");

        Assert.That(tokens.Where(token => token.Keyword.HasValue).Select(token => token.Keyword), Is.EqualTo(new BasicKeyword?[]
        {
            BasicKeyword.If,
            BasicKeyword.NotEqual,
            BasicKeyword.Then,
            BasicKeyword.GoTo
        }));
        Assert.That(tokens.Any(token => token is { Kind: BasicTokenKind.Operator, Text: "↑" }), Is.True);
    }

    [Test]
    public void RecognizesGotoAsAnAliasForGoTo()
    {
        var tokens = BasicTokenizer.Tokenize("GOTO 10");

        Assert.That(tokens[0].Keyword, Is.EqualTo(BasicKeyword.GoTo));
    }

    [Test]
    public void LeavesStringsAndRemarksUntokenized()
    {
        var tokens = BasicTokenizer.Tokenize("PRINT \"GO TO\": REM PRINT IS TEXT");

        Assert.Multiple(() =>
        {
            Assert.That(tokens.Count(token => token.Keyword == BasicKeyword.Print), Is.EqualTo(1));
            Assert.That(tokens.Single(token => token.Kind == BasicTokenKind.String).Text, Is.EqualTo("\"GO TO\""));
            Assert.That(tokens.Last().Kind, Is.EqualTo(BasicTokenKind.Comment));
        });
    }
}
