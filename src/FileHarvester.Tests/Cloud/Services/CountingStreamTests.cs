// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Services/CountingStreamTests.cs
// PURPOSE:  Unit tests for CountingStream. Verifies byte counting,
//           non-ownership of the inner stream, and the disposal
//           contract that CloudWriteHandle owns and disposes the
//           inner stream.
// RELATION: Consumes FileHarvester.Platform.Cloud.Services.CountingStream
//           (internal; visible via InternalsVisibleTo). Also exercises
//           CloudWriteHandle to prove the ownership handoff. No I/O,
//           no network, no resilience.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Services;

/// <summary>
/// Tests for <see cref="CountingStream"/>.
/// </summary>
public sealed class CountingStreamTests
{
    // ------------------------------------------------------------------
    // Byte counting
    // ------------------------------------------------------------------

    [Fact]
    public void WriteAccumulatesBytesWritten()
    {
        using var inner = new MemoryStream();
        var counting = new CountingStream(inner);

        var a = new byte[5];
        var b = new byte[10];
        var c = new byte[15];

        counting.Write(a, 0, a.Length);
        counting.Write(b, 0, b.Length);
        counting.Write(c, 0, c.Length);

        counting.BytesWritten.Should().Be(30);
    }

    [Fact]
    public async Task WriteAsyncAccumulatesBytesWritten()
    {
        using var inner = new MemoryStream();
        var counting = new CountingStream(inner);

        var a = new byte[7];
        var b = new byte[13];

        // The byte[]-based WriteAsync overload is exercised here on purpose:
        // CountingStream overrides it, and callers who use the older API
        // reach it directly. The analyzer prefers the ReadOnlyMemory overload,
        // so the warning is suppressed for these two calls only.
#pragma warning disable CA1835
        await counting.WriteAsync(a, 0, a.Length);
        await counting.WriteAsync(b, 0, b.Length);
#pragma warning restore CA1835

        counting.BytesWritten.Should().Be(20);
    }

    [Fact]
    public async Task WriteAsyncMemoryOverloadAccumulatesBytesWritten()
    {
        using var inner = new MemoryStream();
        var counting = new CountingStream(inner);

        var a = new byte[4];
        var b = new byte[6];

        await counting.WriteAsync(a.AsMemory());
        await counting.WriteAsync(b.AsMemory());

        counting.BytesWritten.Should().Be(10);
    }

    // ------------------------------------------------------------------
    // Seek and position behavior
    // ------------------------------------------------------------------

    [Fact]
    public void CanSeekIsFalse()
    {
        using var inner = new MemoryStream();
        inner.CanSeek.Should().BeTrue();

        var counting = new CountingStream(inner);
        counting.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void SeekThrowsNotSupported()
    {
        using var inner = new MemoryStream();
        var counting = new CountingStream(inner);

        var act = () => counting.Seek(0, SeekOrigin.Begin);
        act.Should().Throw<NotSupportedException>();
    }

    // ------------------------------------------------------------------
    // Disposal ownership: the counting stream is non-owning
    // ------------------------------------------------------------------

    [Fact]
    public void DisposingCountingStreamDoesNotDisposeInnerStream()
    {
        using var inner = new MemoryStream();
        var counting = new CountingStream(inner);

        counting.Dispose();

        inner.CanWrite.Should().BeTrue();
        var act = () => inner.Write(new byte[] { 1, 2, 3 }, 0, 3);
        act.Should().NotThrow();
    }

    [Fact]
    public void DisposingCountingStreamTwiceIsHarmless()
    {
        using var inner = new MemoryStream();
        var counting = new CountingStream(inner);

        var act = () =>
        {
            counting.Dispose();
            counting.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task HandleCanCompleteAndDisposeInnerStreamAfterCountingStreamDisposed()
    {
        // Full ownership handoff: the handle owns the inner stream;
        // the counting wrapper is non-owning. This test proves the
        // handoff works end-to-end.
        var inner = new MemoryStream();
        var committed = false;

        Func<CancellationToken, Task> commit = _ =>
        {
            committed = true;
            return Task.CompletedTask;
        };

        var handle = new CloudWriteHandle(inner, commit);
        var counting = new CountingStream(handle.Stream);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        counting.Write(data, 0, data.Length);
        counting.BytesWritten.Should().Be(5);

        // Dispose the counting wrapper first. The inner stream must survive.
        counting.Dispose();
        inner.CanWrite.Should().BeTrue();

        // The handle must still be able to complete.
        await handle.CompleteAsync();
        committed.Should().BeTrue();

        // The handle disposes the inner stream.
        await handle.DisposeAsync();
        inner.CanWrite.Should().BeFalse();
    }
}
