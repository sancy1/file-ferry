// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/EnvSafe/EnvFileValidatorTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.EnvSafe
// since:    Phase A (EnvSafe)
// purpose:  Unit tests for EnvFileValidator: KEY=value detection, comment
//           exclusion, export prefix, and counting.
// ============================================================================

using DeploymentVault.Platform.EnvSafe.Validation;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.EnvSafe;

/// <summary>
/// Unit tests for <see cref="EnvFileValidator"/>.
/// </summary>
public sealed class EnvFileValidatorTests
{
    private static EnvFileValidator NewValidator() => new();

    [Fact]
    public void DetectsSimpleKeyValue()
    {
        Assert.True(NewValidator().HasKeyValuePairs("API_KEY=sk-123\n"));
    }

    [Fact]
    public void DetectsExportPrefix()
    {
        Assert.True(NewValidator().HasKeyValuePairs("export API_KEY=sk-123\n"));
    }

    [Fact]
    public void DetectsEmptyValue()
    {
        Assert.True(NewValidator().HasKeyValuePairs("API_KEY=\n"));
    }

    [Fact]
    public void RejectsCommentsOnly()
    {
        Assert.False(NewValidator().HasKeyValuePairs("# API_KEY=sk-123\n# another\n"));
    }

    [Fact]
    public void RejectsBlankOnly()
    {
        Assert.False(NewValidator().HasKeyValuePairs("\n\n   \n"));
    }

    [Fact]
    public void RejectsPlainText()
    {
        Assert.False(NewValidator().HasKeyValuePairs("This is just a document.\nNo variables here.\n"));
    }

    [Fact]
    public void CountsKeyValuePairs()
    {
        var content = "# comment\nAPI_KEY=1\nDB_HOST=localhost\n\nexport TOKEN=abc\n";
        Assert.Equal(3, NewValidator().CountKeyValuePairs(content));
    }

    [Fact]
    public void CommentWithKeyValueNotCounted()
    {
        var content = "# API_KEY=sk-123\nREAL_KEY=value\n";
        Assert.Equal(1, NewValidator().CountKeyValuePairs(content));
    }
}
