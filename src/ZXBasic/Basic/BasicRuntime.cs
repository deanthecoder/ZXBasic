// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;
using ZXBasic.Emulation;

namespace ZXBasic.Basic;

public sealed class BasicRuntime
{
    public const int UdgAddress = 65368;
    private const int PrintRows = SpectrumScreen.DrawingHeight / 8;
    private readonly Dictionary<string, double> m_variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> m_stringVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BasicNumericArray> m_arrays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BasicStringArray> m_stringArrays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BasicUserFunction> m_functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<IReadOnlyDictionary<string, double>> m_localScopes = new();
    private readonly List<(int LineNumber, BasicDataValue Value)> m_data = [];
    private int m_dataPosition;

    public SpectrumScreen Screen { get; }
    public SpectrumMemory Memory { get; }
    public SpectrumFont? Font { get; }
    public Random Random { get; private set; } = new();
    public Func<string>? InkeyProvider { get; set; }
    public Func<int>? JoystickProvider { get; set; }
    public byte Ink { get; set; }
    public byte Paper { get; set; } = 7;
    public bool Bright { get; set; }
    public bool Flash { get; set; }
    public bool Inverse { get; set; }
    public bool Over { get; set; }
    public int PrintColumn { get; private set; }
    public int PrintRow { get; private set; }
    public int PlotX { get; set; }
    public int PlotY { get; set; }
    public int MouseX { get; private set; } = -1;
    public int MouseY { get; private set; } = -1;
    public int OldMouseX { get; private set; } = -1;
    public int OldMouseY { get; private set; } = -1;
    public int MouseButtons { get; private set; }
    private int m_sampledMouseX = -1;
    private int m_sampledMouseY = -1;

    public BasicRuntime(SpectrumScreen screen, SpectrumFont? font = null)
    {
        Screen = screen;
        Memory = new SpectrumMemory(screen);
        Font = font;
    }

    public double GetVariable(string name)
    {
        if (name.Equals("_MX", StringComparison.OrdinalIgnoreCase))
        {
            OldMouseX = m_sampledMouseX;
            OldMouseY = m_sampledMouseY;
            m_sampledMouseX = MouseX;
            m_sampledMouseY = MouseY;
            return m_sampledMouseX;
        }
        if (name.Equals("_MY", StringComparison.OrdinalIgnoreCase))
            return m_sampledMouseY;
        if (name.Equals("_MB", StringComparison.OrdinalIgnoreCase))
            return MouseButtons;
        if (name.Equals("_OMX", StringComparison.OrdinalIgnoreCase))
            return OldMouseX;
        if (name.Equals("_OMY", StringComparison.OrdinalIgnoreCase))
            return OldMouseY;
        if (m_localScopes.Count > 0 && m_localScopes.Peek().TryGetValue(name, out var localValue))
            return localValue;
        return m_variables.GetValueOrDefault(name);
    }

    public void SetMouseState(int x, int y, int buttons)
    {
        MouseX = x;
        MouseY = y;
        MouseButtons = buttons;
    }

    public static int GetUdgAddress(char character)
    {
        var letter = char.ToUpperInvariant(character);
        if (letter is < 'A' or > 'U')
            throw new BasicSyntaxException("USR needs a UDG letter from A to U.", 0);
        return UdgAddress + (letter - 'A') * 8;
    }

    public void SetVariable(string name, double value)
    {
        m_variables[name] = value;
    }

    public string GetStringVariable(string name)
    {
        if (m_stringArrays.TryGetValue(name, out var array) && array.Rank == 0)
        {
            return array.Get([]);
        }
        return m_stringVariables.GetValueOrDefault(name, string.Empty);
    }

    public void SetStringVariable(string name, string value)
    {
        if (m_stringArrays.TryGetValue(name, out var array) && array.Rank == 0)
        {
            array.Set([], value);
            return;
        }
        m_stringVariables[name] = value;
    }

    public void DefineStringArray(string name, IReadOnlyList<int> dimensions)
    {
        m_stringArrays[name] = new BasicStringArray(dimensions);
    }

    public bool TryGetStringArrayRank(string name, out int rank)
    {
        if (m_stringArrays.TryGetValue(name, out var array))
        {
            rank = array.Rank;
            return true;
        }

        rank = 0;
        return false;
    }

    public string GetStringArrayValue(string name, IReadOnlyList<int> indices)
    {
        if (!m_stringArrays.TryGetValue(name, out var array))
        {
            throw new BasicSyntaxException($"String array {name} has not been dimensioned.", 0);
        }
        return array.Get(indices);
    }

    public void SetStringArrayValue(string name, IReadOnlyList<int> indices, string value)
    {
        if (!m_stringArrays.TryGetValue(name, out var array))
        {
            throw new BasicSyntaxException($"String array {name} has not been dimensioned.", 0);
        }
        array.Set(indices, value);
    }

    public string ReadScreenCharacter(int row, int column)
    {
        if (Font == null)
        {
            throw new BasicSyntaxException("SCREEN$ needs an available Spectrum font.", 0);
        }

        Span<byte> glyph = stackalloc byte[8];
        try
        {
            Screen.CopyGlyph(row, column, glyph);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new BasicSyntaxException("SCREEN$ position is outside the screen.", 0);
        }

        return Font.Recognize(glyph)?.ToString() ?? string.Empty;
    }

    public string ReadInkey()
    {
        return InkeyProvider?.Invoke() ?? string.Empty;
    }

    public int ReadInputPort(int port)
    {
        if (port != 31)
        {
            throw new BasicSyntaxException("Only joystick port IN 31 is supported.", 0);
        }

        return JoystickProvider?.Invoke() ?? 0;
    }

    public void DefineArray(string name, IReadOnlyList<int> dimensions)
    {
        m_arrays[name] = new BasicNumericArray(dimensions);
    }

    public double GetArrayValue(string name, IReadOnlyList<int> indices)
    {
        if (!m_arrays.TryGetValue(name, out var array))
            throw new BasicSyntaxException($"Array {name} has not been dimensioned.", 0);
        return array.Get(indices);
    }

    public void SetArrayValue(string name, IReadOnlyList<int> indices, double value)
    {
        if (!m_arrays.TryGetValue(name, out var array))
            throw new BasicSyntaxException($"Array {name} has not been dimensioned.", 0);
        array.Set(indices, value);
    }

    public void EraseArray(string name)
    {
        if (!m_arrays.Remove(name) && !m_stringArrays.Remove(name))
        {
            throw new BasicSyntaxException($"Array {name} has not been dimensioned.", 0);
        }
    }

    public void DefineFunction(BasicUserFunction function)
    {
        m_functions[function.Name] = function;
    }

    public void SetData(IEnumerable<(int LineNumber, BasicDataValue Value)> values)
    {
        m_data.Clear();
        m_data.AddRange(values);
        m_dataPosition = 0;
    }

    public double ReadData()
    {
        var value = ReadNextData();
        if (value.IsString)
        {
            throw new BasicSyntaxException("READ expected numeric DATA.", 0);
        }
        return value.Number!.Value;
    }

    public string ReadStringData()
    {
        var value = ReadNextData();
        if (!value.IsString)
        {
            throw new BasicSyntaxException("READ expected string DATA.", 0);
        }
        return value.Text!;
    }

    public void RestoreData(int lineNumber = 0)
    {
        var position = m_data.FindIndex(item => item.LineNumber >= lineNumber);
        m_dataPosition = position < 0 ? m_data.Count : position;
    }

    private BasicDataValue ReadNextData()
    {
        if (m_dataPosition >= m_data.Count)
        {
            throw new BasicSyntaxException("Out of DATA.", 0);
        }

        return m_data[m_dataPosition++].Value;
    }

    public double InvokeFunction(string name, IReadOnlyList<double> arguments)
    {
        if (!m_functions.TryGetValue(name, out var function))
            throw new BasicSyntaxException($"Function {name} has not been defined.", 0);
        if (arguments.Count != function.Parameters.Count)
            throw new BasicSyntaxException($"Function {name} has the wrong number of arguments.", 0);

        var locals = function.Parameters
            .Select((parameter, index) => (parameter, value: arguments[index]))
            .ToDictionary(item => item.parameter, item => item.value, StringComparer.OrdinalIgnoreCase);
        m_localScopes.Push(locals);
        try
        {
            return new BasicExpressionEvaluator(this).Evaluate(function.Expression);
        }
        finally
        {
            m_localScopes.Pop();
        }
    }

    public void ResetForRun()
    {
        m_variables.Clear();
        m_stringVariables.Clear();
        m_arrays.Clear();
        m_stringArrays.Clear();
        m_functions.Clear();
        m_localScopes.Clear();
        m_data.Clear();
        m_dataPosition = 0;
        Ink = 0;
        Paper = 7;
        Bright = false;
        Flash = false;
        Inverse = false;
        Over = false;
        PrintColumn = 0;
        PrintRow = 0;
        PlotX = 0;
        PlotY = 0;
        Screen.Clear(Ink, Paper, Bright);
    }

    public void ClearScreen()
    {
        Screen.Clear(Ink, Paper, Bright);
        PrintColumn = 0;
        PrintRow = 0;
    }

    public void ClearVariables()
    {
        m_variables.Clear();
        m_stringVariables.Clear();
        m_arrays.Clear();
        m_stringArrays.Clear();
        m_localScopes.Clear();
    }

    public void Write(string text)
    {
        if (Font == null)
            throw new BasicSyntaxException("PRINT needs an available Spectrum font.", 0);

        foreach (var character in text)
        {
            if (character == '\n')
            {
                NewLine();
                continue;
            }

            if (PrintColumn >= SpectrumScreen.Columns)
            {
                NewLine();
            }

            Screen.DrawGlyph(PrintColumn, PrintRow, Font[character], Ink, Paper, Bright, Flash, Inverse, Over);
            PrintColumn++;
        }
    }

    public void WriteNumber(double value)
    {
        var text = value.ToString("G10", CultureInfo.InvariantCulture);
        Write(value >= 0 ? " " + text : text);
    }

    public void SetPrintPosition(int row, int column)
    {
        if (row is < 0 or >= PrintRows || column is < 0 or >= SpectrumScreen.Columns)
        {
            throw new BasicSyntaxException("AT position is outside the screen.", 0);
        }

        PrintRow = row;
        PrintColumn = column;
    }

    public void Tab(int column)
    {
        if (column is < 0 or > 255)
        {
            throw new BasicSyntaxException("TAB needs a value from 0 to 255.", 0);
        }

        var target = column % SpectrumScreen.Columns;
        if (target < PrintColumn)
        {
            NewLine();
        }

        PrintColumn = target;
    }

    public void AdvanceToNextPrintZone()
    {
        var target = (PrintColumn / 16 + 1) * 16;
        if (target >= SpectrumScreen.Columns)
        {
            NewLine();
        }
        else
        {
            PrintColumn = target;
        }
    }

    public void NewLine()
    {
        PrintColumn = 0;
        PrintRow++;
        if (PrintRow >= PrintRows)
        {
            Screen.ScrollUp(Paper, Bright, PrintRows);
            PrintRow = PrintRows - 1;
        }
    }

    public void SetRandomSeed(int seed)
    {
        Random = new Random(seed);
    }
}
