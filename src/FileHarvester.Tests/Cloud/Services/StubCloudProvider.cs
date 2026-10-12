// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Services/StubCloudProvider.cs
// PURPOSE:  A programmable test double for ICloudProvider. Each method
//           has a settable delegate. Unset methods throw
//           NotSupportedException so tests that accidentally reach an
//           unconfigured method fail loudly.
// RELATION: Used by CloudFileResolverTests, CloudContentReaderTests, and
//           CloudOutputSinkTests. Not used in production.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Tests.Cloud.Services;

/// <summary>
/// A test double for <see cref="ICloudProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every ICloudProvider method has a settable delegate. If a test does
/// not configure a delegate and the method is called, the stub throws
/// <see cref="NotSupportedException"/>. This makes an accidental call
/// visible as a test failure rather than a silent null.
/// </para>
/// <para>
/// Every call is recorded in <see cref="Calls"/> as
/// <c>MethodName:path</c>. Tests assert on the recorded calls to prove
/// which methods ran and in what order.
/// </para>
/// </remarks>
internal sealed class StubCloudProvider : ICloudProvider
{
    /// <summary>Delegate for OpenReadAsync. Null means the method throws.</summary>
    public Func<string, Task<Stream>>? OnOpenRead { get; set; }

    /// <summary>Delegate for ListAsync. Null means the method throws.</summary>
    public Func<string, string?, int, Task<CloudEntryPage>>? OnList { get; set; }

    /// <summary>Delegate for OpenWriteAsync. Null means the method throws.</summary>
    public Func<string, string?, Task<CloudWriteHandle>>? OnOpenWrite { get; set; }

    /// <summary>Delegate for ExistsAsync. Null means the method throws.</summary>
    public Func<string, Task<bool>>? OnExists { get; set; }

    /// <summary>Delegate for DeleteAsync. Null means the method throws.</summary>
    public Func<string, Task>? OnDelete { get; set; }

    /// <summary>Delegate for GetMetadataAsync. Null means the method throws.</summary>
    public Func<string, Task<ProviderMetadata>>? OnGetMetadata { get; set; }

    /// <summary>
    /// <summary>
    /// The scheme the factory received, or null when the test does not set it.
    /// </summary>
    public FileHarvester.Core.Domain.PathSchemeKind? FactoryScheme { get; set; }

    /// <summary>
    /// The instance the factory received, or null when the test does not set it.
    /// </summary>
    public string? FactoryInstance { get; set; }

    /// <summary>
    /// The content captured by the most recent OpenWriteAsync. Null until a write.
    /// </summary>
    public string? LastWriteContent { get; set; }

    /// <summary>
    /// True when the most recent call received an already-cancelled token.
    /// </summary>
    public bool LastCancellationTokenWasCancelled { get; set; }

    /// Every method call, in order, as <c>MethodName:path</c>.
    /// </summary>
    public List<string> Calls { get; } = new();

    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("OpenReadAsync:" + cloudPath);
        var handler = OnOpenRead;
        if (handler is null)
        {
            throw new NotSupportedException(
                "OpenReadAsync was called but OnOpenRead is not set.");
        }
        return handler(cloudPath);
    }

    /// <inheritdoc/>
    public Task<CloudEntryPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxEntries,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("ListAsync:" + prefix);
        var handler = OnList;
        if (handler is null)
        {
            throw new NotSupportedException(
                "ListAsync was called but OnList is not set.");
        }
        return handler(prefix, continuationToken, maxEntries);
    }

    /// <inheritdoc/>
    public Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("OpenWriteAsync:" + cloudPath);
        var handler = OnOpenWrite;
        if (handler is null)
        {
            throw new NotSupportedException(
                "OpenWriteAsync was called but OnOpenWrite is not set.");
        }
        return handler(cloudPath, contentType);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("ExistsAsync:" + cloudPath);
        var handler = OnExists;
        if (handler is null)
        {
            throw new NotSupportedException(
                "ExistsAsync was called but OnExists is not set.");
        }
        return handler(cloudPath);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("DeleteAsync:" + cloudPath);
        var handler = OnDelete;
        if (handler is null)
        {
            throw new NotSupportedException(
                "DeleteAsync was called but OnDelete is not set.");
        }
        return handler(cloudPath);
    }

    /// <inheritdoc/>
    public Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("GetMetadataAsync:" + cloudPath);
        var handler = OnGetMetadata;
        if (handler is null)
        {
            throw new NotSupportedException(
                "GetMetadataAsync was called but OnGetMetadata is not set.");
        }
        return handler(cloudPath);
    }
}
