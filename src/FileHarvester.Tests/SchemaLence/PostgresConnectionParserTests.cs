using FluentAssertions;
using SchemaLence.Core.Connections;
using SchemaLence.Platform.Postgres;

namespace FileHarvester.Tests.SchemaLence;

public sealed class PostgresConnectionParserTests
{
    [Fact]
    public void ParseUriDecodesCredentialsAndDefaultsToRequiredSsl()
    {
        var parsed = PostgresConnectionParser.ParseUri(
            "  production  ",
            "postgresql://reader:p%40ss%3Aword@db.example.test:5433/inventory");

        parsed.Input.Name.Should().Be("production");
        parsed.Input.Host.Should().Be("db.example.test");
        parsed.Input.Port.Should().Be(5433);
        parsed.Input.Database.Should().Be("inventory");
        parsed.Input.Username.Should().Be("reader");
        parsed.Input.SslMode.Should().Be("Require");
        parsed.Password.Should().Be("p@ss:word");
    }

    [Theory]
    [InlineData("disable", "Disable")]
    [InlineData("verify-ca", "VerifyCA")]
    [InlineData("verify-full", "VerifyFull")]
    [InlineData("prefer", "Prefer")]
    public void ParseUriNormalizesSupportedSslModes(string requested, string expected)
    {
        var parsed = PostgresConnectionParser.ParseUri(
            "test",
            $"postgres://reader@localhost/db?sslmode={requested}");

        parsed.Input.SslMode.Should().Be(expected);
    }

    [Theory]
    [InlineData("mysql://reader@localhost/db")]
    [InlineData("postgres://reader@localhost/db?sslmode=allow")]
    [InlineData("postgres://reader@localhost/db?options=-c%20search_path%3Dpublic")]
    public void ParseUriRejectsUnsupportedSchemesAndOptions(string value)
    {
        var action = () => PostgresConnectionParser.ParseUri("test", value);

        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToProfileRejectsUnsupportedConnectionOptions()
    {
        var input = new DatabaseConnectionInput(
            "test", "localhost", 5432, "db", "reader", string.Empty, Options: "-c search_path=public");

        var action = () => PostgresConnectionParser.ToProfile("id", input);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*options are not supported*");
    }

    [Fact]
    public void BuildConnectionStringUsesBoundedTimeoutsAndDoesNotEnableErrorDetails()
    {
        var profile = new DatabaseConnectionProfile(
            "id", "test", "postgresql", "localhost", 5432, "db", "reader", "Require");

        var connectionString = PostgresConnectionParser.BuildConnectionString(profile, "secret");

        connectionString.Should().Contain("Timeout=10");
        connectionString.Should().Contain("Command Timeout=30");
        connectionString.Should().Contain("Include Error Detail=False");
        connectionString.Should().Contain("Password=secret");
    }
}
