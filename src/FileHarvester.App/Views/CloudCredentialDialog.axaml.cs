// ============================================================================
// FILE:     src/FileHarvester.App/Views/CloudCredentialDialog.axaml.cs
// PURPOSE:  Code-behind for the dynamic credential form. Owns the modal
//           close decision. The view model owns validation and result
//           construction; this file observes LastSaveSucceeded and closes
//           with the ephemeral PendingCredentialSubmission. No persistence,
//           no network, no resilience policy.
// RELATION: Bound to Views/CloudCredentialDialog.axaml. Opened modally by
//           CloudPage.axaml.cs on the Configure action for static providers.
// ============================================================================

using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

public partial class CloudCredentialDialog : Window
{
    private static readonly string[] AllFilesPatterns = { "*" };

    private static readonly FilePickerFileType[] AllFilesFilter =
    {
        new FilePickerFileType("All files") { Patterns = AllFilesPatterns },
    };

    private CloudCredentialFormViewModel? _observedViewModel;

    /// <summary>
    /// Design-time and XAML-loader constructor. The dialog is always
    /// constructed for runtime use by CloudPage.axaml.cs; this constructor
    /// exists so the XAML asset is reachable via the runtime loader.
    /// </summary>
    public CloudCredentialDialog()
    {
        InitializeComponent();
        WireButtons();
    }

    private CloudCredentialFormViewModel? ViewModel =>
        DataContext as CloudCredentialFormViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _observedViewModel = null;
        }

        if (ViewModel is null)
        {
            return;
        }

        _observedViewModel = ViewModel;
        _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CloudCredentialFormViewModel.LastSaveSucceeded))
        {
            return;
        }

        if (ViewModel is null || !ViewModel.LastSaveSucceeded)
        {
            return;
        }

        var result = ViewModel.BuildResult();
        ViewModel.Clear();
        Close(result);
    }

    private void WireButtons()
    {
        Wire("CancelButton", (_, _) =>
        {
            ViewModel?.Clear();
            Close(null);
        });
    }

    private void Wire(string name, EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
        {
            button.Click += handler;
        }
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select the credential file",
                AllowMultiple = false,
                FileTypeFilter = AllFilesFilter
            });

        if (files.Count == 0)
        {
            return;
        }

        if (sender is Button button &&
            button.DataContext is CredentialFieldViewModel field)
        {
            field.Value = files[0].Path.LocalPath;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ViewModel?.Clear();
        base.OnClosing(e);
    }
}
