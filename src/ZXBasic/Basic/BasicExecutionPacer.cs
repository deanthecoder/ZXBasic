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

public sealed class BasicExecutionPacer
{
    private BasicExecutionSpeed m_speed;
    private int m_baselineStatementCount;
    private long m_baselineMilliseconds;

    public BasicExecutionPacer(BasicExecutionSpeed speed, int statementCount, long milliseconds)
    {
        Reset(speed, statementCount, milliseconds);
    }

    public double GetDelayMilliseconds(BasicExecutionSpeed speed, int statementCount, long milliseconds)
    {
        if (speed != m_speed)
        {
            Reset(speed, statementCount, milliseconds);
            return 0;
        }

        var statementsPerSecond = speed switch
        {
            BasicExecutionSpeed.Spectrum => 500,
            BasicExecutionSpeed.Fast => 5000,
            _ => 0
        };
        if (statementsPerSecond == 0)
        {
            return 0;
        }

        var statementsSinceBaseline = statementCount - m_baselineStatementCount;
        var elapsedMilliseconds = milliseconds - m_baselineMilliseconds;
        var expectedMilliseconds = statementsSinceBaseline * 1000.0 / statementsPerSecond;
        return Math.Max(0, expectedMilliseconds - elapsedMilliseconds);
    }

    private void Reset(BasicExecutionSpeed speed, int statementCount, long milliseconds)
    {
        m_speed = speed;
        m_baselineStatementCount = statementCount;
        m_baselineMilliseconds = milliseconds;
    }
}
