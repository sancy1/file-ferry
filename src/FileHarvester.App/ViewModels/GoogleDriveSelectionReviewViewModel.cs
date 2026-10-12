using System.Collections.Generic;
using System.Linq;
using FileHarvester.Platform.Cloud.Services;

namespace FileHarvester.App.ViewModels;

public sealed class GoogleDriveSelectionReviewViewModel
{
    public GoogleDriveSelectionReviewViewModel(IReadOnlyList<GoogleDrivePickerFile> files)
    {
        Files = files;
        DownloadableFileCount = files.Count(file =>
            string.IsNullOrWhiteSpace(file.Error)
            && !string.IsNullOrWhiteSpace(file.DownloadHandle));
    }

    public IReadOnlyList<GoogleDrivePickerFile> Files { get; }

    public int DownloadableFileCount { get; }

    public bool CanDownloadAsZip => DownloadableFileCount > 1;
}
