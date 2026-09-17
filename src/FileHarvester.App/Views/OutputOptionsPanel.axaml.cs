// ============================================================================
// FILE:     src/FileHarvester.App/Views/OutputOptionsPanel.axaml.cs
// PURPOSE:  Code-behind for the Output Options panel. Wires the Harvest
//           button to the Harvest command and the Browse button to the
//           native folder picker.
// RELATION: Bound to Views/OutputOptionsPanel.axaml.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class OutputOptionsPanel : UserControl
{
    public OutputOptionsPanel()
    {
        InitializeComponent();
        WireButtons();
    }

    private OutputOptionsViewModel? ViewModel =>
        DataContext as OutputOptionsViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        var harvest = this.FindControl<Button>("HarvestButton");
        if (harvest is not null)
        {
            harvest.Click += (_, _) => ViewModel?.HarvestCommand.Execute(null);
        }

        var browse = this.FindControl<Button>("BrowseDestinationButton");
        if (browse is not null)
        {
            browse.Click += OnBrowseDestinationClicked;
        }
    }

    private async void OnBrowseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || ViewModel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose destination folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            ViewModel.DestinationPath = path;
            ViewModel.StatusText = $"Destination: {path}";
        }
    }
}
