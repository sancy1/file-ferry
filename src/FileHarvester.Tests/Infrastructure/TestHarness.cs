// ============================================================================
// FILE:     src/FileHarvester.Tests/Infrastructure/TestHarness.cs
// PURPOSE:  A shared test fixture used by every test in FileHarvester.Tests.
//           Creates a per-test temp directory, builds a real DI container
//           via AddFileHarvester(), exposes the resolved services, and
//           cleans up everything on Dispose.
// RELATION: Consumed by every test class in FileHarvester.Tests via
//           IClassFixture<TestHarness>. Also provides a static factory for
//           tests that need to register their own named policies (used by
//           BootstrapSmokeTests to isolate circuit state per test).
// ============================================================================

using System.Text;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;

namespace FileHarvester.Tests.Infrastructure;

/// <summary>
/// Shared fixture for FileHarvester tests. Creates a temp directory and a
/// fully-wired DI container. Disposed automatically by xUnit after the
/// last test in a test class that consumes it.
/// </summary>
public sealed class TestHarness : IDisposable
{
    public string TempRoot { get; }
    public ServiceProvider Provider { get; }
    public IFileResolver Resolver { get; }
    public IContentReader Reader { get; }
    public IBundleComposer Composer { get; }

    public TestHarness()
    {
        TempRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "file-ferry-tests",
            Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(TempRoot);

        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(TempRoot, "logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));

        Provider = services.BuildServiceProvider();
        Resolver = Provider.GetRequiredService<IFileResolver>();
        Reader   = Provider.GetRequiredService<IContentReader>();
        Composer = Provider.GetRequiredService<IBundleComposer>();
    }

    /// <summary>
    /// Writes a file into the temp directory and returns its NORMALIZED
    /// absolute path (native separators, canonical form).
    /// </summary>
    public string WriteFile(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var fullPath = Path.GetFullPath(Path.Combine(TempRoot, relativePath));
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        return fullPath;
    }

    /// <summary>
    /// Writes raw bytes into the temp directory and returns the path.
    /// </summary>
    public string WriteBytes(string relativePath, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(Path.Combine(TempRoot, relativePath));
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    /// <summary>
    /// Builds a FileRequest pointing at the given path with the given index.
    /// </summary>
    public static FileRequest Request(string path, int index = 0) =>
        new(Index: index, RawInput: path);

    /// <summary>
    /// Creates a fresh ServiceProvider with a single named policy,
    /// configured for isolated circuit and metric state. Used by smoke
    /// tests that need to observe circuit-opening behavior without
    /// interfering with the shared harness.
    /// </summary>
    /// <param name="policyName">The unique policy name for this test.</param>
    /// <param name="failureThreshold">Consecutive transient failures to trip the circuit.</param>
    /// <param name="maxAttempts">Retry attempts including the first.</param>
    /// <returns>A new provider. The caller must dispose it.</returns>
    public static ServiceProvider CreateProviderWithPolicy(
        string policyName,
        int failureThreshold = 3,
        int maxAttempts = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        var services = new ServiceCollection();

        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());

            r.AddPolicy(policyName, p =>
            {
                p.Retry.MaxAttempts           = maxAttempts;
                p.Retry.BaseDelayMs           = 10;
                p.Retry.JitterRatio           = 0.0;

                p.Circuit.FailureThreshold    = failureThreshold;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 5_000;
            });

            // Mirror the classification File-Ferry installs at startup.
            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.IO.IOException");
        });

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        Provider.Dispose();

        try
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort. Locks or antivirus can hold
            // files briefly after the test ends.
        }
    }
}
