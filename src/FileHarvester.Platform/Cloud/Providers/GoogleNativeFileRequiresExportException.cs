// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GoogleNativeFileRequiresExportException.cs
// PURPOSE:  Thrown by GoogleDriveProvider.OpenReadAsync when the requested
//           file is a Google-native document (Doc, Sheet, Slide, Drawing)
//           that has no downloadable binary content and must be exported
//           to a concrete MIME type instead. The exception carries the
//           file id. The caller resolves an IGDriveExportClient and calls
//           ExportAsync with an explicit export MIME type; the provider
//           never silently substitutes a default.
// RELATION: Thrown by GoogleDriveProvider. Caught by the caller that
//           decides the export format (C.8 UI). The exception does not
//           carry an export-format list: the Drive metadata does not
//           provide a complete one, and the design does not fabricate
//           a list it has not verified.
// ============================================================================

using System;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// A Google-native file cannot be downloaded as bytes; it must be exported
/// to a concrete MIME type.
/// </summary>
/// <remarks>
/// <para>
/// The message names the file id and nothing else. It does not name a
/// default export format: the caller chooses the format, and a silent
/// default would be a defect the design explicitly forbids.
/// </para>
/// </remarks>
public sealed class GoogleNativeFileRequiresExportException : Exception
{
    /// <summary>
    /// Creates the exception for the given file id.
    /// </summary>
    /// <param name="fileId">
    /// The Drive file id. Never null or empty.
    /// </param>
    public GoogleNativeFileRequiresExportException(string fileId)
        : base("The file is a Google-native document and must be exported to a concrete MIME type. File id: " + fileId)
    {
        FileId = fileId;
    }

    /// <summary>The Drive file id that requires export.</summary>
    public string FileId { get; }
}
