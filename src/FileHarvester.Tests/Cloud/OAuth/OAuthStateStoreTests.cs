// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/OAuthStateStoreTests.cs
// PURPOSE:  Unit tests for OAuthStateStore. Verifies state generation,
//           storage, one-time consumption, expiry, and input validation.
//           The store is pure logic; the clock is injected so expiry is
//           tested deterministically without sleeping.
// RELATION: Exercises FileHarvester.Platform.Cloud.OAuth.OAuthStateStore.
// ============================================================================

using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>
/// Tests for <see cref="OAuthStateStore"/>.
/// </summary>
public sealed class OAuthStateStoreTests
{
    [Fact]
    public void NewStateReturnsNonEmptyBase64UrlString()
    {
        var state = OAuthStateStore.NewState();

        state.Should().NotBeNullOrEmpty();
        state.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        state.Should().NotContain("=");
    }

    [Fact]
    public void NewStateReturnsDifferentValuesOnEachCall()
    {
        var a = OAuthStateStore.NewState();
        var b = OAuthStateStore.NewState();

        a.Should().NotBe(b);
    }

    [Fact]
    public void StoreThenConsumeReturnsTheVerifier()
    {
        var store = new OAuthStateStore();
        var state = OAuthStateStore.NewState();
        const string verifier = "the-pkce-verifier";

        store.Store(state, verifier);
        var consumed = store.Consume(state);

        consumed.Should().Be(verifier);
    }

    [Fact]
    public void ConsumeRemovesEntryOneTimeUse()
    {
        var store = new OAuthStateStore();
        var state = OAuthStateStore.NewState();
        store.Store(state, "verifier");

        store.Consume(state);

        var act = () => store.Consume(state);
        act.Should().Throw<OAuthStateException>();
    }

    [Fact]
    public void ConsumeUnknownStateThrows()
    {
        var store = new OAuthStateStore();
        var unknown = OAuthStateStore.NewState();

        var act = () => store.Consume(unknown);
        act.Should().Throw<OAuthStateException>();
    }

    [Fact]
    public void ConsumeExpiredStateThrows()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new OAuthStateStore(() => now);
        var state = OAuthStateStore.NewState();
        store.Store(state, "verifier");

        // Advance the clock past the entry lifetime.
        now = now.Add(OAuthStateStore.EntryLifetime).Add(TimeSpan.FromSeconds(1));

        var act = () => store.Consume(state);
        act.Should().Throw<OAuthStateException>();
    }

    [Fact]
    public void StoreRejectsDuplicateState()
    {
        var store = new OAuthStateStore();
        var state = OAuthStateStore.NewState();
        store.Store(state, "first-verifier");

        var act = () => store.Store(state, "second-verifier");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "verifier")]
    [InlineData("", "verifier")]
    [InlineData("   ", "verifier")]
    [InlineData("state", "")]
    public void StoreRejectsNullOrWhitespaceInput(string? state, string verifier)
    {
        var store = new OAuthStateStore();

        var act = () => store.Store(state!, verifier);
        act.Should().Throw<ArgumentException>();
    }
}
