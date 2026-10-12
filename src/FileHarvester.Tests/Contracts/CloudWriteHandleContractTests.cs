// ============================================================================
// FILE:     src/FileHarvester.Tests/Contracts/CloudWriteHandleContractTests.cs
// PURPOSE:  Contract tests for CloudWriteHandle. Every test asserts a
//           behavior documented on the class. Tests are written from
//           the XML doc, not from the implementation.
// RELATION: Executable contract tier. No test doubles needed.
// ============================================================================

using FileHarvester.Core.Domain;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Contracts;

public sealed class CloudWriteHandleContractTests
{
    [Fact]
    public async Task CompleteInvokesDelegateOnce()
    {
        // The doc says the delegate is "Invoked exactly once, by
        // CompleteAsync."
        var invoked = 0;
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ =>
        {
            invoked++;
            return Task.CompletedTask;
        });

        await handle.CompleteAsync();

        invoked.Should().Be(1);
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task SecondCompleteThrows()
    {
        // The doc says "Can be called at most once."
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        await handle.CompleteAsync();

        var act = async () => await handle.CompleteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        // The doc says DisposeAsync is "Safe to call multiple times."
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        await handle.DisposeAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task CompleteAfterDisposeThrows()
    {
        // The doc says "A CompleteAsync call after dispose throws
        // ObjectDisposedException."
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        await handle.DisposeAsync();

        var act = async () => await handle.CompleteAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeWithoutCompleteDoesNotThrow()
    {
        // The doc says "Disposing without calling CompleteAsync aborts
        // the upload." Aborting is not an error; dispose must not throw.
        var stream = new MemoryStream();
        var handle = new CloudWriteHandle(stream, _ => Task.CompletedTask);

        var act = async () => await handle.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
