// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/ApplicationNamePolicyTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault
// since:    Phase A (Deployment Vault registry)
// purpose:  Unit tests for ApplicationNamePolicy.Validate. Each test proves
//           one named rule and asserts a specific reason for the invalid cases.
//
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : DeploymentVault.Core.Domain.ApplicationNamePolicy
//   Used by    : test runner
//   See also   : ApplicationNamePolicy
//
// NOTES
//   - Pure-function tests. No database, no file, no clock, no random.
//   - Invalid cases assert a specific reason substring, not a generic failure.
//   - The trailing-whitespace case is NOT tested here. The policy does not
//     currently trim or reject leading/trailing whitespace; that is a
//     Phase A.1 improvement recorded as F-026.
// ============================================================================

using DeploymentVault.Core.Domain;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault;

/// <summary>
/// Unit tests for <see cref="ApplicationNamePolicy"/>.
/// </summary>
public sealed class ApplicationNamePolicyTests
{
    [Theory]
    [InlineData("MyApp")]
    [InlineData("my-app")]
    [InlineData("My_App.v2")]
    [InlineData("My App")]
    [InlineData("A")]
    [InlineData("Café")]
    [InlineData("Ünïcode_Nàme")]
    [InlineData("Café Prod")]
    public void ValidNamesAreAccepted(string name)
    {
        var result = ApplicationNamePolicy.Validate(name);
        Assert.True(result.IsValid, "Expected valid: " + name + " — got: " + result.Reason);
        Assert.Empty(result.Reason);
    }

    [Fact]
    public void NullNameIsRejectedWithRequiredReason()
    {
        var result = ApplicationNamePolicy.Validate(null);
        Assert.False(result.IsValid);
        Assert.Contains("required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyNameIsRejectedWithRequiredReason()
    {
        var result = ApplicationNamePolicy.Validate("");
        Assert.False(result.IsValid);
        Assert.Contains("required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhitespaceNameIsRejectedWithRequiredReason()
    {
        var result = ApplicationNamePolicy.Validate("   ");
        Assert.False(result.IsValid);
        Assert.Contains("required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TooLongNameIsRejected()
    {
        var tooLong = new string('a', ApplicationNamePolicy.MaxLength + 1);
        var result = ApplicationNamePolicy.Validate(tooLong);
        Assert.False(result.IsValid);
        Assert.Contains("characters or fewer", result.Reason);
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    public void PathSeparatorIsRejected(string name)
    {
        var result = ApplicationNamePolicy.Validate(name);
        Assert.False(result.IsValid);
        Assert.Contains("must not contain", result.Reason);
    }

    [Theory]
    [InlineData("bad:name")]
    [InlineData("bad*name")]
    [InlineData("bad?name")]
    [InlineData("bad\"name")]
    [InlineData("bad<name")]
    [InlineData("bad>name")]
    [InlineData("bad|name")]
    public void ReservedPunctuationIsRejected(string name)
    {
        var result = ApplicationNamePolicy.Validate(name);
        Assert.False(result.IsValid);
        Assert.Contains("must not contain", result.Reason);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void WindowsReservedDeviceNameIsRejected(string name)
    {
        var result = ApplicationNamePolicy.Validate(name);
        Assert.False(result.IsValid);
        Assert.Contains("reserved", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MyApp", "myapp", true)]
    [InlineData("MyApp", "MYAPP", true)]
    [InlineData("MyApp", "MyApp", true)]
    [InlineData("MyApp", "MyOtherApp", false)]
    [InlineData("Café", "café", true)]
    public void AreEquivalentIsCaseInsensitive(string a, string b, bool expected)
    {
        Assert.Equal(expected, ApplicationNamePolicy.AreEquivalent(a, b));
    }
}