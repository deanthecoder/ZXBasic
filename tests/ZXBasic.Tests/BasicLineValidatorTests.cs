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

public class BasicLineValidatorTests
{
    [Test]
    public void AcceptsSafeUdgUsrExpressions()
    {
        Assert.That(BasicLineValidator.IsValid(
            "9000 RESTORE 9100: FOR F=USR \"A\" TO USR \"D\"+7: READ A: POKE F,A: NEXT F"), Is.True);
    }
    [TestCase("10 BORDER 1")]
    [TestCase("60 LET S=INT (80+30*SIN ((SQR (A*A+T*T))/12)-.7*T)")]
    [TestCase("120 PAUSE 0")]
    [TestCase("120")]
    [TestCase("RENUM")]
    [TestCase("RENUMBER 100,5")]
    [TestCase("RESET")]
    [TestCase("BORDER 3:PAPER 2:CLS")]
    [TestCase("850 RESTORE: GO TO 5")]
    public void AcceptsInitialLanguageForms(string line)
    {
        Assert.That(BasicLineValidator.IsValid(line), Is.True);
    }

    [TestCase("")]
    [TestCase("10000 PRINT 1")]
    [TestCase("10 PRINT (1")]
    [TestCase("10 RANDOMIZE USR 32768")]
    [TestCase("10 OUT 254, 0")]
    [TestCase("10 SAVE \"PROGRAM\"")]
    [TestCase("10 RESET")]
    [TestCase("RENUM 10,0,5")]
    public void RejectsInvalidOrUnsupportedForms(string line)
    {
        Assert.That(BasicLineValidator.IsValid(line), Is.False);
    }
}
