// ============================================================================
// FILE:     src/FileHarvester.App/Views/CloudPage.axaml.cs
// PURPOSE:  Code-behind for CloudPage. Opens the provider discovery dialog
//           modally when Find a provider is clicked. Subscribes to the
//           view model's ConfigureRequested event and opens the dynamic
//           credential form for the requesting profile. On a successful
//           form submission, persists the credential through
//           ICredentialStore under the CredentialStoreWrite policy.
// RELATION: Bound in CloudPage.axaml. Resolves ICloudNameResolver,
//           ICredentialStore, and IResilienceExecutor from App.Services.
//           The ConfigureRequested handler is on MainWindowViewModel.Cloud
//           (CloudPageViewModel).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using FileHarvester.App.ViewModels;
using Avalonia.Platform.Storage;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Discovery;
using FileHarvester.Platform.Cloud.Services;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace FileHarvester.App.Views;

public partial class CloudPage : UserControl
{
    private CloudPageViewModel? _observedCloudViewModel;

    public CloudPage()
    {
        InitializeComponent();

        var findButton = this.FindControl<Button>("FindProviderButton");
        if (findButton is not null)
        {
            findButton.Click += OnFindProviderClicked;
        }

        var useCredentialButton = this.FindControl<Button>("UseCredentialButton");
        if (useCredentialButton is not null)
        {
            useCredentialButton.Click += OnUseCredentialClicked;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_observedCloudViewModel is not null)
        {
            _observedCloudViewModel.ConfigureRequested -= OnConfigureRequested;
            _observedCloudViewModel.GoogleDriveDownloadRequested -= OnGoogleDriveDownloadRequested;
            _observedCloudViewModel = null;
        }

        if (DataContext is not MainWindowViewModel main)
        {
            return;
        }

        _observedCloudViewModel = main.Cloud;
        _observedCloudViewModel.ConfigureRequested += OnConfigureRequested;
        _observedCloudViewModel.GoogleDriveDownloadRequested += OnGoogleDriveDownloadRequested;
    }

    // ------------------------------------------------------------------
    // Provider discovery dialog.
    // ------------------------------------------------------------------

    private async void OnFindProviderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var resolver = App.Services.GetRequiredService<ICloudNameResolver>();
        var dialog = new CloudDiscoveryDialog(resolver);

        var owner = TopLevel.GetTopLevel(this);

        if (owner is not Window window)
        {
            return;
        }

        var selected = await dialog.ShowDialog<CloudNameCandidate?>(window);

        // Step 3: the selection closes the dialog. It does not connect and
        // it does not store anything. A later step wires the selected
        // candidate to the connection flow.
        _ = selected;
    }

    // ------------------------------------------------------------------
    // Credential form dialog (Step 4/5).
    // ------------------------------------------------------------------

    private async void OnConfigureRequested(object? sender, ProviderProfile profile)
    {
        await OpenCredentialFormAsync(profile).ConfigureAwait(true);
    }

    private async void OnUseCredentialClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_observedCloudViewModel is null)
        {
            return;
        }

        // The picker lists the static-credential profiles only: those that
        // declare credential fields and are not OAuth-connectable.
        var staticProfiles = _observedCloudViewModel.Providers
            .Where(row => !row.IsConnectable && row.Profile.CredentialFields.Count > 0)
            .Select(row => row.Profile)
            .ToList();

        if (staticProfiles.Count == 0)
        {
            return;
        }

        var picker = new ProviderPickerDialog(staticProfiles);

        var owner = TopLevel.GetTopLevel(this);

        if (owner is not Window window)
        {
            return;
        }

        var chosen = await picker.ShowDialog<ProviderProfile?>(window);

        if (chosen is null)
        {
            return;
        }

        await OpenCredentialFormAsync(chosen).ConfigureAwait(true);
    }

    private async Task OpenCredentialFormAsync(ProviderProfile profile)
    {
        var store = App.Services.GetRequiredService<ICredentialStore>();
        var resilience = App.Services.GetRequiredService<IResilienceExecutor>();

        // The uniqueness delegate. The form awaits it during Save. The
        // delegate never throws: a store failure returns true so the form
        // does not block a write that might succeed. The store write
        // itself will fail closed if the store is unreachable.
        Func<string, CancellationToken, Task<bool>> checkUnique = async (name, ct) =>
        {
            try
            {
                var existing = await store.ListInstanceNamesAsync(profile.Id, ct).ConfigureAwait(true);
                return !existing.Contains(name);
            }
            catch (Exception)
            {
                return true;
            }
        };

        var dialog = new CloudCredentialDialog
        {
            DataContext = new CloudCredentialFormViewModel(profile, checkUnique),
        };

        var owner = TopLevel.GetTopLevel(this);

        if (owner is not Window window)
        {
            return;
        }

        var result = await dialog.ShowDialog<PendingCredentialSubmission?>(window)
            .ConfigureAwait(true);

        if (result is null)
        {
            // Cancel or window close. The dialog already cleared its field
            // values. Nothing is written.
            return;
        }

        // OAuth providers store the client secret under the reserved
        // provider-level key as a raw string. The user's instance name is
        // not used for this credential; it applies to the connected account
        // when Connect runs.
        if (profile.AuthKind == AuthKind.OAuth)
        {
            if (result.Values.TryGetValue("clientSecret", out var clientSecret)
                && !string.IsNullOrEmpty(clientSecret))
            {
                var secretKey = new CredentialKey(
                    profile.Id, OAuthClientConfig.ClientSecretInstanceName);
                try
                {
                    await resilience.ExecuteAsync(
                        policyName: PolicyNames.CredentialStoreWrite,
                        operation: async token => await store.WriteAsync(
                            secretKey, clientSecret, token).ConfigureAwait(true),
                        fallback: _ => Task.CompletedTask,
                        ct: CancellationToken.None).ConfigureAwait(true);
                }
                catch (ResilienceException)
                {
                    // Silent failure by design: no secret was stored, the
                    // next Connect reopens the form.
                    return;
                }
            }
            return;
        }

        // Static provider: the values are the credential payload.
        // OAuth providers store the client secret under the reserved
        // provider-level key as a raw string. The user's instance name is
        // not used for this credential; it applies to the connected account
        // when Connect runs.
        if (profile.AuthKind == AuthKind.OAuth)
        {
            if (result.Values.TryGetValue("clientSecret", out var clientSecret)
                && !string.IsNullOrEmpty(clientSecret))
            {
                var secretKey = new CredentialKey(
                    profile.Id, OAuthClientConfig.ClientSecretInstanceName);
                try
                {
                    await resilience.ExecuteAsync(
                        policyName: PolicyNames.CredentialStoreWrite,
                        operation: async token => await store.WriteAsync(
                            secretKey, clientSecret, token).ConfigureAwait(true),
                        fallback: _ => Task.CompletedTask,
                        ct: CancellationToken.None).ConfigureAwait(true);
                }
                catch (ResilienceException)
                {
                    // Silent failure by design: no secret was stored, the
                    // next Connect reopens the form.
                    return;
                }
            }
            return;
        }

        // Static provider: the values are the credential payload.
        var key = new CredentialKey(result.ProfileId, result.InstanceName);
        var payload = JsonSerializer.Serialize(result.Values);

        try
        {
            await resilience.ExecuteAsync(
                policyName: PolicyNames.CredentialStoreWrite,
                operation: async token =>
                {
                    await store.WriteAsync(key, payload, token).ConfigureAwait(true);
                },
                fallback: _ => Task.CompletedTask,
                ct: CancellationToken.None).ConfigureAwait(true);
        }
        catch (ResilienceException)
        {
            // Silent failure by design for Step 5. The row does not update,
            // which is honest: no credential was stored. User-facing error
            // handling for a store failure belongs to a later step.
            return;
        }

        // Success. Refresh the provider list so the new instance appears in
        // the row's Instances collection and the row's StatusText updates.
        if (_observedCloudViewModel is not null)
        {
            await _observedCloudViewModel.LoadAsync().ConfigureAwait(true);
        }
    }

    private async Task OnGoogleDriveDownloadRequested(
        CloudInstanceRowViewModel instance,
        CancellationToken cancellationToken)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var viewModel = _observedCloudViewModel;
        if (topLevel is null || viewModel is null)
        {
            viewModel?.SetDriveDownloadStatus("The Cloud page is not available for the file picker.");
            return;
        }

        var service = App.Services.GetRequiredService<IGoogleDrivePickerDownloadService>();
        var selectedFiles = await service.SelectFilesAsync(
            instance.InstanceName, cancellationToken).ConfigureAwait(true);

        if (selectedFiles.Count == 0)
        {
            viewModel.SetDriveDownloadStatus("Google Picker did not return any files.");
            return;
        }

        var reviewDialog = new GoogleDriveSelectionReviewDialog
        {
            DataContext = new GoogleDriveSelectionReviewViewModel(selectedFiles),
        };
        if (topLevel is not Window ownerWindow)
        {
            service.DiscardDownloadHandles(selectedFiles.Select(file => file.DownloadHandle));
            viewModel.SetDriveDownloadStatus("The file-selection review window is unavailable.");
            return;
        }

        var choice = await reviewDialog.ShowDialog<GoogleDriveSelectionReviewChoice>(ownerWindow)
            .ConfigureAwait(true);
        if (choice == GoogleDriveSelectionReviewChoice.Cancel)
        {
            service.DiscardDownloadHandles(selectedFiles.Select(file => file.DownloadHandle));
            viewModel.SetDriveDownloadStatus("Google Drive download cancelled. No files were saved.");
            return;
        }
        if (choice == GoogleDriveSelectionReviewChoice.SaveAsZip)
        {
            try
            {
                await SaveSelectedFilesAsZipAsync(
                    topLevel,
                    viewModel,
                    service,
                    selectedFiles,
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                service.DiscardDownloadHandles(selectedFiles.Select(file => file.DownloadHandle));
            }
            return;
        }

        var saved = 0;
        var failed = 0;
        var saveCancelled = false;
        try
        {
            foreach (var selected in selectedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(selected.Error))
                {
                    failed++;
                    viewModel.SetDriveDownloadStatus(selected.FileName + ": " + selected.Error);
                    continue;
                }

                var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                    WellKnownFolder.Downloads).ConfigureAwait(true);
                var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Save Google Drive file",
                        SuggestedStartLocation = downloads,
                        SuggestedFileName = selected.FileName,
                        DefaultExtension = selected.Extension.TrimStart('.'),
                        FileTypeChoices = new List<FilePickerFileType>
                        {
                            new FilePickerFileType(selected.Extension.ToUpperInvariant())
                            {
                                Patterns = new List<string> { "*" + selected.Extension },
                            },
                            new FilePickerFileType("All files")
                            {
                                Patterns = new List<string> { "*" },
                            },
                        },
                        ShowOverwritePrompt = true,
                    }).ConfigureAwait(true);

                if (destination is null)
                {
                    saveCancelled = true;
                    break;
                }

                var destinationPath = destination.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    failed++;
                    viewModel.SetDriveDownloadStatus("The chosen location is not a local file path.");
                    continue;
                }

                var selectedPath = destinationPath;
                destinationPath = EnsureExtension(destinationPath, selected.Extension);
                var overwritePromptCoveredDestination = string.Equals(
                    Path.GetFullPath(selectedPath),
                    Path.GetFullPath(destinationPath),
                    StringComparison.OrdinalIgnoreCase);
                if (!overwritePromptCoveredDestination && File.Exists(destinationPath))
                {
                    failed++;
                    viewModel.SetDriveDownloadStatus(
                        "The file with the required extension already exists. Choose a different name.");
                    continue;
                }
                var temporaryPath = destinationPath + ".file-ferry-" +
                    Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    viewModel.SetDriveDownloadStatus("Downloading " + selected.FileName + " (0 bytes)...");
                    await using (var output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 64 * 1024,
                        useAsync: true))
                    {
                        var progress = new Progress<GoogleDriveDownloadProgress>(p =>
                        {
                            viewModel.SetDriveDownloadStatus(
                                $"Downloading {p.FileName} ({p.BytesWritten:N0} bytes"
                                + (p.TotalBytes is { } total ? $" / {total:N0}" : string.Empty)
                                + ")...");
                        });
                        await service.DownloadAsync(
                            selected.DownloadHandle, output, progress, cancellationToken)
                            .ConfigureAwait(true);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(true);
                        output.Flush(flushToDisk: true);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(
                        temporaryPath,
                        destinationPath,
                        overwrite: overwritePromptCoveredDestination);
                    saved++;
                }
                catch (OperationCanceledException)
                {
                    if (!TryDeleteTemporaryFile(temporaryPath))
                    {
                        viewModel.SetDriveDownloadStatus(
                            "Download cancelled, but a partial file may remain at "
                            + temporaryPath + ". Delete it manually.");
                        return;
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    if (!TryDeleteTemporaryFile(temporaryPath))
                    {
                        viewModel.SetDriveDownloadStatus(
                            "Download failed and a partial file may remain at "
                            + temporaryPath + ". Delete it manually.");
                        return;
                    }
                    failed++;
                    viewModel.SetDriveDownloadStatus(
                        selected.FileName + " download failed (" + ex.GetType().Name + ").");
                }
            }
        }
        finally
        {
            service.DiscardDownloadHandles(
                selectedFiles.Select(f => f.DownloadHandle));
        }

        if (saveCancelled)
        {
            viewModel.SetDriveDownloadStatus(
                $"Google Drive download stopped: {saved} saved, {failed} failed. No more files were saved.");
        }
        else if (saved > 0 || failed > 0)
        {
            viewModel.SetDriveDownloadStatus($"Google Drive download finished: {saved} saved, {failed} failed.");
        }
    }

    private static async Task SaveSelectedFilesAsZipAsync(
        TopLevel topLevel,
        CloudPageViewModel viewModel,
        IGoogleDrivePickerDownloadService service,
        IReadOnlyList<GoogleDrivePickerFile> selectedFiles,
        CancellationToken cancellationToken)
    {
        var downloadable = selectedFiles
            .Where(file => string.IsNullOrWhiteSpace(file.Error)
                && !string.IsNullOrWhiteSpace(file.DownloadHandle))
            .ToList();
        var failed = selectedFiles.Count - downloadable.Count;
        if (downloadable.Count == 0)
        {
            viewModel.SetDriveDownloadStatus("No selected files are available to add to a ZIP.");
            return;
        }

        var downloads = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
            WellKnownFolder.Downloads).ConfigureAwait(true);
        var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Save Google Drive files as ZIP",
                SuggestedStartLocation = downloads,
                SuggestedFileName = "Google Drive files.zip",
                DefaultExtension = "zip",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("ZIP archive")
                    {
                        Patterns = new List<string> { "*.zip" },
                    },
                },
                ShowOverwritePrompt = true,
            }).ConfigureAwait(true);

        if (destination is null)
        {
            viewModel.SetDriveDownloadStatus("ZIP save cancelled. No files were saved.");
            return;
        }

        var destinationPath = destination.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            viewModel.SetDriveDownloadStatus("The chosen ZIP location is not a local file path.");
            return;
        }

        var selectedPath = destinationPath;
        destinationPath = EnsureExtension(destinationPath, ".zip");
        var overwritePromptCoveredDestination = string.Equals(
            Path.GetFullPath(selectedPath),
            Path.GetFullPath(destinationPath),
            StringComparison.OrdinalIgnoreCase);
        if (!overwritePromptCoveredDestination && File.Exists(destinationPath))
        {
            viewModel.SetDriveDownloadStatus(
                "The ZIP file with the required extension already exists. Choose a different name.");
            return;
        }

        var temporaryPath = destinationPath + ".file-ferry-" +
            Guid.NewGuid().ToString("N") + ".part";
        var saved = 0;
        try
        {
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                using (var archive = new ZipArchive(
                    output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in downloadable)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(
                            GetUniqueZipEntryName(file.FileName, usedEntryNames),
                            CompressionLevel.Optimal);
                        try
                        {
                            await using var entryStream = entry.Open();
                            var progress = new Progress<GoogleDriveDownloadProgress>(p =>
                                viewModel.SetDriveDownloadStatus(
                                    $"Adding {p.FileName} to ZIP ({p.BytesWritten:N0} bytes"
                                    + (p.TotalBytes is { } total ? $" / {total:N0}" : string.Empty)
                                    + ")..."));
                            await service.DownloadAsync(
                                file.DownloadHandle,
                                entryStream,
                                progress,
                                cancellationToken).ConfigureAwait(true);
                            saved++;
                        }
                        catch (OperationCanceledException)
                        {
                            entry.Delete();
                            throw;
                        }
                        catch (Exception)
                        {
                            entry.Delete();
                            failed++;
                            viewModel.SetDriveDownloadStatus(
                                file.FileName + " could not be added to the ZIP; continuing with the remaining files.");
                        }
                    }
                }

                if (saved == 0)
                {
                    await output.DisposeAsync().ConfigureAwait(true);
                    if (!TryDeleteTemporaryFile(temporaryPath))
                    {
                        viewModel.SetDriveDownloadStatus(
                            "No files could be downloaded, and a temporary file may remain at "
                            + temporaryPath + ". Delete it manually.");
                        return;
                    }
                    viewModel.SetDriveDownloadStatus("No selected files could be downloaded; no ZIP was saved.");
                    return;
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(true);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                destinationPath,
                overwrite: overwritePromptCoveredDestination);
            viewModel.SetDriveDownloadStatus(
                $"ZIP saved with {saved} file(s); {failed} file(s) failed or were skipped.");
        }
        catch (OperationCanceledException)
        {
            if (!TryDeleteTemporaryFile(temporaryPath))
            {
                viewModel.SetDriveDownloadStatus(
                    "ZIP download cancelled, but a partial file may remain at "
                    + temporaryPath + ". Delete it manually.");
                return;
            }
            throw;
        }
        catch (Exception ex)
        {
            if (!TryDeleteTemporaryFile(temporaryPath))
            {
                viewModel.SetDriveDownloadStatus(
                    "ZIP download failed and a partial file may remain at "
                    + temporaryPath + ". Delete it manually.");
                return;
            }
            viewModel.SetDriveDownloadStatus(
                "ZIP download failed (" + ex.GetType().Name + ").");
        }
    }

    private static string GetUniqueZipEntryName(
        string fileName,
        HashSet<string> usedNames)
    {
        var safeName = Path.GetFileName(fileName);
        if (usedNames.Add(safeName))
        {
            return safeName;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = baseName + " (" + suffix + ")" + extension;
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : path + extension;

    private static bool TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
