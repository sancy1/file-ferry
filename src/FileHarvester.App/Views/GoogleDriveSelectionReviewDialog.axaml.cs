using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FileHarvester.App.Views;

public enum GoogleDriveSelectionReviewChoice
{
    Cancel,
    SaveSeparately,
    SaveAsZip,
}

public partial class GoogleDriveSelectionReviewDialog : Window
{
    public GoogleDriveSelectionReviewDialog()
    {
        InitializeComponent();
        Wire("CancelButton", (_, _) => Close(GoogleDriveSelectionReviewChoice.Cancel));
        Wire("SeparateButton", (_, _) => Close(GoogleDriveSelectionReviewChoice.SaveSeparately));
        Wire("ZipButton", (_, _) => Close(GoogleDriveSelectionReviewChoice.SaveAsZip));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Wire(string name, EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
        {
            button.Click += handler;
        }
    }
}
