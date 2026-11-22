// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/EnvSafe/SafetyScannerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.EnvSafe
// since:    Phase A (EnvSafe)
// purpose:  Unit tests for SafetyScanner: clean placeholder output, and each
//           detection pattern (api-key, JWT, URL credentials, PEM, long
//           random, non-placeholder).
// ============================================================================

using DeploymentVault.Core.EnvSafe.Domain;
using DeploymentVault.Platform.EnvSafe.Safety;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.EnvSafe;

/// <summary>
/// Unit tests for <see cref="SafetyScanner"/>.
/// </summary>
public sealed class SafetyScannerTests
{
    private static SafetyScanner NewScanner() => new();

    [Fact]
    public void CleanPlaceholderOutputHasNoWarnings()
    {
        var output = "API_KEY=your_api_key_here\nDB_HOST=your_host_here\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        Assert.Empty(warnings);
    }

    [Fact]
    public void DetectsRemainingApiKey()
    {
        var output = "API_KEY=sk_live_" + new string('x', 20) + "123456\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        Assert.Contains(warnings, w => w.Kind == "api-key-shaped");
    }

    [Fact]
    public void DetectsJwt()
    {
        var jwt = "eyJ" + new string('a', 16) + "." + new string('b', 16) + "." + new string('c', 16);
        var output = $"TOKEN={jwt}\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        Assert.Contains(warnings, w => w.Kind == "jwt-shaped");
    }

    [Fact]
    public void DetectsUrlWithCredentials()
    {
        var output = "DATABASE_URL=postgres://user:secretpassword@db.example.com:5432/app\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        Assert.Contains(warnings, w => w.Kind == "url-with-credentials");
    }

    [Fact]
    public void DetectsPemPrivateKey()
    {
        var output = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA...\n-----END RSA PRIVATE KEY-----\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        Assert.Contains(warnings, w => w.Kind == "pem-private-key");
    }

    [Fact]
    public void DetectsNonPlaceholderValue()
    {
        var output = "API_KEY=some_random_value_that_is_not_a_placeholder\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        Assert.Contains(warnings, w => w.Kind == "non-placeholder-value");
    }

    [Fact]
    public void UpperStylePlaceholdersNotFlagged()
    {
        var output = "API_KEY=YOUR_API_KEY_HERE\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.UpperXHere);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ChangeMePlaceholdersNotFlagged()
    {
        var output = "API_KEY=CHANGE_ME\nDB_HOST=CHANGE_ME\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.ChangeMe);
        Assert.Empty(warnings);
    }

    [Fact]
    public void WarningHasLineNumber()
    {
        var output = "API_KEY=your_api_key_here\nSECRET=sk_live_" + new string('x', 20) + "123456\n";
        var warnings = NewScanner().Scan(output, PlaceholderStyle.YourXHere);
        var warning = Assert.Single(warnings);
        Assert.Equal(2, warning.LineNumber);
    }
}
