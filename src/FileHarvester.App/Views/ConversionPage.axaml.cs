// filepath: src/FileHarvester.App/Views/ConversionPage.axaml.cs
// layer:    App View code-behind
// module:   FileHarvester.App.Views
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Code-behind for ConversionPage.axaml. Loads preview bitmaps for
//           the Original and Converted cards when the ConversionViewModel's
//           InputPath/OutputPath change. All state lives in the ViewModel.
// RELATIONSHIPS
//   Depends on : Avalonia UserControl, Avalonia.Media.Imaging.Bitmap,
//                FileHarvester.App.ViewModels.MainWindowViewModel,
//                FileHarvester.App.ViewModels.ConversionViewModel
//   Used by    : MainWindowViewModel.CurrentPage navigation
//   See also   : FileHarvester.App.Views.ConversionPage.axaml
// NOTES
//   - The ViewModel stays Avalonia-free; only this code-behind touches
//     Bitmap. Decode failures (e.g. an ICO variant the platform decoder
//     rejects) leave the card blank rather than crashing the page.

using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>
/// Code-behind for <see cref="ConversionPage"/>. Loads preview bitmaps.
/// </summary>
public partial class ConversionPage : UserControl
{
    private ConversionViewModel? _conversion;

    /// <summary>Creates the page and wires preview loading.</summary>
    public ConversionPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_conversion is not null)
        {
            _conversion.PropertyChanged -= OnConversionPropertyChanged;
        }
        _conversion = (DataContext as MainWindowViewModel)?.Conversion;
        if (_conversion is not null)
        {
            _conversion.PropertyChanged += OnConversionPropertyChanged;
        }
        RefreshPreviews();
    }

    private void OnConversionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversionViewModel.InputPath) or nameof(ConversionViewModel.OutputPath))
        {
            RefreshPreviews();
        }
    }

    private void RefreshPreviews()
    {
        InputPreviewImage.Source = LoadBitmap(_conversion?.InputPath);
        OutputPreviewImage.Source = LoadBitmap(_conversion?.OutputPath);
    }

    private static Bitmap? LoadBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}
