// ============================================================================
// FILE:     src/FileHarvester.App/Views/HarvestResultDialog.axaml.cs
// PURPOSE:  Code-behind for the harvest result modal. Wires Open Folder
//           and Close buttons.
// RELATION: Bound to Views/HarvestResultDialog.axaml.
// ============================================================================

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class HarvestResultDialog : Window
{
    public HarvestResultDialog()
    {
        InitializeComponent();
        WireButtons();
    }

    private HarvestResultDialogViewModel? ViewModel =>
        DataContext as HarvestResultDialogViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        var openFolder = this.FindControl<Button>("OpenFolderButton");
        if (openFolder is not null) openFolder.Click += OnOpenFolderClicked;

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null) close.Click += (_, _) => Close();
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folder = ViewModel?.Result.DestinationFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception) { /* best-effort */ }
    }
}
