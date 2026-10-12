// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/AzureBlobProviderFactoryTests.cs
// PURPOSE:  Unit tests for AzureBlobProviderFactory. Ten tests covering the
//           three auth shapes (connection string, account key, SAS token),
//           custom endpoint suffix, argument validation, and SAS token
//           normalization rejections (internal whitespace, fragment char).
//           Uses real BlobServiceClient instances — no mocking required,
//           because the constructor only parses the URI and stores the
//           credential without opening a network connection.
// RELATION: Exercises AzureBlobProviderFactory. Every credential in this
//           file is a placeholder; no real secret appears anywhere.
// ============================================================================

using Azure.Storage.Blobs;
using FileHarvester.Platform.Cloud.Providers;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>
/// Unit tests for <see cref="AzureBlobProviderFactory"/>.
/// </summary>
public sealed class AzureBlobProviderFactoryTests
{
    // ---- Placeholder credentials — never real values --------------------

    private const string PlaceholderConnectionString =
        "DefaultEndpointsProtocol=https;" +
        "AccountName=placeholderaccount;" +
        "AccountKey=cGxhY2Vob2xkZXItYWNjb3VudC1rZXk=;" +
        "EndpointSuffix=core.windows.net";

    private const string PlaceholderAccountKey =
        "cGxhY2Vob2xkZXItYWNjb3VudC1rZXk=";

    private const string PlaceholderSasToken =
        "sv=2024-11-04&ss=b&srt=sco&sp=rwdlacupx&se=2030-01-01T00:00:00Z&st=2024-01-01T00:00:00Z&spr=https&sig=placeholder-signature";

    private const string AccountName = "placeholderaccount";
    private const string ContainerName = "placeholder-container";

    // ====================================================================
    // Test 1 — Connection string round-trips to the account name
    // ====================================================================
    [Fact]
    public void CreateClientWithConnectionStringUsesTheString()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(
            AccountName: "ignored-for-connection-string");

        // Act
        BlobServiceClient client = AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.ConnectionString,
            PlaceholderConnectionString);

        // Assert — the client's URI host contains the account name from
        // the connection string, not from the settings record.
        client.Uri.Host.Should().Contain("placeholderaccount");
        client.Uri.Scheme.Should().Be("https");
    }

    // ====================================================================
    // Test 2 — Account key uses account name from settings
    // ====================================================================
    [Fact]
    public void CreateClientWithAccountKeyUsesAccountNameFromSettings()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(AccountName);

        // Act
        BlobServiceClient client = AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.AccountKey,
            PlaceholderAccountKey);

        // Assert
        client.Uri.Host.Should().Be($"{AccountName}.blob.core.windows.net");
        client.Uri.Scheme.Should().Be("https");
        client.Uri.AbsolutePath.Should().Be("/");
    }

    // ====================================================================
    // Test 3 — SAS token without container builds account-scoped URI
    // ====================================================================
    [Fact]
    public void CreateClientWithSasTokenBuildsAccountScopedUri()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(AccountName);

        // Act
        BlobServiceClient client = AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.SasToken,
            PlaceholderSasToken);

        // Assert — no container in the URI, but the SAS query is present.
        client.Uri.Host.Should().Be($"{AccountName}.blob.core.windows.net");
        client.Uri.AbsolutePath.Should().Be("/");
        client.Uri.Query.Should().NotBeNullOrEmpty();
        client.Uri.Query.Should().Contain("sig=placeholder-signature");
    }

    // ====================================================================
    // Test 4 — SAS token with container builds container-scoped URI
    // ====================================================================
    [Fact]
    public void CreateClientWithSasTokenAndContainerBuildsContainerScopedUri()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(
            AccountName,
            CustomEndpointSuffix: null,
            ContainerName: ContainerName);

        // Act
        BlobServiceClient client = AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.SasToken,
            PlaceholderSasToken);

        // Assert — the container appears in the path, SAS in the query.
        client.Uri.Host.Should().Be($"{AccountName}.blob.core.windows.net");
// The Azure SDK normalizes container-scoped URIs to include a
        // trailing slash: https://account.blob.core.windows.net/container/?<sas>.
        // This is documented SDK behavior — BlobContainerClient appends
        // blob names to the base path, and the base path requires the
        // trailing separator. Our factory constructs the un-normalized
        // form; the SDK normalizes it once at construction time.
        client.Uri.AbsolutePath.Should().Be($"/{ContainerName}/");
        client.Uri.Query.Should().NotBeNullOrEmpty();
    }

    // ====================================================================
    // Test 5 — Custom endpoint suffix round-trips
    // ====================================================================
    [Fact]
    public void CreateClientWithCustomEndpointSuffixUsesIt()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(
            AccountName,
            CustomEndpointSuffix: "core.chinacloudapi.cn");

        // Act
        BlobServiceClient client = AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.AccountKey,
            PlaceholderAccountKey);

        // Assert
        client.Uri.Host.Should().Be($"{AccountName}.blob.core.chinacloudapi.cn");
    }

    // ====================================================================
    // Test 6 — Null settings are rejected
    // ====================================================================
    [Fact]
    public void CreateClientRejectsNullSettings()
    {
        // Act
        Action act = () => AzureBlobProviderFactory.CreateClient(
            settings: null!,
            AzureBlobAuthKind.AccountKey,
            PlaceholderAccountKey);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("settings");
    }

    // ====================================================================
    // Test 7 — Empty credential is rejected
    // ====================================================================
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateClientRejectsEmptyCredential(string credential)
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(AccountName);

        // Act
        Action act = () => AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.AccountKey,
            credential);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(credential));
    }

    // ====================================================================
    // Test 8 — Unknown auth kind is rejected
    // ====================================================================
    [Fact]
    public void CreateClientRejectsUnknownAuthKind()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(AccountName);
        var unknownKind = (AzureBlobAuthKind)999;

        // Act
        Action act = () => AzureBlobProviderFactory.CreateClient(
            settings,
            unknownKind,
            PlaceholderAccountKey);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("authKind");
    }

    // ====================================================================
    // Test 9 — SAS token with internal whitespace is rejected
    // ====================================================================
    [Fact]
    public void CreateClientRejectsSasTokenWithInternalWhitespace()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(AccountName);
        var badToken = "sv=2024-11-04&sig=abc def";

        // Act
        Action act = () => AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.SasToken,
            badToken);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("rawSasToken")
            .WithMessage("*whitespace*");
    }

    // ====================================================================
    // Test 10 — SAS token with fragment character is rejected
    // ====================================================================
    [Fact]
    public void CreateClientRejectsSasTokenWithFragmentCharacter()
    {
        // Arrange
        var settings = new AzureBlobConnectionSettings(AccountName);
        var badToken = "sv=2024-11-04&sig=abc#fragment";

        // Act
        Action act = () => AzureBlobProviderFactory.CreateClient(
            settings,
            AzureBlobAuthKind.SasToken,
            badToken);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("rawSasToken")
            .WithMessage("*fragment*");
    }
}
