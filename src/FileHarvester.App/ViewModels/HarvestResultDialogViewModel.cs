// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/HarvestResultDialogViewModel.cs
// PURPOSE:  View model for the harvest result dialog. Formats the SaveResult
//           into display strings.
// RELATION: Consumed by Views/HarvestResultDialog.axaml.
// ============================================================================

using System.Globalization;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class HarvestResultDialogViewModel : ViewModelBase
{
    public HarvestResultDialogViewModel(SaveResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public SaveResult Result { get; }

    public bool IsSuccess => Result.Success;

    public string Headline => Result.Success
        ? "Harvest complete"
        : "Harvest failed";

    public string SubText => Result.Success
        ? "Your files were bundled successfully."
        : "The harvest did not complete. See details below.";

    public string DestinationFolder => Result.DestinationFolder;

    public string OutputPath => Result.OutputPath ?? "(none)";

    public string FileCountText => Result.FileCount.ToString(CultureInfo.InvariantCulture);

    public string BytesWrittenText =>
        FormatBytes(Result.BytesWritten);

    public string ErrorMessage => Result.ErrorMessage ?? "Unknown error.";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
