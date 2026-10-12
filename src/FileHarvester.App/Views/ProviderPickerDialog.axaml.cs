// ============================================================================
// FILE:     src/FileHarvester.App/Views/ProviderPickerDialog.axaml.cs
// PURPOSE:  Code-behind for the static-provider picker. The user selects
//           one of the built-in static-credential providers. The dialog
//           returns the chosen ProviderProfile, or null on Cancel or
//           window close. The picker accepts no credential and performs
//           no I/O.
// RELATION: Bound to Views/ProviderPickerDialog.axaml. Opened modally by
//           CloudPage.axaml.cs from the "Use a Credential" action.
// ============================================================================

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.Views;

public partial class ProviderPickerDialog : Window
{
    /// <summary>
    /// Design-time and XAML-loader constructor. This constructor is never
    /// used at runtime for the "Use a Credential" flow; it exists so the
    /// XAML asset is reachable via the runtime loader.
    /// </summary>
    public ProviderPickerDialog()
    {
        InitializeComponent();
        WireButtons();
    }

    /// <summary>
    /// Creates the picker over the supplied static profiles.
    /// </summary>
    /// <param name="staticProfiles">
    /// The static-credential provider profiles to choose from. Not null;
    /// may be empty.
    /// </param>
    public ProviderPickerDialog(IReadOnlyList<ProviderProfile> staticProfiles)
        : this()
    {
        DataContext = staticProfiles ?? throw new ArgumentNullException(nameof(staticProfiles));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        Wire("CancelButton", (_, _) => Close(null));
    }

    private void Wire(string name, EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
        {
            button.Click += handler;
        }
    }

    private void OnSelectClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.DataContext is ProviderProfile profile)
        {
            Close(profile);
        }
    }
}
