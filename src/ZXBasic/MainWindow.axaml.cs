// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.UI;
using Material.Icons;
using ZXBasic.Basic;
using ZXBasic.Controls;

namespace ZXBasic;

public partial class MainWindow : Window
{
    private BasicExecutionSpeed m_executionSpeed = BasicExecutionSpeed.Spectrum;
    private bool m_isCrtEnabled = true;

    public MainWindow()
    {
        InitializeComponent();
        Terminal.FrameRefreshed += (_, _) =>
        {
            CrtOverlay.InvalidateVisual();
            AmbientDisplay.InvalidateVisual();
        };
        AddHandler(DragDrop.DropEvent, FileDropped);
        Closed += (_, _) => Terminal.DisposeDevices();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && Terminal.StopExecution())
        {
            e.Handled = true;
            return;
        }

        var commandModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(commandModifier))
        {
            return;
        }

        if (e.Key == Key.O)
        {
            OpenFile();
            e.Handled = true;
        }
        else if (e.Key == Key.S)
        {
            SaveFile();
            e.Handled = true;
        }
    }

    private void RotateSpeedClicked(object? sender, RoutedEventArgs e)
    {
        m_executionSpeed = m_executionSpeed switch
        {
            BasicExecutionSpeed.Spectrum => BasicExecutionSpeed.Fast,
            BasicExecutionSpeed.Fast => BasicExecutionSpeed.Unlimited,
            _ => BasicExecutionSpeed.Spectrum
        };
        SetExecutionSpeed(m_executionSpeed);
    }

    private void SetExecutionSpeed(BasicExecutionSpeed speed)
    {
        Terminal.ExecutionSpeed = speed;
        SpeedIcon.Kind = speed switch
        {
            BasicExecutionSpeed.Spectrum => MaterialIconKind.Walk,
            BasicExecutionSpeed.Fast => MaterialIconKind.Run,
            _ => MaterialIconKind.RunFast
        };
        ToolTip.SetTip(SpeedButton, $"Execution speed: {speed}");
        Terminal.Focus();
    }

    private void ToggleCrtClicked(object? sender, RoutedEventArgs e)
    {
        m_isCrtEnabled = !m_isCrtEnabled;
        RenderOptions.SetBitmapInterpolationMode(
            DisplayViewbox,
            SpectrumTerminal.GetDisplayInterpolationMode(m_isCrtEnabled));
        Terminal.IsCrtEnabled = m_isCrtEnabled;
        CrtOverlay.IsVisible = m_isCrtEnabled;
        AmbientDisplay.IsVisible = m_isCrtEnabled;
        CrtCheckIcon.IsVisible = m_isCrtEnabled;
        DisplayViewbox.InvalidateVisual();
        Terminal.Focus();
    }

    private void KeyboardIconPointerEntered(object? sender, PointerEventArgs e)
    {
        Keyboard.IsVisible = true;
        Keyboard.Opacity = 1;
    }

    private void KeyboardIconPointerExited(object? sender, PointerEventArgs e)
    {
        Keyboard.Opacity = 0;
    }

    private void OpenClicked(object? sender, RoutedEventArgs e)
    {
        OpenFile();
    }

    private void OpenFile()
    {
        var command = new FileOpenCommand(
            "Open BASIC Program",
            "BASIC programs",
            ["*.bas", "*.sna"]);
        command.FileSelected += async (_, file) =>
        {
            await LoadFileAsync(file);
            Terminal.Focus();
        };
        command.Cancelled += (_, _) => Terminal.Focus();
        command.Execute(this);
    }

    private void SaveClicked(object? sender, RoutedEventArgs e)
    {
        SaveFile();
    }

    private void SaveFile()
    {
        var command = new FileSaveCommand(
            "Save BASIC Program",
            "BASIC listings",
            ["*.bas"],
            "program.bas");
        command.FileSelected += async (_, file) =>
        {
            await using var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(Terminal.GetListingText());
            Terminal.Focus();
        };
        command.Cancelled += (_, _) => Terminal.Focus();
        command.Execute(this);
    }

    private void SaveScreenshotClicked(object? sender, RoutedEventArgs e)
    {
        var command = new FileSaveCommand(
            "Save Screenshot",
            "PNG images",
            ["*.png"],
            "zxbasic-screenshot.png");
        command.FileSelected += (_, file) =>
        {
            using var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None);
            Terminal.SaveScreenshot(stream);
            Terminal.Focus();
        };
        command.Cancelled += (_, _) => Terminal.Focus();
        command.Execute(this);
    }

    private async void FileDropped(object? sender, DragEventArgs e)
    {
        var file = e.Data.GetFiles()?.OfType<IStorageFile>().FirstOrDefault();
        var extension = file == null ? string.Empty : Path.GetExtension(file.Name);
        if (file != null && (extension.Equals(".bas", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".sna", StringComparison.OrdinalIgnoreCase)))
        {
            await LoadFileAsync(file);
        }
    }

    private async Task LoadFileAsync(FileInfo file)
    {
        await Terminal.StopExecutionAsync();
        await using var stream = file.OpenRead();
        await LoadFileAsync(stream, file.Extension);
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        await Terminal.StopExecutionAsync();
        await using var stream = await file.OpenReadAsync();
        await LoadFileAsync(stream, Path.GetExtension(file.Name));
    }

    private async Task LoadFileAsync(Stream stream, string extension)
    {
        if (extension.Equals(".sna", StringComparison.OrdinalIgnoreCase))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            Terminal.TryLoadSnapshot(memory.ToArray());
        }
        else
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            Terminal.TryLoadListing(await reader.ReadToEndAsync());
        }
        Terminal.Focus();
    }

    private async void ResetMachineClicked(object? sender, RoutedEventArgs e)
    {
        await Terminal.StopExecutionAsync();
        Terminal.ResetMachine();
        Terminal.Focus();
    }

    private void ExitClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenProjectPageClicked(object? sender, RoutedEventArgs e)
    {
        new Uri("https://github.com/deanthecoder/ZXBasic").Open();
        Terminal.Focus();
    }

    private async void AboutClicked(object? sender, RoutedEventArgs e)
    {
        var assembly = Assembly.GetEntryAssembly();
        var dialog = new AboutDialog(new AboutInfo
        {
            Title = "DeanTheCoder ZXBasic",
            Version = assembly?.GetName().Version?.ToString(3) ?? "0.1",
            Copyright = "Copyright (c) 2026 Dean Edis",
            WebsiteUrl = "https://github.com/deanthecoder/ZXBasic",
            Icon = new Bitmap(AssetLoader.Open(new Uri("avares://ZXBasic/App.ico")))
        })
        {
            Icon = Icon,
            ShowInTaskbar = false
        };
        await dialog.ShowDialog(this);
        Terminal.Focus();
    }
}
