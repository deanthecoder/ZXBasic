// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ZXBasic.Basic;
using ZXBasic.Emulation;

namespace ZXBasic.Controls;

public sealed class SpectrumTerminal : Control
{
    private readonly SpectrumFont m_font = SpectrumFont.Load();
    private readonly SpectrumScreen m_screen = new();
    private readonly BasicProgram m_program = new();
    private readonly BasicStatementExecutor m_statementExecutor;
    private readonly BasicInterpreter m_interpreter;
    private readonly WriteableBitmap m_frame;
    private readonly WriteableBitmap m_crtFrame;
    private readonly CrtFrameRenderer m_crtRenderer;
    private readonly int[] m_framePixels;
    private readonly int[] m_crtPixels;
    private readonly DispatcherTimer m_cursorTimer;
    private IReadOnlyList<string> m_outputLines = [];
    private readonly Dictionary<int, string> m_visibleListingRows = [];
    private int m_outputLineOffset;
    private double m_scrollWheelRemainder;
    private int? m_selectedLineNumber;
    private bool m_isCrtEnabled = true;
    private string m_input = string.Empty;
    private string m_inputPrompt = string.Empty;
    private int m_cursor;
    private bool m_cursorVisible = true;
    private bool m_flashPhase;
    private bool m_hasError;
    private bool m_hasStarted;
    private bool m_preserveProgramScreen;
    private bool m_isPaused;
    private bool m_isRunning;
    private bool m_isAwaitingInput;
    private bool m_isWaitingForKey;
    private CancellationTokenSource? m_runCancellation;
    private TaskCompletionSource? m_runCompletion;
    private TaskCompletionSource<string>? m_inputCompletion;
    private TaskCompletionSource? m_keyCompletion;
    private string m_pendingInkey = string.Empty;

    public event EventHandler? FrameRefreshed;
    public WriteableBitmap Frame => m_frame;
    public WriteableBitmap CrtFrame => m_crtFrame;
    public bool IsRunning => m_isRunning;

    public bool IsCrtEnabled
    {
        get => m_isCrtEnabled;
        set
        {
            if (m_isCrtEnabled == value)
            {
                return;
            }

            m_isCrtEnabled = value;
            RefreshFrame();
        }
    }

    public BasicExecutionSpeed ExecutionSpeed
    {
        get => m_interpreter.ExecutionSpeed;
        set => m_interpreter.ExecutionSpeed = value;
    }

    public void LoadSnapshot(byte[] snapshot)
    {
        m_program.Clear();
        ShowLoadedProgram();
        var lines = SnaBasicImporter.Import(snapshot);
        var result = m_program.EnterListingUntilError(string.Join('\n', lines));
        ShowListingResult(result);
        if (!result.IsValid)
        {
            throw new BasicSyntaxException("The snapshot contains an unsupported BASIC line.", 0);
        }
    }

    public bool TryLoadSnapshot(byte[] snapshot)
    {
        try
        {
            LoadSnapshot(snapshot);
            return true;
        }
        catch (BasicSyntaxException)
        {
            if (m_input.Length == 0)
            {
                ShowLoadedProgram();
            }
            m_hasError = true;
            ShowCursor();
            return false;
        }
    }

    public void LoadListing(string listing)
    {
        var result = m_program.EnterListingUntilError(listing, replaceExisting: true);
        ShowListingResult(result);
        if (!result.IsValid)
        {
            throw new BasicSyntaxException("The listing contains an unsupported BASIC line.", 0);
        }
    }

    public bool TryLoadListing(string listing)
    {
        try
        {
            LoadListing(listing);
            return true;
        }
        catch (BasicSyntaxException)
        {
            return false;
        }
    }

    public string GetListingText()
    {
        return string.Join(Environment.NewLine, m_program.GetListing());
    }

    public void ResetMachine()
    {
        m_program.Clear();
        m_statementExecutor.Runtime.ResetForRun();
        m_hasStarted = false;
        m_hasError = false;
        m_preserveProgramScreen = false;
        m_isPaused = false;
        m_input = string.Empty;
        m_cursor = 0;
        m_selectedLineNumber = null;
        SetOutputLines([]);
        ShowCursor();
    }

    public SpectrumTerminal()
    {
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        m_statementExecutor = new BasicStatementExecutor(m_screen, m_font);
        m_statementExecutor.Runtime.InkeyProvider = ConsumeInkey;
        m_interpreter = new BasicInterpreter(m_statementExecutor);
        m_interpreter.ExecutionSpeed = BasicExecutionSpeed.Spectrum;

        m_frame = new WriteableBitmap(
            new PixelSize(SpectrumScreen.FrameWidth, SpectrumScreen.FrameHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        m_crtRenderer = new CrtFrameRenderer(SpectrumScreen.FrameWidth, SpectrumScreen.FrameHeight);
        m_crtFrame = new WriteableBitmap(
            new PixelSize(m_crtRenderer.OutputWidth, m_crtRenderer.OutputHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        m_framePixels = new int[SpectrumScreen.FrameWidth * SpectrumScreen.FrameHeight];
        m_crtPixels = new int[m_crtRenderer.OutputWidth * m_crtRenderer.OutputHeight];

        m_cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        m_cursorTimer.Tick += (_, _) =>
        {
            m_cursorVisible = !m_cursorVisible;
            m_flashPhase = !m_flashPhase;
            RefreshFrame();
        };
        m_cursorTimer.Start();

        Loaded += (_, _) =>
        {
            Focus();
            RefreshFrame();
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scale = Math.Min(Bounds.Width / SpectrumScreen.FrameWidth, Bounds.Height / SpectrumScreen.FrameHeight);
        var width = SpectrumScreen.FrameWidth * scale;
        var height = SpectrumScreen.FrameHeight * scale;
        var destination = new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        var frame = m_isCrtEnabled ? m_crtFrame : m_frame;
        context.DrawImage(frame, new Rect(frame.Size), destination);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        UpdateMouseState(e);
        Focus();
        if (m_isRunning || m_preserveProgramScreen)
        {
            return;
        }

        var row = GetTextRow(e.GetPosition(this));
        if (row == null || !m_visibleListingRows.TryGetValue(row.Value, out var listing))
        {
            return;
        }

        var digitCount = listing.TakeWhile(char.IsDigit).Count();
        if (digitCount == 0 || !int.TryParse(listing[..digitCount], out var lineNumber))
        {
            return;
        }

        var source = m_program.GetSourceLine(lineNumber);
        if (source == null)
        {
            return;
        }

        m_input = source;
        m_cursor = source.Length;
        m_selectedLineNumber = lineNumber;
        m_hasStarted = true;
        m_hasError = false;
        m_isPaused = false;
        ShowCursor();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateMouseState(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        UpdateMouseState(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        m_statementExecutor.Runtime.SetMouseState(-1, -1, 0);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (m_isRunning || m_preserveProgramScreen || m_outputLines.Count == 0 || e.Delta.Y == 0)
        {
            return;
        }

        m_scrollWheelRemainder -= e.Delta.Y * 0.5;
        var lines = (int)m_scrollWheelRemainder;
        if (lines != 0)
        {
            m_scrollWheelRemainder -= lines;
            m_outputLineOffset = Math.Clamp(m_outputLineOffset + lines, 0, m_outputLines.Count - 1);
            RefreshFrame();
        }
        e.Handled = true;
    }

    public bool StopExecution()
    {
        if (!m_isRunning)
        {
            return false;
        }

        m_runCancellation?.Cancel();
        Focus();
        return true;
    }

    public async Task<bool> StopExecutionAsync()
    {
        if (!StopExecution())
        {
            return false;
        }

        var completion = m_runCompletion;
        if (completion != null)
        {
            await completion.Task;
        }
        return true;
    }

    private void UpdateMouseState(PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var position = GetDisplayPosition(point.Position);
        if (position == null)
        {
            m_statementExecutor.Runtime.SetMouseState(-1, -1, 0);
            return;
        }

        var properties = point.Properties;
        var buttons = (properties.IsLeftButtonPressed ? 1 : 0) |
                      (properties.IsRightButtonPressed ? 2 : 0) |
                      (properties.IsMiddleButtonPressed ? 4 : 0);
        m_statementExecutor.Runtime.SetMouseState(position.Value.X, position.Value.Y, buttons);
    }

    private (int X, int Y)? GetDisplayPosition(Point position)
    {
        var scale = Math.Min(Bounds.Width / SpectrumScreen.FrameWidth, Bounds.Height / SpectrumScreen.FrameHeight);
        var width = SpectrumScreen.FrameWidth * scale;
        var height = SpectrumScreen.FrameHeight * scale;
        var frameX = (position.X - (Bounds.Width - width) / 2) / scale;
        var frameY = (position.Y - (Bounds.Height - height) / 2) / scale;
        if (frameX < SpectrumScreen.BorderX || frameX >= SpectrumScreen.BorderX + SpectrumScreen.Width ||
            frameY < SpectrumScreen.BorderY || frameY >= SpectrumScreen.BorderY + SpectrumScreen.Height)
        {
            return null;
        }

        return ((int)(frameX - SpectrumScreen.BorderX),
            SpectrumScreen.Height - 1 - (int)(frameY - SpectrumScreen.BorderY));
    }

    private int? GetTextRow(Point position)
    {
        var scale = Math.Min(Bounds.Width / SpectrumScreen.FrameWidth, Bounds.Height / SpectrumScreen.FrameHeight);
        var width = SpectrumScreen.FrameWidth * scale;
        var height = SpectrumScreen.FrameHeight * scale;
        var frameX = (position.X - (Bounds.Width - width) / 2) / scale;
        var frameY = (position.Y - (Bounds.Height - height) / 2) / scale;
        if (frameX < SpectrumScreen.BorderX || frameX >= SpectrumScreen.BorderX + SpectrumScreen.Width ||
            frameY < SpectrumScreen.BorderY || frameY >= SpectrumScreen.BorderY + SpectrumScreen.Height)
        {
            return null;
        }

        return (int)(frameY - SpectrumScreen.BorderY) / 8;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (m_isRunning && !m_isAwaitingInput)
        {
            var key = SanitizeInput(e.Text);
            if (key.Length > 0)
            {
                m_pendingInkey = key[..1];
            }
            e.Handled = true;
            return;
        }

        if (e.Text.IndexOfAny(['\r', '\n']) >= 0)
        {
            PasteText(e.Text);
            e.Handled = true;
            return;
        }

        var characters = SanitizeInput(e.Text);
        if (characters.Length == 0)
            return;

        if (m_isPaused)
        {
            m_isPaused = false;
            m_preserveProgramScreen = false;
            SetOutputLines([]);
            ShowCursor();
            e.Handled = true;
            return;
        }

        InsertText(characters);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (m_isRunning)
        {
            if (e.Key == Key.Escape)
            {
                m_runCancellation?.Cancel();
                e.Handled = true;
            }
            else if (m_isWaitingForKey)
            {
                m_keyCompletion?.TrySetResult();
                e.Handled = true;
            }
            else if (m_isAwaitingInput && e.Key == Key.Enter)
            {
                CompleteInput();
                e.Handled = true;
            }

            if (!m_isAwaitingInput || e.Handled)
            {
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            m_input = string.Empty;
            m_cursor = 0;
            m_hasError = false;
            ShowCursor();
            e.Handled = true;
            return;
        }

        if (IsPasteGesture(e))
        {
            if (m_isPaused)
            {
                m_isPaused = false;
                m_preserveProgramScreen = false;
                SetOutputLines([]);
            }

            PasteClipboard();
            e.Handled = true;
            return;
        }

        if (m_isPaused)
        {
            m_isPaused = false;
            m_preserveProgramScreen = false;
            SetOutputLines([]);
            ShowCursor();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                SubmitLine();
                break;
            case Key.Back when m_cursor > 0:
                m_input = m_input.Remove(--m_cursor, 1);
                m_hasError = false;
                break;
            case Key.Delete when m_cursor < m_input.Length:
                m_input = m_input.Remove(m_cursor, 1);
                m_hasError = false;
                break;
            case Key.Left:
                m_cursor = Math.Max(0, m_cursor - 1);
                break;
            case Key.Right:
                m_cursor = Math.Min(m_input.Length, m_cursor + 1);
                break;
            case Key.Home:
                m_cursor = 0;
                break;
            case Key.End:
                m_cursor = m_input.Length;
                break;
            default:
                return;
        }

        ShowCursor();
        e.Handled = true;
    }

    private async void PasteClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        var text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            PasteText(text);
        }
    }

    private void PasteText(string text)
    {
        var normalized = text.ReplaceLineEndings("\n");
        if (!normalized.Contains('\n'))
        {
            InsertText(SanitizeInput(normalized));
            return;
        }

        try
        {
            var result = m_program.EnterListingUntilError(normalized);
            if (!result.IsValid)
            {
                ShowListingResult(result);
                return;
            }
            if (result.LastLineNumber == null)
            {
                return;
            }

            m_input = string.Empty;
            m_cursor = 0;
            m_selectedLineNumber = result.LastLineNumber;
            m_hasError = false;
            m_hasStarted = true;
            m_isPaused = false;
            m_preserveProgramScreen = false;
            SetOutputLines(m_program.GetAutomaticListing(
                result.LastLineNumber.Value,
                SpectrumScreen.Columns,
                SpectrumScreen.Rows - 2));
        }
        catch (BasicSyntaxException)
        {
            m_hasError = true;
            m_hasStarted = true;
        }

        ShowCursor();
    }

    private void InsertText(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        m_input = m_input.Insert(m_cursor, text);
        m_cursor += text.Length;
        m_hasError = false;
        m_hasStarted = true;
        ShowCursor();
    }

    private static string SanitizeInput(string text)
    {
        return new string(text.Where(character => !char.IsControl(character)).ToArray()).ToUpperInvariant();
    }

    private static bool IsPasteGesture(KeyEventArgs e)
    {
        var commandModifiers = KeyModifiers.Control | KeyModifiers.Meta;
        return e.Key == Key.V && (e.KeyModifiers & commandModifiers) != KeyModifiers.None;
    }

    private async void SubmitLine()
    {
        if (string.IsNullOrWhiteSpace(m_input) && m_preserveProgramScreen)
        {
            m_preserveProgramScreen = false;
            m_isPaused = false;
            SetOutputLines(m_program.GetListing());
            m_hasError = false;
            ShowCursor();
            return;
        }

        if (!BasicLineValidator.IsValid(m_input))
        {
            m_hasError = true;
            ShowCursor();
            return;
        }

        var input = m_input.Trim();
        if (char.IsDigit(input[0]))
        {
            m_preserveProgramScreen = false;
            m_isPaused = false;
            m_program.Enter(input);
            var digitCount = input.TakeWhile(char.IsDigit).Count();
            var currentLineNumber = int.Parse(input[..digitCount]);
            m_selectedLineNumber = currentLineNumber;
            SetOutputLines(m_program.GetAutomaticListing(
                currentLineNumber,
                SpectrumScreen.Columns,
                SpectrumScreen.Rows - 2));
        }
        else if (input == "LIST" || input.StartsWith("LIST ", StringComparison.Ordinal))
        {
            m_preserveProgramScreen = false;
            m_isPaused = false;
            SetOutputLines(m_program.GetListing(EvaluateOptionalLineNumber(input) ?? 0));
        }
        else if (input == "NEW")
        {
            m_preserveProgramScreen = false;
            m_isPaused = false;
            m_program.Clear();
            m_selectedLineNumber = null;
            SetOutputLines([]);
        }
        else if (input.StartsWith("RENUM", StringComparison.Ordinal))
        {
            var (start, step) = ParseRenumberArguments(input);
            try
            {
                m_program.Renumber(start, step);
                m_selectedLineNumber = null;
            }
            catch (BasicSyntaxException)
            {
                m_hasError = true;
                ShowCursor();
                return;
            }
            m_preserveProgramScreen = false;
            m_isPaused = false;
            SetOutputLines(m_program.GetListing());
        }
        else if (input == "RUN" || input.StartsWith("RUN ", StringComparison.Ordinal))
        {
            var startLineNumber = EvaluateOptionalLineNumber(input);
            m_input = string.Empty;
            m_cursor = 0;
            m_hasError = false;
            m_preserveProgramScreen = true;
            m_isPaused = false;
            m_isRunning = true;
            var cancellation = new CancellationTokenSource();
            m_runCancellation = cancellation;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            m_runCompletion = completion;
            m_statementExecutor.Runtime.ClearScreen();
            RefreshFrame();
            try
            {
                var result = await m_interpreter.RunAsync(
                    m_program,
                    RefreshFrame,
                    cancellation.Token,
                    startLineNumber,
                    ReadInputAsync,
                    WaitForPauseAsync);
                m_isPaused = result.IsPaused;
                SetOutputLines([]);
            }
            catch (BasicRuntimeException exception)
            {
                m_isPaused = false;
                WriteRuntimeReport(exception);
                SetOutputLines([]);
            }
            finally
            {
                m_runCancellation = null;
                m_isRunning = false;
                m_isAwaitingInput = false;
                m_isWaitingForKey = false;
                cancellation.Dispose();
                completion.TrySetResult();
                if (ReferenceEquals(m_runCompletion, completion))
                {
                    m_runCompletion = null;
                }

                RefreshFrame();
            }
        }
        else
        {
            try
            {
                var tokens = BasicTokenizer.Tokenize(input);
                var result = m_statementExecutor.ExecuteSequence(tokens);
                m_preserveProgramScreen = result.Handled;
                m_isPaused = result.Flow == BasicStatementFlow.Pause;
                SetOutputLines(result.Handled ? [] : ["NOT IMPLEMENTED"]);
            }
            catch (BasicSyntaxException)
            {
                m_hasError = true;
                ShowCursor();
                return;
            }
        }

        m_input = string.Empty;
        m_cursor = 0;
        m_hasError = false;
        ShowCursor();
    }

    private void ShowCursor()
    {
        m_cursorVisible = true;
        m_cursorTimer.Stop();
        m_cursorTimer.Start();
        RefreshFrame();
    }

    private void RefreshFrame()
    {
        if (m_preserveProgramScreen)
        {
            if (m_isPaused || m_isRunning && !m_isAwaitingInput)
                CopyFrame(m_screen);
            else
            {
                var editorScreen = m_screen.Copy();
                DrawEditor(editorScreen);
                CopyFrame(editorScreen);
            }
            return;
        }

        m_screen.Clear();

        DrawOutput();

        if (!m_hasStarted)
        {
            m_screen.DrawText(0, 23, "© 2026 DEANTHECODER ZXBASIC", m_font);
            CopyFrame(m_screen);
            return;
        }

        DrawEditor(m_screen);

        CopyFrame(m_screen);
    }

    private void WriteRuntimeReport(BasicRuntimeException exception)
    {
        var runtime = m_statementExecutor.Runtime;
        runtime.NewLine();
        runtime.Write($"{exception.Message} IN {exception.LineNumber}:{exception.StatementNumber}");
        runtime.NewLine();
    }

    private async Task<string> ReadInputAsync(string prompt, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        m_inputCompletion = completion;
        m_isAwaitingInput = true;
        m_inputPrompt = prompt;
        m_input = string.Empty;
        m_cursor = 0;
        ShowCursor();
        try
        {
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            if (ReferenceEquals(m_inputCompletion, completion))
            {
                m_inputCompletion = null;
                m_isAwaitingInput = false;
                m_inputPrompt = string.Empty;
            }
        }
    }

    private void CompleteInput()
    {
        var entered = m_input;
        m_input = string.Empty;
        m_cursor = 0;
        m_inputCompletion?.TrySetResult(entered);
    }

    private async Task WaitForPauseAsync(int frames, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        m_keyCompletion = completion;
        m_isWaitingForKey = true;
        try
        {
            if (frames == 0)
            {
                await completion.Task.WaitAsync(cancellationToken);
            }
            else
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(frames / 50.0), cancellationToken);
                await Task.WhenAny(completion.Task, delay);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            if (ReferenceEquals(m_keyCompletion, completion))
            {
                m_keyCompletion = null;
                m_isWaitingForKey = false;
            }
        }
    }

    private string ConsumeInkey()
    {
        var key = m_pendingInkey;
        m_pendingInkey = string.Empty;
        return key;
    }

    private int? EvaluateOptionalLineNumber(string input)
    {
        var tokens = BasicTokenizer.Tokenize(input);
        if (tokens.Count == 1)
        {
            return null;
        }

        var value = new BasicExpressionEvaluator(m_statementExecutor.Runtime)
            .Evaluate(tokens.Skip(1).ToArray());
        var lineNumber = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
        if (lineNumber is < 0 or > 9999)
        {
            throw new BasicSyntaxException("A line number must be from 0 to 9999.", tokens[1].Position);
        }

        return lineNumber;
    }

    private static (int Start, int Step) ParseRenumberArguments(string input)
    {
        var commandLength = input.StartsWith("RENUMBER", StringComparison.Ordinal) ? 8 : 5;
        var arguments = input[commandLength..].Trim();
        if (arguments.Length == 0)
        {
            return (10, 10);
        }

        var values = arguments.Split(',').Select(value => int.Parse(value.Trim())).ToArray();
        return (values[0], values.Length == 1 ? 10 : values[1]);
    }

    private void DrawOutput()
    {
        m_visibleListingRows.Clear();
        var row = 0;
        foreach (var line in m_outputLines.Skip(m_outputLineOffset))
        {
            if (row >= SpectrumScreen.Rows - 2)
                break;

            var displayLine = MarkSelectedListingLine(line);
            var availableCharacters = (SpectrumScreen.Rows - 2 - row) * SpectrumScreen.Columns;
            var visibleLine = displayLine[..Math.Min(displayLine.Length, availableCharacters)];
            m_screen.DrawText(0, row, visibleLine, m_font);
            var requiredRows = Math.Max(1, (visibleLine.Length + SpectrumScreen.Columns - 1) / SpectrumScreen.Columns);
            for (var visibleRow = row; visibleRow < row + requiredRows; visibleRow++)
            {
                m_visibleListingRows[visibleRow] = displayLine;
            }
            row += requiredRows;
        }
    }

    private void SetOutputLines(IReadOnlyList<string> lines)
    {
        m_outputLines = lines;
        m_outputLineOffset = 0;
        m_scrollWheelRemainder = 0;
    }

    private void ShowLoadedProgram()
    {
        m_hasStarted = true;
        m_hasError = false;
        m_preserveProgramScreen = false;
        m_isPaused = false;
        m_input = string.Empty;
        m_cursor = 0;
        m_selectedLineNumber = null;
        SetOutputLines(m_program.GetListing());
        ShowCursor();
    }

    private void ShowListingResult(BasicListingResult result)
    {
        ShowLoadedProgram();
        if (result.IsValid)
        {
            return;
        }

        m_input = result.InvalidLine!;
        m_cursor = m_input.Length;
        m_hasError = true;
        m_selectedLineNumber = result.LastLineNumber;
        ShowCursor();
    }

    private string MarkSelectedListingLine(string line)
    {
        var digitCount = line.TakeWhile(char.IsDigit).Count();
        if (digitCount == 0 || !int.TryParse(line[..digitCount], out var lineNumber))
        {
            return line;
        }

        var source = m_program.GetSourceLine(lineNumber);
        if (source == null)
        {
            return line;
        }

        if (lineNumber != m_selectedLineNumber)
        {
            return source;
        }

        return $"{source[..digitCount]}>{source[digitCount..].TrimStart()}";
    }

    private void CopyFrame(SpectrumScreen screen)
    {
        var pixels = MemoryMarshal.Cast<int, uint>(m_framePixels.AsSpan());
        screen.Render(pixels, m_flashPhase);
        using (var buffer = m_frame.Lock())
            Marshal.Copy(m_framePixels, 0, buffer.Address, m_framePixels.Length);

        if (m_isCrtEnabled)
        {
            var crtPixels = MemoryMarshal.Cast<int, uint>(m_crtPixels.AsSpan());
            m_crtRenderer.Render(pixels, crtPixels);
            using var crtBuffer = m_crtFrame.Lock();
            Marshal.Copy(m_crtPixels, 0, crtBuffer.Address, m_crtPixels.Length);
        }

        InvalidateVisual();
        FrameRefreshed?.Invoke(this, EventArgs.Empty);
    }

    private void DrawEditor(SpectrumScreen screen)
    {
        var paper = screen.BorderColor;
        var ink = paper < 4 ? (byte)7 : (byte)0;
        screen.ClearTextRow(SpectrumScreen.Rows - 2, paper);
        screen.ClearTextRow(SpectrumScreen.Rows - 1, paper);
        const int editorCharacters = SpectrumScreen.Columns - 1;
        var editorText = m_inputPrompt + m_input;
        var editorCursor = m_inputPrompt.Length + m_cursor;
        var viewportStart = Math.Max(0, editorCursor - editorCharacters + 1);
        var visibleInput = editorText[viewportStart..];
        if (visibleInput.Length > editorCharacters)
            visibleInput = visibleInput[..editorCharacters];

        screen.DrawText(0, SpectrumScreen.Rows - 1, visibleInput, m_font, ink, paper);
        if (!m_cursorVisible)
            return;

        var cursorPosition = editorCursor - viewportStart;
        var cursorCharacter = m_hasError ? 'E' : GetCursorCharacter();
        screen.DrawGlyph(cursorPosition, SpectrumScreen.Rows - 1, m_font[cursorCharacter], ink, paper);
    }

    private char GetCursorCharacter()
    {
        var statementStart = m_input.TakeWhile(character => char.IsDigit(character) || char.IsWhiteSpace(character)).Count();
        return m_cursor <= statementStart ? 'K' : 'L';
    }
}
