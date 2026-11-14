// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ImageTextifyFileRowViewModel.cs
// PURPOSE:  View model for one row in the ImageTextify intake queue.
//           Carries the display name, the redacted path, the category,
//           the human-readable status, the validation reason, and a
//           per-row remove command.
// RELATION: Created by ImageTextifyViewModel.AddFilesAsync. Bound by
//           Views/ImageTextifyPage.axaml through MainWindowViewModel.
//           The row never reads the file system; it holds only what the
//           intake decision reported.
// ============================================================================

using System.Windows.Input;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the ImageTextify intake queue.
/// </summary>
/// <remarks>
/// <para>
/// Every row, including a rejected or duplicate row, is visible. No
/// added path may disappear without explanation. The row's
/// <see cref="StatusText"/> is the human-readable reason; the
/// <see cref="Reason"/> is the machine-readable counterpart.
/// </para>
/// <para>
/// <see cref="RedactedPath"/> is safe for logging. <see cref="FullPath"/>
/// is not, and is not exposed as a public property - the row keeps it
/// private and the diagnostic representation uses the redacted form.
/// </para>
/// </remarks>
public sealed class ImageTextifyFileRowViewModel : ViewModelBase
{
    private readonly string _fullPath;
    private readonly long _sizeBytes;

    /// <summary>
    /// Creates a row from a validation result and an intake reason.
    /// </summary>
    /// <param name="validation">The validation result. Not null.</param>
    /// <param name="reason">The intake reason. Not null.</param>
    /// <param name="category">The category the row was validated for.</param>
    /// <param name="removeCommand">
    /// The per-row remove command, supplied by the owning view model.
    /// Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="validation"/>, <paramref name="reason"/>,
    /// or <paramref name="removeCommand"/> is null.
    /// </exception>
    public ImageTextifyFileRowViewModel(
        ImageTextifyValidationResult validation,
        ImageTextifyIntakeReason reason,
        ImageTextifyCategory category,
        ICommand removeCommand)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(removeCommand);

        _fullPath = validation.FullPath;
        _sizeBytes = validation.SizeBytes;
        DisplayName = validation.DisplayName;
        RedactedPath = validation.RedactedPath;
        Category = category;
        Reason = reason;
        ValidationReason = validation.Reason;
        RemoveCommand = removeCommand;
        StatusText = BuildStatusText(reason, validation.Reason);
    }

    /// <summary>The file name without directory components. Safe to display.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// The full path with the user segment redacted. Safe for logs and
    /// diagnostics.
    /// </summary>
    public string RedactedPath { get; }

    /// <summary>The category the row was validated for.</summary>
    public ImageTextifyCategory Category { get; }

    /// <summary>The intake-level reason for this row's presence.</summary>
    public ImageTextifyIntakeReason Reason { get; }

    /// <summary>The validation-level reason.</summary>
    public ImageTextifyValidationReason ValidationReason { get; }

    /// <summary>
    /// A human-readable status line. Every rejection has a reason; no
    /// row is silently discarded.
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// True when the row is a valid accepted intake. False for a
    /// duplicate or a rejected file. Used by the row template to pick
    /// the status pill class.
    /// </summary>
    public bool IsAccepted => Reason == ImageTextifyIntakeReason.Accepted;

    /// <summary>
    /// The full path as the row received it. Used by the owning
    /// view model to compare against the current queue for duplicate
    /// detection. Internal only; never logged, never bound, never
    /// surfaced to the UI.
    /// </summary>
    internal string FullPathForComparison => _fullPath;

    /// <summary>
    /// The full path to open for extraction. Distinct from
    /// <see cref="FullPathForComparison"/>: this property serves the
    /// owning view model's extraction command, which opens the file;
    /// the comparison property serves duplicate detection. Neither is
    /// bound to the UI, logged, or redacted; the redacted form is
    /// <see cref="RedactedPath"/>.
    /// </summary>
    public string FullPathForExtraction => _fullPath;

    /// <summary>
    /// The file size as a short human-readable string. Zero bytes
    /// renders as "0 B". Uses one decimal place for KB, MB, and GB.
    /// </summary>
    public string SizeText => FormatSize(_sizeBytes);

    /// <summary>
    /// The parent directory of the source file. Literal, not redacted,
    /// because it is user-visible display, not a log line. Empty when
    /// the path has no directory component.
    /// </summary>
    public string DirectoryText =>
        Path.GetDirectoryName(_fullPath) ?? string.Empty;

    /// <summary>The per-row remove command, supplied by the owning view model.</summary>
    public ICommand RemoveCommand { get; }

    private static string BuildStatusText(
        ImageTextifyIntakeReason reason,
        ImageTextifyValidationReason validationReason) =>
        reason switch
        {
            ImageTextifyIntakeReason.Accepted  => "Ready",
            ImageTextifyIntakeReason.Duplicate => "Already in queue",
            ImageTextifyIntakeReason.Rejected  => validationReason switch
            {
                ImageTextifyValidationReason.MissingFile          => "File not found",
                ImageTextifyValidationReason.UnsupportedExtension => "Unsupported extension for this category",
                ImageTextifyValidationReason.Unavailable          => "Validation unavailable",
                ImageTextifyValidationReason.Cancelled            => "Cancelled",
                ImageTextifyValidationReason.NotYetSupported      => "Not yet supported",
                _                                                 => "Rejected",
            },
            _ => "Unknown",
        };
    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    }

}
