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

public class BasicProgramTests
{
    [Test]
    public void ListsLinesInNumberOrder()
    {
        var program = new BasicProgram();

        program.Enter("20 PRINT 2");
        program.Enter("10 PRINT 1");

        Assert.That(program.GetListing(), Is.EqualTo(new[] { "10 PRINT 1", "20 PRINT 2" }));
    }

    [Test]
    public void ListsFromARequestedLineNumber()
    {
        var program = new BasicProgram();
        program.Enter("10 PRINT 1");
        program.Enter("20 PRINT 2");
        program.Enter("30 PRINT 3");

        Assert.That(program.GetListing(20), Is.EqualTo(new[] { "20 PRINT 2", "30 PRINT 3" }));
    }

    [Test]
    public void RetrievesTheCompleteSourceForEditing()
    {
        var program = new BasicProgram();
        program.Enter("10 PRINT \"A LONG LINE THAT MAY WRAP ON SCREEN\"");

        Assert.Multiple(() =>
        {
            Assert.That(program.GetSourceLine(10), Is.EqualTo("10 PRINT \"A LONG LINE THAT MAY WRAP ON SCREEN\""));
            Assert.That(program.GetSourceLine(20), Is.Null);
        });
    }

    [Test]
    public void ReplacesAndDeletesLinesByNumber()
    {
        var program = new BasicProgram();

        Assert.That(program.Enter("10 PRINT 1"), Is.EqualTo(ProgramLineChange.Added));
        Assert.That(program.Enter("10 PRINT 2"), Is.EqualTo(ProgramLineChange.Replaced));
        Assert.That(program.Enter("10"), Is.EqualTo(ProgramLineChange.Deleted));
        Assert.That(program.Lines, Is.Empty);
    }

    [Test]
    public void RenumberChangesLinesAndLiteralControlFlowTargets()
    {
        var program = new BasicProgram();
        program.Enter("100 GO TO 300");
        program.Enter("200 IF A THEN 300");
        program.Enter("300 GO SUB 100:RESTORE 200");

        program.Renumber(10, 5);

        Assert.That(program.GetListing(), Is.EqualTo(new[]
        {
            "10 GO TO 20",
            "15 IF A THEN 20",
            "20 GO SUB 10:RESTORE 15"
        }));
    }

    [Test]
    public void RenumberLeavesComputedAndMissingTargetsUnchanged()
    {
        var program = new BasicProgram();
        program.Enter("100 GO TO A");
        program.Enter("200 GO TO 999");

        program.Renumber();

        Assert.That(program.GetListing(), Is.EqualTo(new[] { "10 GO TO A", "20 GO TO 999" }));
    }

    [Test]
    public void StoresTokenizedStatements()
    {
        var program = new BasicProgram();

        program.Enter("10 GO TO 10");

        Assert.That(program.Lines.Single().Tokens[0].Keyword, Is.EqualTo(BasicKeyword.GoTo));
    }

    [Test]
    public void EntersAMultilinePastedListingAndNormalizesItsCase()
    {
        var program = new BasicProgram();

        var lastLineNumber = program.EnterListing("10 print \"hello\"\n20 go to 10");

        Assert.Multiple(() =>
        {
            Assert.That(lastLineNumber, Is.EqualTo(20));
            Assert.That(program.GetListing(), Is.EqualTo(new[]
            {
                "10 PRINT \"HELLO\"",
                "20 GO TO 10"
            }));
        });
    }

    [Test]
    public void RejectsAnInvalidPastedListingBeforeChangingTheProgram()
    {
        var program = new BasicProgram();

        Assert.That(
            () => program.EnterListing("10 PRINT \"HELLO\"\nTHIS IS NOT BASIC"),
            Throws.TypeOf<BasicSyntaxException>());
        Assert.That(program.Lines, Is.Empty);
    }

    [Test]
    public void ReplaceListingRemovesThePreviousProgram()
    {
        var program = new BasicProgram();
        program.Enter("10 PRINT \"OLD\"");
        program.Enter("20 STOP");

        program.ReplaceListing(["100 PRINT \"NEW\""]);

        Assert.That(program.GetListing(), Is.EqualTo(new[] { "100 PRINT \"NEW\"" }));
    }

    [Test]
    public void ListingImportKeepsTheValidPrefixAndReportsTheBadLine()
    {
        var program = new BasicProgram();
        program.Enter("5 PRINT \"OLD\"");

        var result = program.EnterListingUntilError(
            "10 PRINT \"GOOD\"\n20 RANDOMIZE USR 1234\n30 PRINT \"UNREACHED\"",
            replaceExisting: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.LastLineNumber, Is.EqualTo(10));
            Assert.That(result.InvalidLine, Is.EqualTo("20 RANDOMIZE USR 1234"));
            Assert.That(program.GetListing(), Is.EqualTo(new[] { "10 PRINT \"GOOD\"" }));
        });
    }

    [Test]
    public void RejectsUnsupportedProgramLines()
    {
        var program = new BasicProgram();

        Assert.That(() => program.Enter("10 OUT 254, 0"), Throws.TypeOf<BasicSyntaxException>());
    }

    [Test]
    public void AutomaticListingIncludesLinesFollowingTheCurrentLineWhenTheyFit()
    {
        var program = new BasicProgram();
        program.Enter("10 REM FIRST");
        program.Enter("20 REM SECOND");
        program.Enter("30 REM THIRD");

        var listing = program.GetAutomaticListing(20, columns: 32, rows: 23);

        Assert.That(listing, Is.EqualTo(new[] { "10 REM FIRST", "20>REM SECOND", "30 REM THIRD" }));
    }

    [Test]
    public void AutomaticListingKeepsOnlyTheLastPageThatFits()
    {
        var program = new BasicProgram();
        for (var lineNumber = 10; lineNumber <= 300; lineNumber += 10)
            program.Enter($"{lineNumber} REM LINE {lineNumber}");

        var listing = program.GetAutomaticListing(300, columns: 32, rows: 3);

        Assert.That(listing, Is.EqualTo(new[]
        {
            "280 REM LINE 280",
            "290 REM LINE 290",
            "300>REM LINE 300"
        }));
    }

    [Test]
    public void AutomaticListingAfterDeletionEndsAtThePreviousLine()
    {
        var program = new BasicProgram();
        program.Enter("10 REM FIRST");
        program.Enter("20 REM SECOND");
        program.Enter("20");

        Assert.That(
            program.GetAutomaticListing(20, columns: 32, rows: 23),
            Is.EqualTo(new[] { "10 REM FIRST" }));
    }
}
