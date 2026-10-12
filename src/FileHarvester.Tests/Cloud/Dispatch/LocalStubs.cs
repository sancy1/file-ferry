// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Dispatch/LocalStubs.cs
// PURPOSE:  Shared test doubles for the three dispatcher test files.
//           Programmable local resolver, reader, and sink. Each
//           records calls so tests can assert routing.
// RELATION: Used by DispatchingFileResolverTests,
//           DispatchingContentReaderTests, DispatchingOutputSinkTests.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;

namespace FileHarvester.Tests.Cloud.Dispatch;

/// <summary>Programmable ILocalFileResolver.</summary>
internal sealed class StubLocalFileResolver : ILocalFileResolver
{
    public List<IReadOnlyList<FileRequest>> Batches { get; } = new();
    public Func<IReadOnlyList<FileRequest>, Task<IReadOnlyList<ResolvedFile>>>? OnResolve { get; set; }

    public Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(requests, SearchOptions.Default, progress, cancellationToken);

    public async Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        SearchOptions searchOptions,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Batches.Add(requests);
        if (OnResolve is null) return Array.Empty<ResolvedFile>();
        return await OnResolve(requests).ConfigureAwait(false);
    }
}

/// <summary>Programmable ICloudFileResolver.</summary>
internal sealed class StubCloudFileResolver : ICloudFileResolver
{
    public List<IReadOnlyList<FileRequest>> Batches { get; } = new();
    public Func<IReadOnlyList<FileRequest>, Task<IReadOnlyList<ResolvedFile>>>? OnResolve { get; set; }

    public async Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Batches.Add(requests);
        if (OnResolve is null) return Array.Empty<ResolvedFile>();
        return await OnResolve(requests).ConfigureAwait(false);
    }
}

/// <summary>Programmable ILocalContentReader.</summary>
internal sealed class StubLocalContentReader : ILocalContentReader
{
    public List<IReadOnlyList<ResolvedFile>> Batches { get; } = new();
    public Func<IReadOnlyList<ResolvedFile>, Task<IReadOnlyList<FileContent>>>? OnReadMany { get; set; }

    public Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FileContent.Unreadable(file, "stub"));

    public async Task<IReadOnlyList<FileContent>> ReadManyAsync(
        IReadOnlyList<ResolvedFile> files,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Batches.Add(files);
        if (OnReadMany is null) return Array.Empty<FileContent>();
        return await OnReadMany(files).ConfigureAwait(false);
    }
}

/// <summary>Programmable ICloudContentReader.</summary>
internal sealed class StubCloudContentReader : ICloudContentReader
{
    public List<IReadOnlyList<ResolvedFile>> Batches { get; } = new();
    public Func<IReadOnlyList<ResolvedFile>, Task<IReadOnlyList<FileContent>>>? OnReadMany { get; set; }

    public Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FileContent.Unreadable(file, "stub"));

    public async Task<IReadOnlyList<FileContent>> ReadManyAsync(
        IReadOnlyList<ResolvedFile> files,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Batches.Add(files);
        if (OnReadMany is null) return Array.Empty<FileContent>();
        return await OnReadMany(files).ConfigureAwait(false);
    }
}

/// <summary>Programmable ILocalOutputSink.</summary>
internal sealed class StubLocalOutputSink : ILocalOutputSink
{
    public int SaveCalls { get; private set; }
    public Func<ComposedDocument, OutputSinkOptions, Task<SaveResult>>? OnSave { get; set; }

    public Task<SaveResult> SaveAsync(
        ComposedDocument document,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        if (OnSave is null)
        {
            return Task.FromResult(SaveResult.Ok(
                destinationFolder: options.DestinationFolder ?? "local",
                outputPath: options.DestinationFolder ?? "local",
                fileCount: document.Files.Count,
                bytesWritten: 0));
        }
        return OnSave(document, options);
    }
}

/// <summary>Programmable ICloudOutputSink.</summary>
internal sealed class StubCloudOutputSink : ICloudOutputSink
{
    public int SaveCalls { get; private set; }
    public Func<ComposedDocument, OutputSinkOptions, Task<SaveResult>>? OnSave { get; set; }

    public Task<SaveResult> SaveAsync(
        ComposedDocument document,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        if (OnSave is null)
        {
            return Task.FromResult(SaveResult.Ok(
                destinationFolder: options.DestinationFolder ?? "cloud",
                outputPath: options.DestinationFolder ?? "cloud",
                fileCount: document.Files.Count,
                bytesWritten: 0));
        }
        return OnSave(document, options);
    }
}
