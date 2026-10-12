using System.Diagnostics;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Providers;

namespace SchemaLence.Platform.Postgres;

public sealed class PostgresBackupService : IPostgresBackupService
{
    public async Task CreateCustomArchiveAsync(
        DatabaseConnectionProfile profile,
        string password,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "pg_dump",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("--format=custom");
        startInfo.ArgumentList.Add("--no-password");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(Path.GetFullPath(destinationPath));
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(profile.Host);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(profile.Port.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(profile.Username);
        startInfo.ArgumentList.Add("--dbname");
        startInfo.ArgumentList.Add(profile.Database);
        startInfo.Environment["PGPASSWORD"] = password;
        startInfo.Environment["PGSSLMODE"] = profile.SslMode.ToLowerInvariant() switch
        {
            "verifyfull" => "verify-full",
            "verifyca" => "verify-ca",
            var sslMode => sslMode,
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("pg_dump could not be started.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "PostgreSQL backup requires pg_dump. Install the PostgreSQL client tools "
                + "and ensure pg_dump is available on PATH.",
                exception);
        }

        var errorTask = ReadDiagnosticAsync(process.StandardError, cancellationToken);
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(
            Stream.Null, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var errorOutput = await errorTask.ConfigureAwait(false);
            await outputTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var diagnostic = RedactDiagnostic(
                    errorOutput,
                    password,
                    profile.Host,
                    profile.Username,
                    profile.Database,
                    Path.GetFullPath(destinationPath));
                throw new InvalidOperationException(
                    $"pg_dump failed with exit code {process.ExitCode}."
                    + (string.IsNullOrWhiteSpace(diagnostic)
                        ? " Check database permissions and PostgreSQL client compatibility."
                        : " " + diagnostic));
            }

            var archiveInfo = new FileInfo(destinationPath);
            if (!archiveInfo.Exists || archiveInfo.Length == 0)
            {
                throw new InvalidOperationException(
                    "pg_dump completed without producing a non-empty archive.");
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            startInfo.Environment.Remove("PGPASSWORD");
        }
    }

    private static async Task<string> ReadDiagnosticAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        const int maximumCharacters = 4096;
        var buffer = new char[1024];
        var diagnostic = new System.Text.StringBuilder(maximumCharacters);
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(
            buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = maximumCharacters - diagnostic.Length;
            if (remaining > 0)
            {
                diagnostic.Append(buffer, 0, Math.Min(count, remaining));
            }
            if (count > remaining)
            {
                truncated = true;
            }
        }

        if (truncated)
        {
            diagnostic.Append(" [diagnostic truncated]");
        }
        return diagnostic.ToString().Trim();
    }

    private static string RedactDiagnostic(string diagnostic, params string[] secrets)
    {
        foreach (var secret in secrets.Where(value => !string.IsNullOrEmpty(value)))
        {
            diagnostic = diagnostic.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }
        return diagnostic;
    }
}
