using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using WarpinatorWinClient.App.Interop;
using WarpinatorWinClient.App.ViewModels;

namespace WarpinatorWinClient.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly WarpinatorRuntime runtime;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = MainViewModel.CreateDesignState();
        runtime = ((App)Application.Current).Runtime;
        runtime.SnapshotChanged += RuntimeSnapshotChanged;
        DataContext = viewModel;
        Loaded += MainWindowLoaded;
        Closed += MainWindowClosed;
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        await Task.Run(() => runtime.StartAsync(CancellationToken.None));
    }

    private async void SearchPeersClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => runtime.SearchPeersAsync());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Search failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SendFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = true,
            Title = "Choose files to send to Linux"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await SendFilesAsync(dialog.FileNames);
    }

    private void SendStageDragEnter(object sender, DragEventArgs e)
    {
        UpdateDropTargetState(e);
    }

    private void SendStageDragOver(object sender, DragEventArgs e)
    {
        UpdateDropTargetState(e);
    }

    private void SendStageDragLeave(object sender, DragEventArgs e)
    {
        viewModel.SetDropTargetActive(false);
    }

    private async void SendStageDrop(object sender, DragEventArgs e)
    {
        viewModel.SetDropTargetActive(false);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var items = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>();
        await SendFilesAsync(items.Where(File.Exists));
    }

    private void RevealTransferClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string revealPath } || string.IsNullOrWhiteSpace(revealPath))
        {
            return;
        }

        try
        {
            RevealInExplorer(revealPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Reveal failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RuntimeSnapshotChanged(object? sender, WarpinatorRuntimeSnapshot snapshot)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.InvokeAsync(() => viewModel.ApplyRuntimeSnapshot(snapshot));
        }
    }

    private void MainWindowClosed(object? sender, EventArgs e)
    {
        runtime.SnapshotChanged -= RuntimeSnapshotChanged;
    }

    private void UpdateDropTargetState(DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop)
            && ((string[]?)e.Data.GetData(DataFormats.FileDrop))?.Any(File.Exists) == true;

        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        viewModel.SetDropTargetActive(hasFiles);
    }

    private async Task SendFilesAsync(IEnumerable<string> selectedPaths)
    {
        var files = selectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToArray();

        if (files.Length == 0)
        {
            MessageBox.Show(
                this,
                "Choose one or more files. Folder drag and drop will come in a later pass.",
                "Nothing to send",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            foreach (var file in files)
            {
                await runtime.SendFileToConnectedPeerAsync(file);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Send failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void RevealInExplorer(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            });
            return;
        }

        if (Directory.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{fullPath}\"",
                UseShellExecute = true
            });
            return;
        }

        throw new FileNotFoundException("The received item is no longer available on disk.", fullPath);
    }
}
