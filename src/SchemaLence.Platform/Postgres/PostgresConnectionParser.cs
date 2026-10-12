using Npgsql;
using SchemaLence.Core.Connections;

namespace SchemaLence.Platform.Postgres;

public sealed record ParsedPostgresConnection(
    DatabaseConnectionInput Input,
    string? Password);

public static class PostgresConnectionParser
{
    public static ParsedPostgresConnection ParseUri(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("postgres" or "postgresql"))
        {
            throw new FormatException("Enter a valid PostgreSQL URI.");
        }

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var query = ParseQuery(uri.Query);
        if (query.ContainsKey("options"))
        {
            throw new FormatException(
                "PostgreSQL URI options are not supported. Configure server settings separately.");
        }
        string sslMode;
        try
        {
            sslMode = query.TryGetValue("sslmode", out var requestedSsl)
                ? NormalizeSslMode(requestedSsl)
                : "Require";
        }
        catch (ArgumentException ex)
        {
            throw new FormatException("The PostgreSQL URI contains an unsupported SSL mode.", ex);
        }

        var input = new DatabaseConnectionInput(
            name.Trim(),
            uri.Host,
            uri.IsDefaultPort ? 5432 : uri.Port,
            database,
            Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]),
            string.Empty,
            sslMode,
            null);

        var password = uri.UserInfo.Contains(':')
            ? Uri.UnescapeDataString(uri.UserInfo[(uri.UserInfo.IndexOf(':') + 1)..])
            : null;
        return new ParsedPostgresConnection(input, password);
    }

    public static DatabaseConnectionProfile ToProfile(
        string id,
        DatabaseConnectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);
        return new DatabaseConnectionProfile(
            id,
            input.Name.Trim(),
            "postgresql",
            input.Host.Trim(),
            input.Port,
            input.Database.Trim(),
            input.Username.Trim(),
            NormalizeSslMode(input.SslMode));
    }

    public static string BuildConnectionString(
        DatabaseConnectionProfile profile,
        string password)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            Username = profile.Username,
            Password = password,
            SslMode = Enum.Parse<SslMode>(NormalizeSslMode(profile.SslMode), ignoreCase: true),
            Timeout = 10,
            CommandTimeout = 30,
            ApplicationName = "SchemaLence",
            IncludeErrorDetail = false,
        };

        return builder.ConnectionString;
    }

    private static void Validate(DatabaseConnectionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)
            || string.IsNullOrWhiteSpace(input.Host)
            || string.IsNullOrWhiteSpace(input.Database)
            || string.IsNullOrWhiteSpace(input.Username))
        {
            throw new ArgumentException(
                "Name, host, database, and username are required.",
                nameof(input));
        }
        if (input.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Port must be between 1 and 65535.");
        }
        if (!string.IsNullOrWhiteSpace(input.Options))
        {
            throw new ArgumentException(
                "PostgreSQL connection options are not supported.",
                nameof(input));
        }
        _ = NormalizeSslMode(input.SslMode);
    }

    private static string NormalizeSslMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "require" => "Require",
            "verify-ca" or "verifyca" => "VerifyCA",
            "verify-full" or "verifyfull" => "VerifyFull",
            "prefer" => "Prefer",
            "disable" => "Disable",
            _ => throw new ArgumentException(
                "Choose a supported PostgreSQL SSL mode.",
                nameof(value)),
        };

        return normalized;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2
                ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                : string.Empty;
            values[key] = value;
        }
        return values;
    }
}
