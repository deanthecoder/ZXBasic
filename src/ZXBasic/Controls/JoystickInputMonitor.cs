// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using SharpHook;
using SharpHook.Native;

namespace ZXBasic.Controls;

internal sealed class JoystickInputMonitor : IDisposable
{
    private readonly SimpleGlobalHook m_keyboardHook = new();
    private int m_state;
    private int m_hasStarted;

    public int State => Volatile.Read(ref m_state);

    public JoystickInputMonitor()
    {
        m_keyboardHook.KeyPressed += (_, args) => SetKeyState(args.Data.KeyCode, true);
        m_keyboardHook.KeyReleased += (_, args) => SetKeyState(args.Data.KeyCode, false);
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref m_hasStarted, 1) != 0)
        {
            return;
        }

        _ = m_keyboardHook.RunAsync();
    }

    public void Dispose()
    {
        m_keyboardHook.Dispose();
    }

    private void SetKeyState(KeyCode key, bool isPressed)
    {
        var bit = key switch
        {
            KeyCode.VcRight => 1,
            KeyCode.VcLeft => 2,
            KeyCode.VcDown => 4,
            KeyCode.VcUp => 8,
            _ => 0
        };
        if (bit == 0)
        {
            return;
        }

        if (isPressed)
        {
            Interlocked.Or(ref m_state, bit);
        }
        else
        {
            Interlocked.And(ref m_state, ~bit);
        }
    }
}
