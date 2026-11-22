// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/EnvSafe/EnvFileConverterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.EnvSafe
// since:    Phase A (EnvSafe)
// purpose:  Unit tests for EnvFileConverter: placeholder generation,
//           preservation of comments/blank lines/quoting/export prefix/
//           inline comments/ordering, placeholder styles, and the
//           no-KEY=value failure path.
// ============================================================================

using DeploymentVault.Core.EnvSafe.Domain;
using DeploymentVault.Platform.EnvSafe.Conversion;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.EnvSafe;

/// <summary>
/// Unit tests for <see cref="EnvFileConverter"/>.
/// </summary>
public sealed class EnvFileConverterTests
{
    private static EnvFileConverter NewConverter() => new();

    [Fact]
    public void ConvertsApiKeyToPlaceholder()
    {
        var result = NewConverter().Convert("API_KEY=sk-live-abc123\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("API_KEY=your_api_key_here\n", result.Output);
        Assert.Equal(1, result.VariablesDetected);
        Assert.Equal(1, result.ValuesReplaced);
    }

    [Fact]
    public void PreservesCommentsBlankLinesAndOrdering()
    {
        var input = "# Database settings\n\nDB_HOST=localhost\nDB_PORT=5432\n# end\n";
        var result = NewConverter().Convert(input, PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        var lines = result.Output!.Split('\n');
        Assert.Equal("# Database settings", lines[0]);
        Assert.Equal(string.Empty, lines[1]);
        Assert.Equal("DB_HOST=your_host_here", lines[2]);
        Assert.Equal("DB_PORT=your_port_here", lines[3]);
        Assert.Equal("# end", lines[4]);
        Assert.Equal(2, result.CommentsPreserved);
    }

    [Fact]
    public void PreservesQuotingStyle()
    {
        var result = NewConverter().Convert("SECRET=\"supersecret\"\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("SECRET=\"your_secret_here\"\n", result.Output);
    }

    [Fact]
    public void PreservesSingleQuotes()
    {
        var result = NewConverter().Convert("TOKEN='abc123'\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("TOKEN='your_token_here'\n", result.Output);
    }

    [Fact]
    public void PreservesExportPrefix()
    {
        var result = NewConverter().Convert("export API_KEY=sk-123\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("export API_KEY=your_api_key_here\n", result.Output);
    }

    [Fact]
    public void PreservesInlineComments()
    {
        var result = NewConverter().Convert("API_KEY=sk-123 # rotate monthly\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("API_KEY=your_api_key_here # rotate monthly\n", result.Output);
    }

    [Fact]
    public void KnownSuffixShortensToSuffixSegments()
    {
        var result = NewConverter().Convert("STRIPE_API_KEY=sk_test_123\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("STRIPE_API_KEY=your_api_key_here\n", result.Output);
    }

    [Fact]
    public void UnknownKeyUsesFullSegments()
    {
        var result = NewConverter().Convert("MY_CUSTOM_VAR=value\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("MY_CUSTOM_VAR=your_my_custom_var_here\n", result.Output);
    }

    [Fact]
    public void UpperStyle()
    {
        var result = NewConverter().Convert("API_KEY=sk-123\n", PlaceholderStyle.UpperXHere);

        Assert.True(result.Success);
        Assert.Equal("API_KEY=YOUR_API_KEY_HERE\n", result.Output);
    }

    [Fact]
    public void AngleStyle()
    {
        var result = NewConverter().Convert("API_KEY=sk-123\n", PlaceholderStyle.AngleXHere);

        Assert.True(result.Success);
        Assert.Equal("API_KEY=<your_api_key_here>\n", result.Output);
    }

    [Fact]
    public void ChangeMeStyle()
    {
        var result = NewConverter().Convert("API_KEY=sk-123\nDB_HOST=localhost\n", PlaceholderStyle.ChangeMe);

        Assert.True(result.Success);
        Assert.Equal("API_KEY=CHANGE_ME\nDB_HOST=CHANGE_ME\n", result.Output);
    }

    [Fact]
    public void NoKeyValueFailsWithMessage()
    {
        var result = NewConverter().Convert("# just a comment\n\nno equals here\n", PlaceholderStyle.YourXHere);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("No valid environment variables", result.Error);
        Assert.Contains("KEY=value", result.Error);
    }

    [Fact]
    public void EmptyValueStillGetsPlaceholder()
    {
        var result = NewConverter().Convert("API_KEY=\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("API_KEY=your_api_key_here\n", result.Output);
    }

    [Fact]
    public void CommentContainingKeyValueIsNotConverted()
    {
        var result = NewConverter().Convert("# API_KEY=sk-123\nREAL_KEY=value\n", PlaceholderStyle.YourXHere);

        Assert.True(result.Success);
        Assert.Equal("# API_KEY=sk-123\nREAL_KEY=your_real_key_here\n", result.Output);
        Assert.Equal(1, result.VariablesDetected);
    }
}
