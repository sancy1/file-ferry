// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/CloudProfilesTests.cs
// PURPOSE:  Unit tests for BuiltInProfiles, LlmPromptGenerator, and
//           ProviderProfileStore. All three are pure or file-local.
// RELATION: Consumes FileHarvester.Core.Domain.BuiltInProfiles,
//           FileHarvester.Core.Implementation.LlmPromptGenerator, and
//           FileHarvester.Platform.Cloud.Profiles.ProviderProfileStore.
// ============================================================================

using System.Text.Json;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Platform.Cloud.Profiles;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud;

/// <summary>
/// Tests for the C.2 provider profile data model and store.
/// </summary>
public sealed class CloudProfilesTests : IDisposable
{
    private readonly string _tempRoot;

    public CloudProfilesTests()
    {
        _tempRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "file-ferry-profiles-tests",
            Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    // ------------------------------------------------------------------
    // BuiltInProfiles - structural checks
    // ------------------------------------------------------------------

    [Fact]
    public void AllContainsElevenProfiles()
    {
        BuiltInProfiles.All.Should().HaveCount(11);
    }

    [Fact]
    public void FindByIdAwsS3ReturnsProfile()
    {
        var p = BuiltInProfiles.FindById("aws-s3");
        p.Should().NotBeNull();
        p!.DisplayName.Should().Be("AWS S3");
    }

    [Fact]
    public void FindByIdIsCaseInsensitive()
    {
        BuiltInProfiles.FindById("AWS-S3").Should().NotBeNull();
        BuiltInProfiles.FindById("Aws-S3").Should().NotBeNull();
    }

    [Fact]
    public void FindByIdUnknownReturnsNull()
    {
        BuiltInProfiles.FindById("not-a-provider").Should().BeNull();
    }

    [Fact]
    public void FindByIdNullReturnsNull()
    {
        BuiltInProfiles.FindById(null).Should().BeNull();
    }

    [Fact]
    public void FindByIdEmptyReturnsNull()
    {
        BuiltInProfiles.FindById("   ").Should().BeNull();
    }

    // ------------------------------------------------------------------
    // BuiltInProfiles - per-profile validity
    // ------------------------------------------------------------------

    [Fact]
    public void EveryProfileHasRequiredFields()
    {
        foreach (var p in BuiltInProfiles.All)
        {
            p.Id.Should().NotBeNullOrWhiteSpace();
            p.DisplayName.Should().NotBeNullOrWhiteSpace();
            p.HelpPromptTemplate.Should().NotBeNullOrWhiteSpace();
            p.HelpPromptTemplate.Should().Contain("[DisplayName]");
        }
    }

    [Fact]
    public void EveryProfileIdIsUnique()
    {
        var ids = BuiltInProfiles.All.Select(p => p.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AwsS3HasFourCredentialFields()
    {
        var p = BuiltInProfiles.FindById("aws-s3")!;
        p.Kind.Should().Be(CloudProviderKind.S3Compatible);
        p.AuthKind.Should().Be(AuthKind.LongLivedKey);
        p.CredentialFields.Should().HaveCount(4);
    }

    [Fact]
    public void AzureBlobUsesConnectionStringAuth()
    {
        var p = BuiltInProfiles.FindById("azure-blob")!;
        p.Kind.Should().Be(CloudProviderKind.AzureBlob);
        p.AuthKind.Should().Be(AuthKind.ConnectionString);
    }

    [Fact]
    public void GcsUsesServiceAccountFile()
    {
        var p = BuiltInProfiles.FindById("gcs")!;
        p.Kind.Should().Be(CloudProviderKind.GcsObject);
        p.CredentialFields.Should().HaveCount(1);
        p.CredentialFields[0].Kind.Should().Be(FieldKind.File);
    }

    [Fact]
    public void S3CompatibleProvidersHaveEndpointUrl()
    {
        var ids = new[] { "cloudflare-r2", "backblaze-b2", "wasabi", "digitalocean-spaces", "minio" };
        foreach (var id in ids)
        {
            var p = BuiltInProfiles.FindById(id)!;
            p.Kind.Should().Be(CloudProviderKind.S3Compatible);
            p.DefaultEndpointUrl.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GoogleDriveRequiresClientSecretWhileOtherOAuthProvidersHaveNoCredentialFields()
    {
        var googleDrive = BuiltInProfiles.FindById("gdrive")!;
        googleDrive.AuthKind.Should().Be(AuthKind.OAuth);
        googleDrive.CredentialFields.Should().ContainSingle();
        googleDrive.CredentialFields[0].Key.Should().Be("clientSecret");
        googleDrive.CredentialFields[0].Kind.Should().Be(FieldKind.Password);
        googleDrive.CredentialFields[0].Required.Should().BeTrue();

        foreach (var id in new[] { "onedrive", "dropbox" })
        {
            var profile = BuiltInProfiles.FindById(id)!;
            profile.AuthKind.Should().Be(AuthKind.OAuth);
            profile.CredentialFields.Should().BeEmpty();
        }
    }

    // ------------------------------------------------------------------
    // LlmPromptGenerator
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateReplacesDisplayName()
    {
        var p = BuiltInProfiles.FindById("aws-s3")!;
        var prompt = LlmPromptGenerator.Generate(p);

        prompt.Should().Contain("AWS S3");
        prompt.Should().NotContain("[DisplayName]");
    }

    [Fact]
    public void GenerateHandlesEmptyTemplate()
    {
        var p = new ProviderProfile(
            Id: "test-empty",
            DisplayName: "Test Provider",
            Kind: CloudProviderKind.S3Compatible,
            AuthKind: AuthKind.LongLivedKey,
            CredentialFields: Array.Empty<FieldSpec>(),
            DefaultEndpointUrl: null,
            HelpPromptTemplate: string.Empty,
            IconKey: null);

        var prompt = LlmPromptGenerator.Generate(p);

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Should().Contain("Test Provider");
    }

    [Fact]
    public void GenerateHandlesTemplateWithNoPlaceholder()
    {
        var p = new ProviderProfile(
            Id: "test-no-placeholder",
            DisplayName: "NoPlaceholderProvider",
            Kind: CloudProviderKind.S3Compatible,
            AuthKind: AuthKind.LongLivedKey,
            CredentialFields: Array.Empty<FieldSpec>(),
            DefaultEndpointUrl: null,
            HelpPromptTemplate: "Tell me about credentials.",
            IconKey: null);

        var prompt = LlmPromptGenerator.Generate(p);

        prompt.Should().Contain("NoPlaceholderProvider");
        prompt.Should().Contain("Tell me about credentials.");
    }

    [Fact]
    public void GenerateIsIdempotent()
    {
        var p = BuiltInProfiles.FindById("azure-blob")!;
        var a = LlmPromptGenerator.Generate(p);
        var b = LlmPromptGenerator.Generate(p);
        a.Should().Be(b);
    }

    [Fact]
    public void GenerateRejectsNull()
    {
        var act = () => LlmPromptGenerator.Generate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // ProviderProfileStore
    // ------------------------------------------------------------------

    private static ProviderProfile MakeCustomProfile(string id) => new(
        Id: id,
        DisplayName: "Custom " + id,
        Kind: CloudProviderKind.S3Compatible,
        AuthKind: AuthKind.LongLivedKey,
        CredentialFields: new[]
        {
            new FieldSpec(
                Key: "endpoint",
                Label: "Endpoint",
                Kind: FieldKind.Text,
                Required: true,
                Placeholder: null,
                HelpText: null,
                ValidatePattern: null),
        },
        DefaultEndpointUrl: "https://example.com",
        HelpPromptTemplate: "Help me with [DisplayName].",
        IconKey: null);

    [Fact]
    public async Task SaveThenListReturnsProfile()
    {
        var store = new ProviderProfileStore(_tempRoot);
        var profile = MakeCustomProfile("custom-one");

        await store.SaveCustomAsync(profile);
        var list = await store.ListCustomAsync();

        list.Should().HaveCount(1);
        list[0].Id.Should().Be("custom-one");
        list[0].DisplayName.Should().Be("Custom custom-one");
    }

    [Fact]
    public async Task SaveTwiceReplacesExisting()
    {
        var store = new ProviderProfileStore(_tempRoot);
        await store.SaveCustomAsync(MakeCustomProfile("custom-two"));
        await store.SaveCustomAsync(MakeCustomProfile("custom-two"));

        var list = await store.ListCustomAsync();
        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteRemovesProfile()
    {
        var store = new ProviderProfileStore(_tempRoot);
        await store.SaveCustomAsync(MakeCustomProfile("custom-three"));
        await store.DeleteCustomAsync("custom-three");

        var list = await store.ListCustomAsync();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteNonExistentIsNoOp()
    {
        var store = new ProviderProfileStore(_tempRoot);
        var act = async () => await store.DeleteCustomAsync("nothing");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MalformedFileReturnsEmptyList()
    {
        var filePath = Path.Combine(_tempRoot, "cloud-profiles.json");
        await File.WriteAllTextAsync(filePath, "{ this is not valid json");

        var store = new ProviderProfileStore(_tempRoot);
        var list = await store.ListCustomAsync();

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingFileReturnsEmptyList()
    {
        var store = new ProviderProfileStore(_tempRoot);
        var list = await store.ListCustomAsync();
        list.Should().BeEmpty();
    }
}
