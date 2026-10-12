// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/GcsProviderFactoryTests.cs
// PURPOSE:  Unit tests for GcsProviderFactory. Covers the credential-shape
//           validation rules, the settings validation, and the
//           no-JSON-retention contract of the factory's own object graph.
//           The positive construction test is deferred to C.5.6
//           [VERIFY-REAL]: a fabricated PEM would not prove the SDK path,
//           and C.5.4 must not accept an SDK construction failure as
//           success.
// RELATION: Exercises FileHarvester.Platform.Cloud.Providers.GcsProviderFactory.
//           Every credential in this file is a placeholder; no real secret
//           appears anywhere. The factory is static and pure, so no mocking
//           is required.
// ============================================================================

using System.Reflection;
using FileHarvester.Platform.Cloud.Providers;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>
/// Unit tests for <see cref="GcsProviderFactory"/> credential validation.
/// </summary>
public sealed class GcsProviderFactoryTests
{
    // ---- Placeholder credentials — never real values --------------------

    private const string ValidProjectId = "placeholder-project";

    // A minimal well-formed service-account JSON for validation tests.
    // The private_key is a placeholder string, not a real PEM; it is
    // never handed to the SDK in the C.5.4 tests (validation rejects
    // before construction, and the happy path is deferred to C.5.6).
    private const string ValidJson =
        "{" +
        "\"type\":\"service_account\"," +
        "\"client_email\":\"placeholder@example.invalid\"," +
        "\"private_key\":\"-----BEGIN PLACEHOLDER KEY-----\"," +
        "\"project_id\":\"placeholder-project\"" +
        "}";

    private static string JsonMissing(string field)
    {
        switch (field)
        {
            case "client_email":
                return "{\"type\":\"service_account\",\"private_key\":\"-----BEGIN PLACEHOLDER KEY-----\",\"project_id\":\"placeholder-project\"}";
            case "private_key":
                return "{\"type\":\"service_account\",\"client_email\":\"placeholder@example.invalid\",\"project_id\":\"placeholder-project\"}";
            case "project_id":
                return "{\"type\":\"service_account\",\"client_email\":\"placeholder@example.invalid\",\"private_key\":\"-----BEGIN PLACEHOLDER KEY-----\"}";
            default:
                return "{}";
        }
    }

    private static GcsConnectionSettings Settings()
        => new GcsConnectionSettings(ValidProjectId);

    // ====================================================================
    // Group 1 — validation (never reaches the SDK)
    // ====================================================================

    [Fact]
    public void CreateClientRejectsNullSettings()
    {
        Action act = () => GcsProviderFactory.CreateClient(settings: null!, ValidJson);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("settings");
    }

    [Fact]
    public void CreateClientRejectsNullJson()
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceAccountJson");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateClientRejectsEmptyJson(string json)
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), json);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson");
    }

    [Fact]
    public void CreateClientRejectsEmptyProjectId()
    {
        var settings = new GcsConnectionSettings(string.Empty);

        Action act = () => GcsProviderFactory.CreateClient(settings, ValidJson);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("settings");
    }

    [Fact]
    public void CreateClientRejectsMalformedJson()
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), "{ not json");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson")
            .WithMessage("*not valid JSON*");
    }

    [Fact]
    public void CreateClientRejectsNonObjectJson()
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), "[1,2,3]");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson")
            .WithMessage("*must be a JSON object*");
    }

    [Fact]
    public void CreateClientRejectsNonServiceAccountType()
    {
        var json = "{\"type\":\"authorized_user\",\"client_email\":\"placeholder@example.invalid\",\"private_key\":\"-----BEGIN PLACEHOLDER KEY-----\",\"project_id\":\"placeholder-project\"}";

        Action act = () => GcsProviderFactory.CreateClient(Settings(), json);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson")
            .WithMessage("*not a service-account credential*");
    }

    [Fact]
    public void CreateClientRejectsMissingClientEmail()
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), JsonMissing("client_email"));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson")
            .WithMessage("*client_email*");
    }

    [Fact]
    public void CreateClientRejectsMissingPrivateKey()
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), JsonMissing("private_key"));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson")
            .WithMessage("*private_key*");
    }

    [Fact]
    public void CreateClientRejectsMissingProjectId()
    {
        Action act = () => GcsProviderFactory.CreateClient(Settings(), JsonMissing("project_id"));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceAccountJson")
            .WithMessage("*project_id*");
    }

    // ====================================================================
    // Group 2 — secret hygiene (targets our code, not the SDK)
    // ====================================================================

    [Fact]
    public void ValidationMessagesNameFieldsButNeverValues()
    {
        // The messages name the field but never the value. This is the
        // secret-hygiene contract stated in the factory XML doc.
        var missingKey = JsonMissing("private_key");

        Action act = () => GcsProviderFactory.CreateClient(Settings(), missingKey);

        act.Should().Throw<ArgumentException>()
            .Which.Message.Should()
            .Contain("private_key")
            .And.NotContain("-----BEGIN PLACEHOLDER KEY-----");
    }

    [Fact]
    public void FactoryHasNoInstanceState()
    {
        // A static class has no instance fields. This is the strongest
        // available assertion that the factory cannot retain the JSON.
        var type = typeof(GcsProviderFactory);

        type.IsAbstract.Should().BeTrue();
        type.IsSealed.Should().BeTrue();

        var instanceFields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        instanceFields.Should().BeEmpty();
    }

    [Fact]
    public void GcsClientAdapterDoesNotRetainJsonString()
    {
        // The adapter takes a StorageClient, not the JSON, so it cannot
        // retain the JSON. This test asserts that fact against our own
        // object graph — no SDK internals are inspected.
        //
        // We cannot construct a real StorageClient here (the positive
        // path is deferred to C.5.6), so we assert on the adapter type's
        // instance fields: none is a string. A string field would be the
        // only way the adapter could retain the JSON.
        var adapterType = typeof(GcsClientAdapter);

        var stringFields = adapterType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(string))
            .ToArray();

        stringFields.Should().BeEmpty();
    }
}
