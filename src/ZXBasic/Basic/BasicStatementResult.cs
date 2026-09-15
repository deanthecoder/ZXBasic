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

public enum BasicStatementFlow
{
    Continue,
    Stop,
    Pause,
    Beep
}

public readonly record struct BasicStatementResult(
    bool Handled,
    BasicStatementFlow Flow = BasicStatementFlow.Continue,
    int PauseFrames = 0,
    double BeepDuration = 0,
    double BeepPitch = 0)
{
    public static BasicStatementResult Continue { get; } = new(true);
    public static BasicStatementResult Stop { get; } = new(true, BasicStatementFlow.Stop);
    public static BasicStatementResult NotHandled { get; } = new(false);

    public static BasicStatementResult Pause(int frames)
    {
        return new BasicStatementResult(true, BasicStatementFlow.Pause, frames);
    }

    public static BasicStatementResult Beep(double duration, double pitch)
    {
        return new BasicStatementResult(
            true,
            BasicStatementFlow.Beep,
            BeepDuration: duration,
            BeepPitch: pitch);
    }
}
