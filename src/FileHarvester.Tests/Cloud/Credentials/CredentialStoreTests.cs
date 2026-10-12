// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Credentials/CredentialStoreTests.cs
// PURPOSE:  Unit tests for CredentialKey, the four ICredentialStore
//           implementations, and the DI registration. Real Windows
//           Credential Manager tests are platform-gated, use unique
//           keys, and clean up in finally.
// RELATION: Consumes FileHarvester.Platform.Cloud.Credentials.
// ============================================================================

using FileHarvester.Platform.Cloud.Credentials;
using System.Runtime.Versioning;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileHarvester.Tests.Cloud.Credentials;

/// <summary>
/// Unit tests for the platform credential store.
/// </summary>
public sealed class CredentialStoreTests
{

    // ------------------------------------------------------------------
    // CredentialKey
    // ------------------------------------------------------------------

    [Fact]
    public void CredentialKeyToStoreKeyProducesExpectedFormat()
    {
        var key = new CredentialKey("aws-s3", "work-account");
        key.ToStoreKey().Should().Be("file-ferry/aws-s3/work-account");
    }

    [Fact]
    public void CredentialKeyParseRoundTrips()
    {
        var original = new CredentialKey("cloudflare-r2", "prod");
        var composed = original.ToStoreKey();
        var parsed = CredentialKey.Parse(composed);

        parsed.ProfileId.Should().Be("cloudflare-r2");
        parsed.InstanceName.Should().Be("prod");
    }

    [Fact]
    public void CredentialKeyParseRejectsWrongPrefix()
    {
        var act = () => CredentialKey.Parse("other/aws-s3/work");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void CredentialKeyParseRejectsWrongPartCount()
    {
        var act = () => CredentialKey.Parse("file-ferry/aws-s3");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void CredentialKeyToStoreKeyRejectsSeparatorInProfileId()
    {
        var key = new CredentialKey("aws/s3", "work");
        var act = () => key.ToStoreKey();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CredentialKeyToStoreKeyRejectsSeparatorInInstanceName()
    {
        var key = new CredentialKey("aws-s3", "work/account");
        var act = () => key.ToStoreKey();
        act.Should().Throw<InvalidOperationException>();
    }

    // ------------------------------------------------------------------
    // MacOsKeychainStore
    // ------------------------------------------------------------------

    [Fact]
    public async Task MacOsKeychainStoreWriteAsyncThrowsCloudCredentialUnavailable()
    {
        var store = new MacOsKeychainStore();
        var key = new CredentialKey("aws-s3", "test");
        var act = async () => await store.WriteAsync(key, "secret");
        var ex = await act.Should().ThrowAsync<CloudCredentialUnavailableException>();
        ex.Which.PlatformName.Should().Be("macOS");
        ex.Which.Message.Should().Contain("macOS");
    }

    [Fact]
    public async Task MacOsKeychainStoreReadAsyncThrowsCloudCredentialUnavailable()
    {
        var store = new MacOsKeychainStore();
        var key = new CredentialKey("aws-s3", "test");
        var act = async () => await store.ReadAsync(key);
        await act.Should().ThrowAsync<CloudCredentialUnavailableException>();
    }

    [Fact]
    public async Task MacOsKeychainStoreDeleteAsyncThrowsCloudCredentialUnavailable()
    {
        var store = new MacOsKeychainStore();
        var key = new CredentialKey("aws-s3", "test");
        var act = async () => await store.DeleteAsync(key);
        await act.Should().ThrowAsync<CloudCredentialUnavailableException>();
    }

    [Fact]
    public async Task MacOsKeychainStoreListInstanceNamesAsyncThrowsCloudCredentialUnavailable()
    {
        var store = new MacOsKeychainStore();
        var act = async () => await store.ListInstanceNamesAsync("aws-s3");
        await act.Should().ThrowAsync<CloudCredentialUnavailableException>();
    }

    // ------------------------------------------------------------------
    // LinuxSecretServiceStore
    // ------------------------------------------------------------------

    [Fact]
    public async Task LinuxSecretServiceStoreThrowsCloudCredentialUnavailable()
    {
        var store = new LinuxSecretServiceStore();
        var key = new CredentialKey("aws-s3", "test");
        var act = async () => await store.ReadAsync(key);
        var ex = await act.Should().ThrowAsync<CloudCredentialUnavailableException>();
        ex.Which.PlatformName.Should().Be("Linux");
    }

    // ------------------------------------------------------------------
    // NotSupportedCredentialStore
    // ------------------------------------------------------------------

    [Fact]
    public async Task NotSupportedCredentialStoreThrowsCloudCredentialUnavailable()
    {
        var store = new NotSupportedCredentialStore();
        var key = new CredentialKey("aws-s3", "test");
        var act = async () => await store.ReadAsync(key);
        await act.Should().ThrowAsync<CloudCredentialUnavailableException>();
    }

    // ------------------------------------------------------------------
    // WindowsCredentialStore - real Windows Credential Manager
    // Platform-gated. Uses a unique key per test. Cleans up in finally.
    // Never touches any production credential name.
    // ------------------------------------------------------------------

    [Fact]
    public async Task WindowsCredentialStoreRoundTripsAndCleansUp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // The runtime guard above ensures this method only proceeds on
        // Windows. The analyzer cannot prove this across the async
        // boundary, so the pragma is explicit and deliberate.
#pragma warning disable CA1416
        var uniqueInstance = "test-" + Guid.NewGuid().ToString("N");
        var key = new CredentialKey("file-ferry-tests", uniqueInstance);
        var store = new WindowsCredentialStore();

        try
        {
            var marker = "value-" + Guid.NewGuid().ToString("N");

            await store.WriteAsync(key, marker);
            var readBack = await store.ReadAsync(key);
            readBack.Should().Be(marker);

            await store.DeleteAsync(key);
            var afterDelete = await store.ReadAsync(key);
            afterDelete.Should().BeNull();
        }
        finally
        {
            try { await store.DeleteAsync(key); } catch (Exception) { }
        }
#pragma warning restore CA1416
    }

    [Fact]
    public async Task WindowsCredentialStoreListFindsTestInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // The runtime guard above ensures this method only proceeds on
        // Windows. The analyzer cannot prove this across the async
        // boundary, so the pragma is explicit and deliberate.
#pragma warning disable CA1416
        var uniqueInstance = "list-" + Guid.NewGuid().ToString("N");
        var key = new CredentialKey("file-ferry-tests", uniqueInstance);
        var store = new WindowsCredentialStore();

        try
        {
            await store.WriteAsync(key, "x");
            var instances = await store.ListInstanceNamesAsync("file-ferry-tests");
            instances.Should().Contain(uniqueInstance);
        }
        finally
        {
            try { await store.DeleteAsync(key); } catch (Exception) { }
        }
#pragma warning restore CA1416
    }

    // ------------------------------------------------------------------
    // DI registration
    // ------------------------------------------------------------------

    [Fact]
    public void AddFileHarvesterCloudCredentialsRegistersAStore()
    {
        var services = new ServiceCollection();
        services.AddFileHarvesterCloudCredentials();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ICredentialStore>();
        store.Should().NotBeNull();

        if (OperatingSystem.IsWindows())
        {
            store.Should().BeOfType<WindowsCredentialStore>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            store.Should().BeOfType<MacOsKeychainStore>();
        }
        else if (OperatingSystem.IsLinux())
        {
            store.Should().BeOfType<LinuxSecretServiceStore>();
        }
        else
        {
            store.Should().BeOfType<NotSupportedCredentialStore>();
        }
    }

    // ------------------------------------------------------------------
    // Cross-namespace isolation. The static listing must not return
    // OAuth keys, and the OAuth listing must not return static keys,
    // even when the same profile id holds both kinds.
    // ------------------------------------------------------------------

    [Fact]
    public async Task WindowsCredentialStoreOAuthRoundTrip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416
        var profileId = "file-ferry-tests-" + Guid.NewGuid().ToString("N");
        var key = CredentialStoreKey.OAuth(profileId, "oauth-instance");
        var store = new WindowsCredentialStore();

        try
        {
            await store.WriteAsync(key, "refresh-token-value");
            var readBack = await store.ReadAsync(key);
            readBack.Should().Be("refresh-token-value");

            await store.DeleteAsync(key);
            var afterDelete = await store.ReadAsync(key);
            afterDelete.Should().BeNull();
        }
        finally
        {
            try { await store.DeleteAsync(key); } catch (Exception) { }
        }
#pragma warning restore CA1416
    }

    [Fact]
    public async Task WindowsCredentialStoreStaticListDoesNotReturnOAuth()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416
        var profileId = "file-ferry-tests-" + Guid.NewGuid().ToString("N");
        var staticKey = CredentialStoreKey.Static(profileId, "static-instance");
        var oauthKey  = CredentialStoreKey.OAuth(profileId, "oauth-instance");
        var store = new WindowsCredentialStore();

        try
        {
            await store.WriteAsync(staticKey, "static-value");
            await store.WriteAsync(oauthKey, "oauth-value");

            var names = await store.ListInstanceNamesAsync(profileId);

            names.Should().Contain("static-instance");
            names.Should().NotContain("oauth-instance");
        }
        finally
        {
            try { await store.DeleteAsync(staticKey); } catch (Exception) { }
            try { await store.DeleteAsync(oauthKey); } catch (Exception) { }
        }
#pragma warning restore CA1416
    }

    [Fact]
    public async Task WindowsCredentialStoreOAuthListDoesNotReturnStatic()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416
        var profileId = "file-ferry-tests-" + Guid.NewGuid().ToString("N");
        var staticKey = CredentialStoreKey.Static(profileId, "static-instance");
        var oauthKey  = CredentialStoreKey.OAuth(profileId, "oauth-instance");
        var store = new WindowsCredentialStore();

        try
        {
            await store.WriteAsync(staticKey, "static-value");
            await store.WriteAsync(oauthKey, "oauth-value");

            var names = await store.ListInstanceNamesAsync(profileId, CredentialKind.OAuth);

            names.Should().Contain("oauth-instance");
            names.Should().NotContain("static-instance");
        }
        finally
        {
            try { await store.DeleteAsync(staticKey); } catch (Exception) { }
            try { await store.DeleteAsync(oauthKey); } catch (Exception) { }
        }
#pragma warning restore CA1416
    }

    [Fact]
    public async Task WindowsCredentialStoreOAuthListFindsTestInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416
        var profileId = "file-ferry-tests-" + Guid.NewGuid().ToString("N");
        var key = CredentialStoreKey.OAuth(profileId, "oauth-instance");
        var store = new WindowsCredentialStore();

        try
        {
            await store.WriteAsync(key, "oauth-value");
            var names = await store.ListInstanceNamesAsync(profileId, CredentialKind.OAuth);
            names.Should().Contain("oauth-instance");
        }
        finally
        {
            try { await store.DeleteAsync(key); } catch (Exception) { }
        }
#pragma warning restore CA1416
    }

}
