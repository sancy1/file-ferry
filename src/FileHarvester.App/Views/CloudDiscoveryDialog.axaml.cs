// ============================================================================
// FILE:     src/FileHarvester.App/Views/CloudDiscoveryDialog.axaml.cs
// PURPOSE:  Code-behind for the provider discovery dialog. The user types
//           a name and clicks Find. The dialog calls ICloudNameResolver
//           (the local catalog only) and shows the candidates. Selecting a
//           candidate closes the dialog and returns it. Cancel closes with
//           null. The dialog accepts no credential.
// RELATION: Opened by CloudPage.axaml.cs. Bound by CloudDiscoveryDialog.axaml.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FileHarvester.App.ViewModels;
using FileHarvester.Platform.Cloud.Discovery;

namespace FileHarvester.App.Views;

public partial class CloudDiscoveryDialog : Window
{
    private ICloudNameResolver? _resolver;
    private bool _busy;

    /// <summary>
    /// Design-time and XAML-loader constructor. Required so the compiled
    /// XAML asset is reachable via the runtime loader. This constructor is
    /// never used at runtime: the dialog is always constructed with its
    /// resolver via the parameterized constructor.
    /// </summary>
    public CloudDiscoveryDialog()
    {
        InitializeComponent();

        var findButton = this.FindControl<Button>("FindButton");
        var searchBox = this.FindControl<TextBox>("SearchBox");

        if (searchBox is not null)
        {
            searchBox.TextChanged += (_, _) => UpdateFindEnabled();
        }

        if (findButton is not null)
        {
            findButton.Click += async (_, _) => await FindAsync();
        }

        UpdateFindEnabled();
    }

    /// <summary>
    /// Creates the dialog over the name resolver.
    /// </summary>
    /// <param name="resolver">The resolver. Not null.</param>
    public CloudDiscoveryDialog(ICloudNameResolver resolver)
        : this()
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    // ------------------------------------------------------------------
    // Find.
    // ------------------------------------------------------------------

    private async Task FindAsync()
    {
        if (_busy) return;

        var searchBox = this.FindControl<TextBox>("SearchBox");
        var name = searchBox?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            // Empty input: no resolver call. Find is disabled, so this is a
            // defensive guard.
            return;
        }

        SetBusy(true);

        IReadOnlyList<CloudNameCandidate> candidates;
        try
        {
            if (_resolver is null)
            {
                throw new InvalidOperationException(
                    "CloudDiscoveryDialog requires a resolver.");
            }

            candidates = await _resolver.ResolveAsync(name, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            candidates = Array.Empty<CloudNameCandidate>();
        }

        PopulateResults(candidates);
        SetBusy(false);
    }

    private void PopulateResults(IReadOnlyList<CloudNameCandidate> candidates)
    {
        var resultsList = this.FindControl<ItemsControl>("ResultsList");
        var statusText = this.FindControl<TextBlock>("StatusText");

        if (resultsList is not null)
        {
            var rows = new List<CloudNameCandidateRowViewModel>(candidates.Count);
            foreach (var candidate in candidates)
            {
                rows.Add(new CloudNameCandidateRowViewModel(candidate));
            }
            resultsList.ItemsSource = rows;
        }

        if (statusText is not null)
        {
            if (candidates.Count == 0)
            {
                statusText.Text = "No known provider found. You can define a custom provider in the next step.";
                statusText.IsVisible = true;
            }
            else
            {
                statusText.IsVisible = false;
            }
        }
    }

    // ------------------------------------------------------------------
    // State helpers.
    // ------------------------------------------------------------------

    private void UpdateFindEnabled()
    {
        if (_busy) return;

        var searchBox = this.FindControl<TextBox>("SearchBox");
        var findButton = this.FindControl<Button>("FindButton");

        var hasText = !string.IsNullOrWhiteSpace(searchBox?.Text);
        if (findButton is not null)
        {
            findButton.IsEnabled = hasText;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        var findButton = this.FindControl<Button>("FindButton");
        if (findButton is not null)
        {
            findButton.IsEnabled = !busy;
        }

        var resultsList = this.FindControl<ItemsControl>("ResultsList");
        if (resultsList is not null)
        {
            resultsList.IsEnabled = !busy;
        }
    }

    // ------------------------------------------------------------------
    // Selection and cancel.
    // ------------------------------------------------------------------

    private void OnSelectClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is CloudNameCandidateRowViewModel row)
        {
            Close(row.Candidate);
        }
    }
}
