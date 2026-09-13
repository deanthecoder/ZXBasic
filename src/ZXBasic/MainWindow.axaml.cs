// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
// 
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
// 
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Material.Icons;
using ZXBasic.Basic;

namespace ZXBasic;

public partial class MainWindow : Window
{
    private BasicExecutionSpeed m_executionSpeed = BasicExecutionSpeed.Spectrum;
    private bool m_isCrtEnabled = true;

    public MainWindow()
    {
        InitializeComponent();
        Terminal.FrameRefreshed += (_, _) => CrtOverlay.InvalidateVisual();
        AddHandler(DragDrop.DropEvent, SnapshotDropped);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && Terminal.StopExecution())
        {
            e.Handled = true;
            return;
        }

        var openModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.O && e.KeyModifiers.HasFlag(openModifier))
        {
            OpenSnapshot();
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
        CrtCheckIcon.IsVisible = m_isCrtEnabled;
        Terminal.Focus();
    }

    private async void OpenSnapshotClicked(object? sender, RoutedEventArgs e)
    {
        await OpenSnapshotAsync();
    }

    private async void OpenSnapshot()
    {
        await OpenSnapshotAsync();
    }

    private async Task OpenSnapshotAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Spectrum Snapshot",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Spectrum snapshots") { Patterns = ["*.sna"] }
            ]
        });
        if (files.Count == 1)
        {
            await LoadSnapshotAsync(files[0]);
        }

        Terminal.Focus();
    }

    private async void SnapshotDropped(object? sender, DragEventArgs e)
    {
        var file = e.Data.GetFiles()?.OfType<IStorageFile>().FirstOrDefault();
        if (file != null && string.Equals(Path.GetExtension(file.Name), ".sna", StringComparison.OrdinalIgnoreCase))
        {
            await LoadSnapshotAsync(file);
        }
    }

    private async Task LoadSnapshotAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Terminal.TryLoadSnapshot(memory.ToArray());
        Terminal.Focus();
    }
}
