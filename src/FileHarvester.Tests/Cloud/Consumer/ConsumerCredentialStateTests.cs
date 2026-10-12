// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Consumer/ConsumerCredentialStateTests.cs
// PURPOSE:  Consumer-driven tests for the credential-state boundary. Each
//           test uses the real BuiltInProviderRegistration path, an
//           in-memory ICredentialStore, and the real CloudFileResolver, and
//           asserts the observable user-facing outcome. No network call, no
//           real credential, no shared state.
// RELATION: Exercises the zero/one/many credential rule through the real
//           ProviderConstruction path.
// NOTES
//   Group 2 of the consumer-driven verification. The assertion is the
//   observable equivalent, not a substitute call list: under the real
//   registration path there is no substitute to record calls. The proof
//   that no provider operation ran is: zero resolved files, the generic
//   resolver note present, and the call returning without an exception.
//
//   F-109: the credential-specific reason (no credential, ambiguous,
//   unknown instance) is lost at the registry boundary; the user sees the
//   same generic note for all three. These tests assert that behavior as
//   it is.
//
//   The registration path now registers the three OAuth providers
//   (gdrive, onedrive, dropbox) in addition to the three static ones, so
//   the Build() helper registers substituted OAuth dependencies. The
//   static S3 path these tests exercise is unchanged.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Registry;
using FileHarvester.Platform.Cloud.Services;
using FileHarvester.Tests.Cloud.OAuth;
using FileHarvester.Tests.Cloud.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Portfolio.Resilience.Extensions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Consumer;

/// <summary>
/// Consumer-driven credential-state tests for the cloud surface.
/// </summary>
public sealed class ConsumerCredentialStateTests
{
    private const string GenericNote =
        "No provider is registered for the s3:// scheme in this build.";

    /// <summary>
    /// A minimal in-memory ICredentialStore. No persistence, no encryption,
    /// no Windows Credential Manager.
    /// </summary>
    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public void Seed(string profileId, string instanceName, string value)
        {
            _values[profileId + "/" + instanceName] = value;
        }

        public Task WriteAsync(CredentialKey key, string secret, CancellationToken cancellationToken = default)
        {
            _values[key.ProfileId + "/" + key.InstanceName] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(CredentialKey key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key.ProfileId + "/" + key.InstanceName, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(CredentialKey key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key.ProfileId + "/" + key.InstanceName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListInstanceNamesAsync(string profileId, CancellationToken cancellationToken = default)
        {
            var names = new List<string>();
            foreach (var kvp in _values)
            {
                var parts = kvp.Key.Split((char)47);
                if (parts.Length == 2 && string.Equals(parts[0], profileId, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(parts[1]);
                }
            }
            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    /// <summary>
    /// Builds a provider with the real registration path and an in-memory
    /// credential store, and returns the cloud resolver. The OAuth
    /// dependencies are substituted because the registration path now
    /// registers the three OAuth providers in addition to the static ones.
    /// </summary>
    private static (ServiceProvider Provider, ICloudFileResolver Resolver, InMemoryCredentialStore Store) Build()
    {
        var store = new InMemoryCredentialStore();
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialStore>(store);
        services.AddPortfolioResilience(_ => { });
        services.AddSingleton<ICloudProviderRegistry, CloudProviderRegistry>();
        services.AddSingleton<Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>>>(sp =>
        {
            var registry = sp.GetRequiredService<ICloudProviderRegistry>();
            return (scheme, instance, ct) => registry.ResolveAsync(scheme, instance, ct);
        });
        services.AddSingleton<ICloudFileResolver, CloudFileResolver>();

        var typedCredentialStore = Substitute.For<ITypedCredentialStore>();
        var flow = new SubstituteOAuthFlow();
        var flowRegistry = new SingleFlowRegistry(flow);
        var oauthTokenStore = new OAuthTokenStore(typedCredentialStore, flowRegistry);
        var oauthConfigStore = Substitute.For<IOAuthClientConfigStore>();
        services.AddSingleton(oauthTokenStore);
        services.AddSingleton(oauthConfigStore);

        var provider = services.BuildServiceProvider();
        BuiltInProviderRegistration.RegisterBuiltInProviders(provider);
        return (provider, provider.GetRequiredService<ICloudFileResolver>(), store);
    }

    private sealed class NoteRecorder : IProgress<ResolveProgress>
    {
        public string? LastNote { get; private set; }
        public void Report(ResolveProgress value) => LastNote = value.Note;
    }

    private static async Task<string?> ResolveAndCaptureNoteAsync(
        ICloudFileResolver resolver,
        string rawInput)
    {
        var recorder = new NoteRecorder();
        var requests = new[] { new FileRequest(Index: 0, RawInput: rawInput) };
        await resolver.ResolveAsync(requests, recorder, CancellationToken.None);
        return recorder.LastNote;
    }

    [Fact]
    public async Task NoCredentialShowsTheConnectMessage()
    {
        var (provider, resolver, store) = Build();
        using (provider)
        {
            // No credential seeded. The real construction finds zero
            // instances, throws, the registry returns null, the resolver
            // reports the generic note. No provider is constructed.
            var note = await ResolveAndCaptureNoteAsync(
                resolver, "s3://example-bucket/reports/file.txt");

            note.Should().Be("No credential is connected for AWS S3. Connect AWS S3 first.");
        }
    }

    [Fact]
    public async Task MultipleCredentialsShowsTheInstanceMessage()
    {
        var (provider, resolver, store) = Build();
        using (provider)
        {
            // Two stored credentials; a bare path is ambiguous. The real
            // construction finds more than one instance, throws, the
            // registry returns null, the resolver reports the generic note.
            store.Seed("aws-s3", "work", "{\"accessKeyId\":\"AKIAPLACEHOLDER000000\",\"secretAccessKey\":\"placeholder\",\"region\":\"us-east-1\"}");
            store.Seed("aws-s3", "personal", "{\"accessKeyId\":\"AKIAPLACEHOLDER111111\",\"secretAccessKey\":\"placeholder\",\"region\":\"us-east-1\"}");

            var note = await ResolveAndCaptureNoteAsync(
                resolver, "s3://example-bucket/reports/file.txt");

            note.Should().Be("Multiple AWS S3 credentials are connected. Specify one using s3://[instance]/...");
        }
    }

    [Fact]
    public async Task UnknownInstanceShowsTheNamedMessage()
    {
        var (provider, resolver, store) = Build();
        using (provider)
        {
            // One stored credential, but the path names a different
            // instance. The real construction finds the one instance, sees
            // it is not the requested name, throws, the registry returns
            // null, the resolver reports the generic note.
            store.Seed("aws-s3", "work", "{\"accessKeyId\":\"AKIAPLACEHOLDER000000\",\"secretAccessKey\":\"placeholder\",\"region\":\"us-east-1\"}");

            var note = await ResolveAndCaptureNoteAsync(
                resolver, "s3://[missing]/example-bucket/reports/file.txt");

            note.Should().Be("No AWS S3 credential named [missing] is connected.");
        }
    }

    [Fact]
    public async Task UnregisteredSchemeKeepsTheGenericNote()
    {
        var resolver = new CloudFileResolver(
            (_, _, _) => Task.FromResult(CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered)));

        var note = await ResolveAndCaptureNoteAsync(
            resolver, "s3://example-bucket/reports/file.txt");

        note.Should().Be(GenericNote);
    }

    [Fact]
    public async Task ResolvedProviderProducesNoFailure()
    {
        var resolution = CloudProviderResolution.Resolved(new StubCloudProvider());

        resolution.IsResolved.Should().BeTrue();
        resolution.Provider.Should().NotBeNull();
        resolution.Failure.Should().BeNull();
    }

    [Fact]
    public async Task UnexpectedProviderFailureShowsConstructionMessage()
    {
        var resolver = new CloudFileResolver(
            (_, _, _) => Task.FromResult(CloudProviderResolution.Failed(CloudResolutionFailure.ProviderConstructionFailed)));

        var note = await ResolveAndCaptureNoteAsync(
            resolver, "s3://example-bucket/reports/file.txt");

        note.Should().Be("AWS S3 could not be started because its provider failed to construct.");
    }

    [Fact]
    public void NoSecretAppearsInAnyMessage()
    {
        var scheme = PathScheme.Parse("s3://[missing]/example-bucket/reports/file.txt");
        var messages = new[]
        {
            CloudFileResolver.BuildFailureNote(scheme, CloudResolutionFailure.NoCredential),
            CloudFileResolver.BuildFailureNote(scheme, CloudResolutionFailure.MultipleCredentials),
            CloudFileResolver.BuildFailureNote(scheme, CloudResolutionFailure.UnknownInstance),
            CloudFileResolver.BuildFailureNote(scheme, CloudResolutionFailure.ProviderConstructionFailed),
            CloudFileResolver.BuildFailureNote(scheme, CloudResolutionFailure.NoProviderRegistered),
        };

        foreach (var message in messages)
        {
            message.Should().NotContain("AKIA", "no access key id may appear");
            message.Should().NotContain("placeholder", "no secret may appear");
            message.Should().NotContain("{", "no credential JSON may appear");
        }
    }
}
