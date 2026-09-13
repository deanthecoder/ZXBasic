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
using Avalonia.Platform.Storage;
using DTC.Core.Extensions;
using DTC.Core.UI;
using Material.Icons;
using ZXBasic.Basic;

namespace ZXBasic;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType BasicFiles = new("BASIC listings") { Patterns = ["*.bas"] };
    private static readonly FilePickerFileType SnapshotFiles = new("Spectrum snapshots") { Patterns = ["*.sna"] };
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
        Closed += (_, _) => Terminal.DisposeJoystickInput();
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
        Terminal.IsCrtEnabled = m_isCrtEnabled;
        CrtOverlay.IsVisible = m_isCrtEnabled;
        AmbientDisplay.IsVisible = m_isCrtEnabled;
        CrtCheckIcon.IsVisible = m_isCrtEnabled;
        Terminal.Focus();
    }

    private async void OpenClicked(object? sender, RoutedEventArgs e)
    {
        await OpenFileAsync();
    }

    private async void OpenFile()
    {
        await OpenFileAsync();
    }

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open BASIC Program",
            AllowMultiple = false,
            FileTypeFilter = [BasicFiles, SnapshotFiles]
        });
        if (files.Count == 1)
        {
            await LoadFileAsync(files[0]);
        }

        Terminal.Focus();
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e)
    {
        await SaveFileAsync();
    }

    private async void SaveFile()
    {
        await SaveFileAsync();
    }

    private async Task SaveFileAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save BASIC Program",
            SuggestedFileName = "program.bas",
            DefaultExtension = "bas",
            FileTypeChoices = [BasicFiles]
        });
        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(Terminal.GetListingText());
        }

        Terminal.Focus();
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

    private async Task LoadFileAsync(IStorageFile file)
    {
        await Terminal.StopExecutionAsync();
        await using var stream = await file.OpenReadAsync();
        if (Path.GetExtension(file.Name).Equals(".sna", StringComparison.OrdinalIgnoreCase))
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
            Icon = Terminal.Frame
        })
        {
            ShowInTaskbar = false
        };
        await dialog.ShowDialog(this);
        Terminal.Focus();
    }
}
